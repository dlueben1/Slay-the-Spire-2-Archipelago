using System.Text.Json;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Data;
using StS2AP.Extensions;
using STS2RitsuLib.Networking.ManagedActions;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Utils;

/// <summary>
/// Owns the host-authoritative multiplayer ascension set. Existing host receipts are frozen into
/// lobby run data before native player/map construction; each live receipt becomes one ordered
/// non-combat managed action admitted only at an idle boundary outside room transitions.
/// </summary>
public static class AscensionMultiplayer
{
    private const int SchemaVersion = 1;
    private const string ActionKey = "ascension_down_v1";

    private static readonly RitsuLibManagedNetActionDescriptor<ApAscensionDownActionMessage>
        ActionDescriptor = new(
            ModuleId: ModEntry.ModId,
            ActionKey: ActionKey,
            Serialize: static message => JsonSerializer.SerializeToUtf8Bytes(message),
            Deserialize: DeserializeMessage,
            Execute: ExecuteAction,
            ActionType: GameActionType.NonCombat
        );

    private static readonly HashSet<AscensionLevel> ConstructionCurrent = new();
    private static StartRunLobby? _constructionLobby;
    private static string? _constructionError;
    private static bool _constructionStateReady;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
            return;

        RitsuLibManagedNetActions.Register(ActionDescriptor);
        _initialized = true;
    }

    public static void EndRun()
    {
        ConstructionCurrent.Clear();
        _constructionLobby = null;
        _constructionError = null;
        _constructionStateReady = false;
    }

    public static bool TryBuildLobbyState(
        StartRunLobby lobby,
        ArchipelagoSettings hostSettings,
        IReadOnlyList<ItemInfo> receivedItems,
        out long hostCharacterOffset,
        out List<int> configuredAscensions,
        out List<int> currentAscensions,
        out List<int> handledReceiptIndexes,
        out string reason)
    {
        hostCharacterOffset = 0;
        configuredAscensions = new List<int>();
        currentAscensions = new List<int>();
        handledReceiptIndexes = new List<int>();

        var character = BetaMainCompatibility.GetLocalCharacter(lobby);
        if (!hostSettings.Characters.TryGetValue(
                character.Id.Entry,
                out CharacterConfig? config))
        {
            reason = $"The host character {character.Id.Entry} has no AP ascension configuration.";
            return false;
        }

        hostCharacterOffset = config.CharOffset;
        var configured = new HashSet<AscensionLevel>();
        foreach (string rawLevel in config.Ascension)
        {
            AscensionLevel? level = AscensionManager.GetLevel(rawLevel);
            if (level is > AscensionLevel.None)
                configured.Add(level.Value);
        }

        var current = new HashSet<AscensionLevel>(configured);
        for (int index = 0; index < receivedItems.Count; index++)
        {
            ItemInfo item = receivedItems[index];
            if (!CoopSlot.Owns(item.ItemId)) continue;
            if (ArchipelagoIdCodec.IsUniversalItemId(item.ItemId)
                || item.GetAPCharacterNumber() != hostCharacterOffset
                || !TryGetAscensionLevel(item, out AscensionLevel level))
            {
                continue;
            }

            handledReceiptIndexes.Add(index + 1);
            current.Remove(level);
        }

        configuredAscensions = configured.Select(level => (int)level).Order().ToList();
        currentAscensions = current.Select(level => (int)level).Order().ToList();
        handledReceiptIndexes.Sort();
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Makes the lobby-frozen host set available while native multiplayer construction runs,
    /// before RunManager exposes a committed RunState.
    /// </summary>
    public static bool PrepareRunConstruction(
        StartRunLobby lobby,
        out int nativeAscensionLevel,
        out string reason)
    {
        nativeAscensionLevel = 0;
        reason = string.Empty;
        if (!ApRunData.TryGetLobbySharedState(lobby, out ApRunSharedState shared))
        {
            reason = "the canonical host ascension state is missing from the lobby";
            _constructionError = reason;
            return false;
        }
        if (!ValidateSharedState(shared, out reason))
        {
            _constructionError = reason;
            return false;
        }

        SetConstructionProjection(shared);
        nativeAscensionLevel = shared.ConfiguredAscensions.Count == 0 ? 0 : 10;
        _constructionLobby = lobby;
        _constructionError = null;
        SyncLocalProjection(shared);
        LogUtility.Info(
            $"Prepared host multiplayer ascensions for construction: "
                + $"[{string.Join(",", shared.CurrentAscensions)}]"
        );
        return true;
    }

    /// <summary>
    /// Restores the construction projection from the canonical begin-run payload. RitsuLib calls
    /// this after importing RunSavedData and before the base game applies starting ascension
    /// effects, which is the first point where clients can read the host-authored shared block.
    /// </summary>
    public static bool PrepareRunConstruction(RunState runState, out string reason)
    {
        if (!MultiplayerSupport.ShouldRunReplicatedConstruction(
                MultiplayerFeature.AscensionEffects))
        {
            reason = string.Empty;
            return true;
        }

        if (!ApRunData.TryGetSharedState(runState, out ApRunSharedState shared))
        {
            reason = "the canonical host ascension state is missing from the run payload";
            _constructionError = reason;
            return false;
        }
        if (!ValidateSharedState(shared, out reason))
        {
            _constructionError = reason;
            return false;
        }

        SetConstructionProjection(shared);
        _constructionError = null;
        SyncLocalProjection(shared);
        return true;
    }

    public static bool IsLobbyConstructionPrepared(StartRunLobby lobby, out string reason)
    {
        bool prepared = _constructionStateReady && ReferenceEquals(_constructionLobby, lobby);
        reason = prepared
            ? string.Empty
            : _constructionError ?? "the host ascension set was not captured at lobby commit";
        return prepared;
    }

    public static void BeginRun(RunState runState, Player localPlayer)
    {
        if (!MultiplayerSupport.ShouldRunReplicatedConstruction(
                MultiplayerFeature.AscensionEffects))
            return;

        if (!ApRunData.TryGetSharedState(runState, out ApRunSharedState shared))
        {
            Fail("could not bind shared ascension state: no canonical state was saved");
            return;
        }
        if (!ValidateSharedState(shared, out string reason))
        {
            Fail($"could not bind shared ascension state: {reason}");
            return;
        }

        SetConstructionProjection(shared);
        SyncLocalProjection(shared);

        if (RunManager.Instance.NetService.Type != NetGameType.Host
            || !MultiplayerSupport.IsLocalOwnApSlot
            || localPlayer.NetId != RunManager.Instance.NetService.NetId
            || !ArchipelagoClient.IsConnected)
        {
            return;
        }

        ArchipelagoSession? session = ArchipelagoClient.Session;
        if (session == null)
        {
            Fail("could not reconcile Ascension Down receipts: no active AP session");
            return;
        }

        IReadOnlyList<ItemInfo> receivedItems = session.Items.AllItemsReceived;
        var handled = shared.HandledAscensionDownReceiptIndexes.ToHashSet();
        for (int index = 0; index < receivedItems.Count; index++)
        {
            int receivedItemIndex = index + 1;
            ItemInfo item = receivedItems[index];
            if (!CoopSlot.Owns(item.ItemId)) continue;
            if (handled.Contains(receivedItemIndex)
                || ArchipelagoIdCodec.IsUniversalItemId(item.ItemId)
                || item.GetAPCharacterNumber() != shared.HostCharacterOffset
                || !TryGetAscensionLevel(item, out _))
            {
                continue;
            }

            RequestLiveReceipt(
                new IndexedItemInfo(item, receivedItemIndex),
                localPlayer,
                runState,
                shared
            );
        }
    }

    public static void ReceiveLiveReceipt(IndexedItemInfo receipt)
    {
        if (!MultiplayerSupport.IsRealMultiplayerRun
            || !MultiplayerSupport.IsLocalOwnApSlot
            || !MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.AscensionEffects)
            || RunManager.Instance.NetService.Type != NetGameType.Host
            || GameUtility.CurrentPlayer is not Player localPlayer
            || localPlayer.NetId != RunManager.Instance.NetService.NetId
            || localPlayer.RunState is not RunState runState
            || !ApRunData.TryGetSharedState(runState, out ApRunSharedState shared))
        {
            return;
        }

        RequestLiveReceipt(receipt, localPlayer, runState, shared);
    }

    public static void RefreshLobbyStagingForReceipt()
    {
        if (MultiplayerSupport.IsRealMultiplayerRun
            || !MultiplayerSupport.TryGetObservedStartLobby(out StartRunLobby lobby)
            || lobby.NetService.Type != NetGameType.Host)
        {
            return;
        }

        ApRunData.StageLocalPlayer(lobby);
    }

    public static void QueueReconnectReconciliation()
    {
        Callable.From(() =>
        {
            if (!MultiplayerSupport.IsRealMultiplayerRun
                || RunManager.Instance.DebugOnlyGetState() is not RunState runState
                || runState.GetPlayer(RunManager.Instance.NetService.NetId) is not Player localPlayer)
            {
                return;
            }

            BeginRun(runState, localPlayer);
        }).CallDeferred();
    }

    public static bool TryHasLevel(AscensionLevel level, out bool enabled)
    {
        if (!MultiplayerSupport.IsMultiplayerScope)
        {
            if (RunManager.Instance.IsInProgress)
            {
                enabled = ArchipelagoClient.Progress.Ascensions.HasLevel(level);
                return true;
            }

            enabled = false;
            return false;
        }

        if (MultiplayerSupport.IsRealMultiplayerRun
            && RunManager.Instance.DebugOnlyGetState() is RunState runState
            && ApRunData.TryGetSharedState(runState, out ApRunSharedState shared)
            && shared.AscensionStateInitialized)
        {
            enabled = shared.CurrentAscensions.Contains((int)level);
            return true;
        }

        if (MultiplayerSupport.IsMultiplayerScope && _constructionStateReady)
        {
            enabled = ConstructionCurrent.Contains(level);
            return true;
        }

        enabled = false;
        return false;
    }

    public static int GetCurrentCount()
    {
        if (MultiplayerSupport.IsRealMultiplayerRun
            && RunManager.Instance.DebugOnlyGetState() is RunState runState
            && ApRunData.TryGetSharedState(runState, out ApRunSharedState shared)
            && shared.AscensionStateInitialized)
        {
            return shared.CurrentAscensions.Count;
        }

        return _constructionStateReady
            ? ConstructionCurrent.Count
            : ArchipelagoClient.Progress.Ascensions.CurrentAscension.Count;
    }

    public static void SyncLocalProjection(RunState runState)
    {
        if (ApRunData.TryGetSharedState(runState, out ApRunSharedState shared))
            SyncLocalProjection(shared);
    }

    private static void RequestLiveReceipt(
        IndexedItemInfo receipt,
        Player owner,
        RunState runState,
        ApRunSharedState shared)
    {
        if (!ValidateSharedState(shared, out string reason))
        {
            Fail($"could not author Ascension Down receipt {receipt.Index}: {reason}");
            return;
        }
        if (receipt.Index <= 0
            || ArchipelagoIdCodec.IsUniversalItemId(receipt.Item.ItemId)
            || !TryGetAscensionLevel(receipt.Item, out AscensionLevel level))
        {
            Fail($"could not author invalid Ascension Down receipt {receipt.Index}");
            return;
        }
        if (receipt.Item.GetAPCharacterNumber() != shared.HostCharacterOffset)
        {
            LogUtility.Info(
                $"Banked Ascension Down receipt {receipt.Index}; it does not belong to the "
                    + "fixed host's active character."
            );
            return;
        }
        if (!ApRunData.TryGetPlayerState(
                runState,
                owner.NetId,
                out ApPlayerRunState ownerState)
            || ownerState.Participation != ApParticipationKind.OwnApSlot
            || ownerState.ApSlotId == null)
        {
            Fail($"could not resolve the fixed host AP owner for receipt {receipt.Index}");
            return;
        }

        if (shared.HandledAscensionDownReceiptIndexes.Contains(receipt.Index))
            return;

        var message = new ApAscensionDownActionMessage
        {
            RunId = shared.RunId,
            ActionId = Guid.NewGuid(),
            OwnerNetId = owner.NetId,
            ApSlotId = ownerState.ApSlotId.Value,
            ReceivedItemIndex = receipt.Index,
            CharacterOffset = shared.HostCharacterOffset!.Value,
            AscensionLevel = (int)level,
        };

        ManagedActionRequestScheduler.RequestOrDefer(
            message.ActionId,
            $"Ascension Down receipt {receipt.Index}",
            () => RitsuLibManagedNetActions.Request(
                RunManager.Instance,
                ActionDescriptor,
                message,
                owner.NetId
            ),
            () => IsCurrentRequest(message),
            () => LogUtility.Info(
                $"Requested managed Ascension Down receipt {receipt.Index} "
                    + $"({level}) as {message.ActionId} at an idle noncombat boundary."
            ),
            reason => Fail(reason),
            canRequest: NonCombatActionAdmission.CreateGate(
                $"Ascension Down receipt {receipt.Index} ({level})")
        );
    }

    private static bool IsCurrentRequest(ApAscensionDownActionMessage message) =>
        MultiplayerSupport.IsRealMultiplayerRun
        && !MultiplayerSupport.ClaimsInvalidated
        && RunManager.Instance.DebugOnlyGetState() is RunState runState
        && ApRunData.TryGetSharedState(runState, out ApRunSharedState shared)
        && shared.RunId == message.RunId;

    private static ApAscensionDownActionMessage DeserializeMessage(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<ApAscensionDownActionMessage>(bytes) ?? new();
        }
        catch (JsonException ex)
        {
            LogUtility.Warn($"Could not deserialize managed Ascension Down payload: {ex.Message}");
            return new ApAscensionDownActionMessage();
        }
    }

    private static async Task ExecuteAction(
        RitsuLibManagedNetActionContext<ApAscensionDownActionMessage> context)
    {
        ApAscensionDownActionMessage message = context.Message;
        if (!TryValidateAction(message, context.Player, out RunState runState))
        {
            Fail($"invalid managed Ascension Down action {message.ActionId}");
            throw new InvalidOperationException($"Invalid Ascension Down action {message.ActionId}.");
        }

        try
        {
            AscensionLevel level = (AscensionLevel)message.AscensionLevel;
            // update the canonical state and record the receipt
            if (!ApRunData.TryApplyAscensionDown(
                    runState,
                    message.ReceivedItemIndex,
                    level,
                    out bool alreadyHandled,
                    out bool removed))
            {
                throw new InvalidOperationException("The shared ascension state could not be updated.");
            }

            SyncLocalProjection(runState);
            if (alreadyHandled)
            {
                LogUtility.Info(
                    $"Ignored already-handled Ascension Down receipt {message.ReceivedItemIndex}."
                );
                return;
            }

            if (!removed)
            {
                LogUtility.Info(
                    $"Ascension Down receipt {message.ReceivedItemIndex} ({level}) was an "
                        + "idempotent no-op because the level was already absent."
                );
                return;
            }

            // the state is synced and it wasn't already handled and the ascension wasn't already removed
            // so apply its effects to everyone, every player does this individually
            await ApplyRetrospectiveEffect(runState, level);
            LogUtility.Success(
                $"Managed Ascension Down applied {level} from receipt "
                    + $"{message.ReceivedItemIndex}."
            );
        }
        catch (Exception ex)
        {
            Fail($"managed Ascension Down {message.ActionId} failed: {ex.Message}", ex);
            throw;
        }
    }

    private static bool TryValidateAction(
        ApAscensionDownActionMessage message,
        Player owner,
        out RunState runState)
    {
        runState = null!;
        if (!MultiplayerSupport.IsRealMultiplayerRun
            || !MultiplayerSupport.ShouldRunReplicatedConstruction(
                MultiplayerFeature.AscensionEffects)
            || message.SchemaVersion != SchemaVersion
            || message.RunId == Guid.Empty
            || message.ActionId == Guid.Empty
            || message.OwnerNetId != owner.NetId
            || message.ReceivedItemIndex <= 0
            || message.AscensionLevel is < 1 or > 10
            || RunManager.Instance.DebugOnlyGetState() is not RunState current
            || !BetaMainCompatibility.TryGetHostNetId(
                RunManager.Instance.NetService,
                out ulong hostNetId)
            || owner.NetId != hostNetId
            || !ApRunData.TryGetSharedState(current, out ApRunSharedState shared)
            || !ValidateSharedState(shared, out _)
            || shared.RunId != message.RunId
            || shared.HostCharacterOffset != message.CharacterOffset
            || owner.GetAPCharacterNumber() != message.CharacterOffset
            || !ApRunData.TryGetPlayerState(current, owner.NetId, out ApPlayerRunState ownerState)
            || ownerState.Participation != ApParticipationKind.OwnApSlot
            || ownerState.ApSlotId != message.ApSlotId)
        {
            return false;
        }

        runState = current;
        return true;
    }

    private static async Task ApplyRetrospectiveEffect(
        RunState runState,
        AscensionLevel level)
    {
        // Probably some duplication here compared to single-player but this applies to all players
        // maybe a future refactoring here could be nice
        switch (level)
        {
            case AscensionLevel.AscendersBane:
                foreach (Player player in runState.Players)
                {
                    var bane = player.Deck.Cards.OfType<AscendersBane>().FirstOrDefault();
                    if (bane != null)
                        await CardPileCmd.RemoveFromDeck(bane, showPreview: false);
                }
                break;

            case AscensionLevel.Poverty:
                foreach (Player player in runState.Players)
                {
                    if (!ApRunData.TryGetPlayerState(
                            runState,
                            player.NetId,
                            out ApPlayerRunState playerState))
                    {
                        continue;
                    }

                    int redeemed = playerState.Progress.GoldRedeemed;
                    int refund = redeemed - redeemed * 3 / 4;
                    if (refund > 0)
                        await PlayerCmd.GainGold(refund, player);
                }
                break;

            case AscensionLevel.TightBelt:
                foreach (Player player in runState.Players)
                    await PlayerCmd.GainMaxPotionCount(1, player);
                break;

            case AscensionLevel.DoubleBoss:
                foreach (var act in runState.Acts)
                    act.SetSecondBossEncounter(null);
                if (runState.Act.Index == 2
                    && runState.Map.SecondBossMapPoint is { } secondBossPoint)
                {
                    runState.Map.BossMapPoint.RemoveChildPoint(secondBossPoint);
                }
                break;
        }
    }

    private static bool ValidateSharedState(ApRunSharedState shared, out string reason)
    {
        var configured = shared.ConfiguredAscensions.ToHashSet();
        var current = shared.CurrentAscensions.ToHashSet();
        bool valid = shared.SchemaVersion == ApRunData.RunSchemaVersion
            && shared.RunId != Guid.Empty
            && shared.HostSettings != null
            && shared.AscensionStateInitialized
            && shared.HostCharacterOffset.HasValue
            && configured.All(level => level is >= 1 and <= 10)
            && current.All(level => level is >= 1 and <= 10)
            && current.IsSubsetOf(configured)
            && shared.HandledAscensionDownReceiptIndexes.All(index => index > 0);
        reason = valid ? string.Empty : "the canonical host ascension state is missing or invalid";
        return valid;
    }

    private static void SetConstructionProjection(ApRunSharedState shared)
    {
        ConstructionCurrent.Clear();
        ConstructionCurrent.UnionWith(
            shared.CurrentAscensions.Select(level => (AscensionLevel)level));
        _constructionStateReady = true;
    }

    private static void SyncLocalProjection(ApRunSharedState shared)
    {
        ArchipelagoClient.Progress.Ascensions.ReplaceLevels(
            shared.ConfiguredAscensions.Select(level => (AscensionLevel)level),
            shared.CurrentAscensions.Select(level => (AscensionLevel)level)
        );
    }

    private static bool TryGetAscensionLevel(ItemInfo item, out AscensionLevel level)
    {
        level = AscensionLevel.None;
        if (ArchipelagoIdCodec.IsUniversalItemId(item.ItemId))
            return false;

        APItem itemId = item.GetCharacterItemType();
        if (itemId is < APItem.SwarmingElites or > APItem.DoubleBoss)
            return false;

        level = AscensionManager.ToAscensionLevel(itemId);
        return level != AscensionLevel.None;
    }

    private static void Fail(string reason, Exception? ex = null)
    {
        LogUtility.Error(ex == null ? reason : $"{reason}\n{ex}");
        MultiplayerSupport.InvalidateRunClaims(reason);
        NotificationUtility.ShowRawText("Could not synchronize an Ascension Down item.");
    }
}
