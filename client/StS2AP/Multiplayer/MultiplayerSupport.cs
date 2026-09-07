using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Data;
using StS2AP.DomainAdapters;
using StS2AP.Patches;
using StS2AP.Utils;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Multiplayer;

/// <summary>
/// Owns the deliberately small experimental multiplayer profile. Singleplayer remains
/// unrestricted; real multiplayer fails closed unless a capability is listed in
/// <see cref="EnabledExperimentalFeatures"/>.
/// </summary>
public static class MultiplayerSupport
{
    // AP_MP: This is the master feature switchboard. Each enabled capability must have an
    // explicit replicated construction path and a single AP-side owner for external effects.
    private static readonly HashSet<MultiplayerFeature> EnabledExperimentalFeatures = new()
    {
        MultiplayerFeature.CharacterUnlocks,
        MultiplayerFeature.PressStartCheck,
        MultiplayerFeature.GoldRewards,
        MultiplayerFeature.CardRewards,
        MultiplayerFeature.RelicRewards,
        MultiplayerFeature.PotionRewards,
        MultiplayerFeature.AncientRewardChoices,
        MultiplayerFeature.CombatRewardLocations,
        MultiplayerFeature.FloorChecks,
        MultiplayerFeature.Shops,
        MultiplayerFeature.RestSites,
        MultiplayerFeature.Ancients,
        MultiplayerFeature.VictoryChecks,
        MultiplayerFeature.ProgressiveStarters,
        MultiplayerFeature.AscensionEffects,
        MultiplayerFeature.DeathLink,
        MultiplayerFeature.SaveAndReconnect,
    };

    internal static bool IsSynchronizedCombatActive =>
        CombatManager.Instance.IsStarting
        || !BetaMainCompatibility.IsActionSynchronizerCombatState(
            RunManager.Instance.ActionQueueSynchronizer.CombatState,
            ActionSynchronizerCombatState.NotInCombat
        );

    private static readonly Dictionary<int, IndexedItemInfo> DeferredItems = new();

    private static NCharacterSelectScreen? _observedStartLobbyScreen;
    private static bool _claimInvalidationNoticeShown;
    private static bool _apHistoryPrepared;
    private static ApSessionIdentity? _deferredSessionIdentity;
    private static ApSessionIdentity? _preparedSessionIdentity;
    private static ApParticipationKind? _activeParticipation;
    private static IReadOnlyList<ItemInfo> _preparedReceivedItems = Array.Empty<ItemInfo>();

    public static ApPlayDestination PendingDestination { get; private set; } =
        ApPlayDestination.None;

    public static ApParticipationKind PendingParticipation { get; private set; } =
        ApParticipationKind.VanillaGuest;

    public static bool IsRealMultiplayerRun { get; private set; }

    public static bool IsExperimentalMultiplayerRun => IsRealMultiplayerRun;

    public static bool IsLocalGuest => IsRealMultiplayerRun
        ? _activeParticipation == ApParticipationKind.VanillaGuest
        : PendingDestination == ApPlayDestination.Multiplayer
            && PendingParticipation == ApParticipationKind.VanillaGuest;

    public static bool IsLocalOwnApSlot => IsRealMultiplayerRun
        ? _activeParticipation == ApParticipationKind.OwnApSlot
        : PendingDestination == ApPlayDestination.Multiplayer
            && PendingParticipation == ApParticipationKind.OwnApSlot;

    public static bool IsLocalApParticipant =>
        !IsLocalGuest && (IsRealMultiplayerRun || PendingDestination == ApPlayDestination.Multiplayer);

    public static bool UsesFrozenHostSettings => IsRealMultiplayerRun
        && IsLocalOwnApSlot
        && RunManager.Instance.NetService.Type == NetGameType.Host;

    public static bool ClaimsInvalidated { get; private set; }

    /// <summary>
    /// True while the player is entering multiplayer or is already in a real multiplayer run.
    /// The pending intent is needed because AP items can arrive in the lobby before RunManager
    /// has a RunState.
    /// </summary>
    public static bool IsMultiplayerScope =>
        IsRealMultiplayerRun || PendingDestination == ApPlayDestination.Multiplayer;

    public static IReadOnlyCollection<IndexedItemInfo> PendingUnsupportedItems =>
        DeferredItems.Values.OrderBy(item => item.Index).ToArray();

    internal static ApSlotIdentity? PreparedSlotIdentity => _preparedSessionIdentity?.Slot;

    public static string? PreparedApRoomSeed => _preparedSessionIdentity?.RoomSeed;

    public static int? PreparedApTeamId => _preparedSessionIdentity?.ApTeamId;

    public static int? PreparedApSlotId => _preparedSessionIdentity?.ApSlotId;

    public static bool InitialItemsLoaded => _apHistoryPrepared;

    /// <summary>
    /// Exposes only the currently displayed start lobby for read-only diagnostics. This is not
    /// a saved lobby handle: clients normally see their own staged AP contribution, while the
    /// host's lobby session contains the contributions merged from every peer.
    /// </summary>
    public static bool TryGetObservedStartLobby(out StartRunLobby lobby)
    {
        NCharacterSelectScreen? screen = _observedStartLobbyScreen;
        if (screen != null && GodotObject.IsInstanceValid(screen))
        {
            lobby = screen.Lobby;
            return true;
        }

        lobby = null!;
        return false;
    }

    internal static NCharacterSelectScreen? GetObservedStartLobbyScreen(StartRunLobby lobby)
    {
        NCharacterSelectScreen? screen = _observedStartLobbyScreen;
        return screen != null && GodotObject.IsInstanceValid(screen)
            && ReferenceEquals(screen.Lobby, lobby) ? screen : null;
    }

    /// <summary>
    /// Requests re-evaluation of the host's Ready UI after authoritative lobby staging changes
    /// or the final launch guard rejects a race. Defer the Godot work because either call can
    /// occur inside a network handler.
    /// </summary>
    public static void RequestHostLobbyRefresh(StartRunLobby lobby)
    {
        NCharacterSelectScreen? screen = _observedStartLobbyScreen;
        if (screen == null
            || !GodotObject.IsInstanceValid(screen)
            || !ReferenceEquals(screen.Lobby, lobby))
        {
            return;
        }

        Callable.From(RefreshObservedStartLobby).CallDeferred();
    }

    public static void ClearPendingPlaySelection()
    {
        PendingDestination = ApPlayDestination.None;
        PendingParticipation = ApParticipationKind.VanillaGuest;
    }

    public static void BeginMultiplayerEntry()
    {
        PendingParticipation = ArchipelagoClient.IsConnected
            ? ApParticipationKind.OwnApSlot
            : ApParticipationKind.VanillaGuest;
        SelectDestination(ApPlayDestination.Multiplayer);
    }

    public static void BeginApBoundMultiplayerEntry()
    {
        PendingParticipation = ApParticipationKind.OwnApSlot;
        SelectDestination(ApPlayDestination.Multiplayer);
    }

    public static void SelectDestination(ApPlayDestination destination)
    {
        PendingDestination = destination;

        // The user can switch flows without reconnecting to AP. Keep the already-created
        // Death Link service aligned with the newly selected capability profile.
        if (ArchipelagoClient.IsConnected)
        {
            DeathLinkService? deathLinkController = ArchipelagoClient.DeathLinkController;
            if (deathLinkController == null)
            {
                LogUtility.Error(
                    "Cannot update Death Link for the selected play destination because "
                        + "the service is unavailable."
                );
            }
            else if (DeathLinkUtility.IsDeathLinkEnabled)
                deathLinkController.EnableDeathLink();
            else
                deathLinkController.DisableDeathLink();

            if (destination == ApPlayDestination.Singleplayer)
                PendingCheckUtility.ReconcileAndSend();
        }

        LogUtility.Info($"Selected AP play destination: {destination}");
    }

    public static bool IsFeatureEnabled(MultiplayerFeature feature)
    {
        if (!IsMultiplayerScope)
            return true;

        if (IsLocalGuest)
            return false;

        return EnabledExperimentalFeatures.Contains(feature);
    }

    /// <summary>
    /// Shop inventories are local presentation state. Directly connected AP players apply
    /// their AP source's slot unlocks only to the locally displayed inventory; Vanilla Guests
    /// and remote inventory replicas remain native.
    /// </summary>
    public static bool ShouldApplyLocalShopUnlocks(Player player) =>
        IsFeatureEnabled(MultiplayerFeature.Shops)
        && MultiplayerLocationChecks.IsLocalProgressOwner(player);

    /// <summary>
    /// An AP-check page is shown only to the process that can write checks for its local player.
    /// Players sharing an AP slot each use their own connection and local shop page.
    /// </summary>
    public static bool ShouldShowLocalShopChecks(Player player) =>
        ShouldApplyLocalShopUnlocks(player)
        && MultiplayerLocationChecks.IsCheckWriter(player);

    /// <summary>
    /// Feature gate for native callbacks that construct state for every player on every replica.
    /// Participant ownership is evaluated separately for the callback's concrete player.
    /// </summary>
    public static bool ShouldRunReplicatedConstruction(MultiplayerFeature feature)
    {
        if (!IsMultiplayerScope)
            return true;
        return EnabledExperimentalFeatures.Contains(feature);
    }

    public static MultiplayerFeature GetFeatureForItem(IndexedItemInfo indexedItem)
    {
        var item = indexedItem.Item;
        if (ArchipelagoIdCodec.IsUniversalItemId(item.ItemId))
            return IsUniversalCombatBuff(item.ItemId)
                ? MultiplayerFeature.GoldRewards
                : MultiplayerFeature.UnknownReceivedItems;

        return item.GetCharacterItemType() switch
        {
            APItem.Unlock => MultiplayerFeature.CharacterUnlocks,
            APItem.OneGold or APItem.FiveGold or APItem.CombatGold or APItem.EliteGold
                or APItem.BossGold => MultiplayerFeature.GoldRewards,
            APItem.CardReward or APItem.RareCardReward => MultiplayerFeature.CardRewards,
            APItem.Relic => MultiplayerFeature.RelicRewards,
            APItem.Potion => MultiplayerFeature.PotionRewards,
            APItem.ProgressiveRest or APItem.ProgressiveSmith => MultiplayerFeature.RestSites,
            APItem.ProgressiveAncient => MultiplayerFeature.AncientRewardChoices,
            APItem.ShopCardSlot or APItem.NeutralShopCardSlot or APItem.ShopRelicSlot
                or APItem.ShopPotionSlot or APItem.ProgressiveShopRemove =>
                    MultiplayerFeature.Shops,
            APItem.ProgressiveStarterCard or APItem.ProgressiveStarterRelic =>
                MultiplayerFeature.ProgressiveStarters,
            APItem.SwarmingElites or APItem.WearyTraveler or APItem.Poverty
                or APItem.TightBelt or APItem.AscenderBane or APItem.Inflation
                or APItem.Scarcity or APItem.ToughEnemies or APItem.DeadlyEnemies
                or APItem.DoubleBoss => MultiplayerFeature.AscensionEffects,
            _ => MultiplayerFeature.UnknownReceivedItems,
        };
    }

    // AP_MP: Unsupported receipt types are held here instead of mutating replicated state.
    public static bool ShouldDeferItem(IndexedItemInfo item) =>
        IsMultiplayerScope && !IsFeatureEnabled(GetFeatureForItem(item));

    public static void DeferItem(IndexedItemInfo item)
    {
        if (DeferredItems.TryAdd(item.Index, item))
        {
            LogUtility.Warn(
                $"Deferred AP item index {item.Index} ({item.Item.ItemName}); "
                    + $"multiplayer feature {GetFeatureForItem(item)} is disabled"
            );
        }
    }

    /// <summary>Rejects a reconnect that would replace the AP owner of an active STS lobby/run.</summary>
    public static bool ValidateApSessionIdentity(
        string roomSeed,
        int apTeamId,
        int apSlotId,
        out string reason)
    {
        reason = string.Empty;
        var candidate = ApSessionIdentity.Create(
            ArchipelagoClient.ServerAddress, roomSeed, apTeamId, apSlotId,
            ArchipelagoClient.LocalSettings.Value.MultiplayerPlayerNumber);
        bool identityLocked =
            ApReconnectController.IsActive
            || _observedStartLobbyScreen != null
            || IsRealMultiplayerRun || GameUtility.IsInRun || RunManager.Instance.IsInProgress;
        if (identityLocked
            && _preparedSessionIdentity is { } expected
            && expected != candidate)
        {
            reason = $"Expected AP session {expected}, but connected to {candidate}.";
            return false;
        }

        return true;
    }

    /// <summary>Records every successful login so deferred state cannot cross AP identities.</summary>
    public static void NoteApSessionConnected(string roomSeed, int apTeamId, int apSlotId)
    {
        var identity = ApSessionIdentity.Create(
            ArchipelagoClient.ServerAddress, roomSeed, apTeamId, apSlotId, CoopSlot.PlayerNumber);
        if (_deferredSessionIdentity != null && _deferredSessionIdentity != identity)
        {
            LogUtility.Info(
                $"Discarding {DeferredItems.Count} deferred multiplayer item(s) from the previous AP session"
            );
            DeferredItems.Clear();
            _apHistoryPrepared = false;
        }

        _preparedSessionIdentity = identity;
        _deferredSessionIdentity = identity;
    }

    /// <summary>
    /// Deterministically prepares the deliberately small multiplayer receipt profile while
    /// initial SDK callbacks are still blocked. Unsupported receipts are retained by index,
    /// character unlocks are replayed idempotently, and gold is rebuilt separately.
    /// </summary>
    public static bool PrepareApSession(
        string roomSeed,
        int apTeamId,
        int apSlotId,
        IReadOnlyList<ItemInfo> receivedItems,
        out string reason)
    {
        reason = string.Empty;
        if (!ValidateApSessionIdentity(roomSeed, apTeamId, apSlotId, out reason))
            return false;

        if (ArchipelagoClient.Settings?.Characters == null
            || ArchipelagoClient.Settings.Characters.Count == 0)
        {
            reason = "The AP slot did not provide any usable character settings.";
            return false;
        }
        if (!ArchipelagoClient.TryValidateConfiguredCharacters(
                ArchipelagoClient.Settings,
                out reason))
        {
            return false;
        }

        var identity = ApSessionIdentity.Create(
            ArchipelagoClient.ServerAddress, roomSeed, apTeamId, apSlotId, CoopSlot.PlayerNumber);

        DeferredItems.Clear();
        var receipts = new List<IndexedItemInfo>();
        ArchipelagoClient.Progress.ProgressiveAncients.Clear();
        ArchipelagoClient.Progress.ProgressiveRests.Clear();
        ArchipelagoClient.Progress.ProgressiveSmiths.Clear();
        ArchipelagoClient.Progress.ProgressiveStarterCards.Clear();
        ArchipelagoClient.Progress.ProgressiveStarterRelics.Clear();
        ArchipelagoClient.Progress.ProgressiveStarterCardBaseId = null;
        ArchipelagoClient.Progress.ProgressiveStarterCardUpgradedId = null;
        ArchipelagoClient.Progress.ProgressiveStarterCardTier =
            ProgressiveStarterTier.Unsupported;
        ArchipelagoClient.Progress.ProgressiveStarterRelicBaseId = null;
        ArchipelagoClient.Progress.ProgressiveStarterRelicUpgradedId = null;
        ArchipelagoClient.Progress.ProgressiveStarterRelicTier =
            ProgressiveStarterTier.Unsupported;
        ArchipelagoClient.Progress.ShopCardSlotsReceived.Clear();
        ArchipelagoClient.Progress.ShopNeutralSlotsReceived.Clear();
        ArchipelagoClient.Progress.ShopRelicSlotsReceived.Clear();
        ArchipelagoClient.Progress.ShopPotionSlotsReceived.Clear();
        ArchipelagoClient.Progress.ShopRemovesReceived.Clear();
        var ancientCounts = new Dictionary<long, int>();
        for (int index = 0; index < receivedItems.Count; index++)
        {
            ItemInfo item = receivedItems[index];
            if (!CoopSlot.Owns(item.ItemId))
                continue;
            var indexedItem = new IndexedItemInfo(item, index + 1);
            MultiplayerFeature feature = GetFeatureForItem(indexedItem);
            if (feature == MultiplayerFeature.CharacterUnlocks)
            {
                GameUtility.UnlockCharacter(item);
            }
            else if (feature == MultiplayerFeature.GoldRewards)
            {
                // Aggregate gold is reconstructed below rather than stored as discrete rows.
            }
            else if (!IsFeatureEnabled(feature))
            {
                DeferItem(indexedItem);
            }
            else if (ArchipelagoIdCodec.IsCharacterItemId(item.ItemId)
                && item.GetCharacterItemType() == APItem.ProgressiveAncient)
            {
                long characterOffset = item.GetAPCharacterNumber();
                ancientCounts.TryGetValue(characterOffset, out int count);
                count++;
                ancientCounts[characterOffset] = count;
                ArchipelagoClient.Progress.ProgressiveAncients[characterOffset] = count;

                receipts.Add(indexedItem);
            }
            else if (feature == MultiplayerFeature.RestSites
                && ArchipelagoIdCodec.IsCharacterItemId(item.ItemId))
            {
                Dictionary<long, int>? counts = item.GetCharacterItemType() switch
                {
                    APItem.ProgressiveRest => ArchipelagoClient.Progress.ProgressiveRests,
                    APItem.ProgressiveSmith => ArchipelagoClient.Progress.ProgressiveSmiths,
                    _ => null,
                };
                if (counts != null)
                {
                    long characterOffset = item.GetAPCharacterNumber();
                    counts.TryGetValue(characterOffset, out int count);
                    counts[characterOffset] = count + 1;
                }
            }
            else if (feature == MultiplayerFeature.ProgressiveStarters
                && ArchipelagoIdCodec.IsCharacterItemId(item.ItemId))
            {
                Dictionary<long, int>? counts = item.GetCharacterItemType() switch
                {
                    APItem.ProgressiveStarterCard =>
                        ArchipelagoClient.Progress.ProgressiveStarterCards,
                    APItem.ProgressiveStarterRelic =>
                        ArchipelagoClient.Progress.ProgressiveStarterRelics,
                    _ => null,
                };
                if (counts != null)
                {
                    long characterOffset = item.GetAPCharacterNumber();
                    counts.TryGetValue(characterOffset, out int count);
                    counts[characterOffset] = count + 1;
                }
            }
            else if (feature == MultiplayerFeature.Shops
                && ArchipelagoIdCodec.IsCharacterItemId(item.ItemId))
            {
                Dictionary<long, int>? counts = item.GetCharacterItemType() switch
                {
                    APItem.ShopCardSlot => ArchipelagoClient.Progress.ShopCardSlotsReceived,
                    APItem.NeutralShopCardSlot =>
                        ArchipelagoClient.Progress.ShopNeutralSlotsReceived,
                    APItem.ShopRelicSlot => ArchipelagoClient.Progress.ShopRelicSlotsReceived,
                    APItem.ShopPotionSlot => ArchipelagoClient.Progress.ShopPotionSlotsReceived,
                    APItem.ProgressiveShopRemove =>
                        ArchipelagoClient.Progress.ShopRemovesReceived,
                    _ => null,
                };
                if (counts != null)
                {
                    long characterOffset = item.GetAPCharacterNumber();
                    counts.TryGetValue(characterOffset, out int count);
                    counts[characterOffset] = count + 1;
                }
                receipts.Add(indexedItem);
            }
            else
            {
                receipts.Add(indexedItem);
            }
        }

        ArchipelagoClient.Progress.Items.ReplaceReceivedItems(receipts);
        ApGrantDispatcher.RebuildGoldBank(receivedItems);
        _preparedSessionIdentity = identity;
        _deferredSessionIdentity = identity;
        _preparedReceivedItems = receivedItems.ToArray();

        // Durable consumption and assignments are restored separately from the player's saved
        // ApRunProgressState snapshot. This flag says only that the transient receipt catalog is
        // complete enough to reconcile against that progress.
        _apHistoryPrepared = true;
        RefreshObservedStartLobby();
        LogUtility.Info(
            $"Prepared AP multiplayer session {identity}: receipts={receivedItems.Count}, "
                + $"deferred={DeferredItems.Count}"
        );
        return true;
    }

    public static void OnApDisconnected()
    {
        _apHistoryPrepared = false;
        // AP socket termination may be raised off the Godot main thread.
        Callable.From(RefreshObservedStartLobby).CallDeferred();
    }

    /// <summary>Called only after an intentional departure at the home screen.</summary>
    internal static void ForgetApSession()
    {
        EndRun();
        ClearPendingPlaySelection();
        _observedStartLobbyScreen = null;
        _apHistoryPrepared = false;
        _preparedSessionIdentity = null;
        _deferredSessionIdentity = null;
        _preparedReceivedItems = Array.Empty<ItemInfo>();
        DeferredItems.Clear();
    }

    public static bool CanEnterMultiplayerLobby(out string reason)
    {
        if (PendingParticipation == ApParticipationKind.VanillaGuest)
        {
            reason = string.Empty;
            return true;
        }

        if (!ArchipelagoClient.IsConnected)
        {
            reason = "This AP-bound player must reconnect before opening the multiplayer lobby.";
            return false;
        }

        if (!_apHistoryPrepared || _preparedSessionIdentity == null)
        {
            reason = "Archipelago is still preparing slot settings and received-item history.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Hosting is AP-authoritative, so only a process connected to and prepared for its own AP
    /// slot may create a lobby. Disconnected players remain free to enter the native Join flow.
    /// </summary>
    public static bool CanHostMultiplayer(out string reason)
    {
        if (!ArchipelagoClient.IsConnected)
        {
            reason = "Connect to an Archipelago slot before hosting multiplayer. You can still join as a guest.";
            return false;
        }

        if (PendingDestination != ApPlayDestination.Multiplayer
            || PendingParticipation != ApParticipationKind.OwnApSlot)
        {
            reason = "Archipelago has not prepared this player to host with its connected slot.";
            return false;
        }

        return CanEnterMultiplayerLobby(out reason);
    }

    public static bool CanEmbark(CharacterModel character, out string reason)
    {
        if (!CanEnterMultiplayerLobby(out reason))
            return false;

        if (PendingParticipation == ApParticipationKind.VanillaGuest)
        {
            reason = string.Empty;
            return true;
        }

        return ArchipelagoClient.CanSelectCharacter(character, out reason);
    }

    public static bool CanLaunchRun(RunState runState, out string reason)
    {
        Player? localPlayer = runState.Players.FirstOrDefault(
            player => player.NetId == RunManager.Instance.NetService.NetId
        );
        if (localPlayer == null)
        {
            reason = "The local STS multiplayer player could not be resolved.";
            return false;
        }

        if (ApRunData.TryGetLocalPlayerState(runState, localPlayer.NetId, out ApPlayerRunState savedState)
            && !ValidateReturningPlayerIdentity(savedState, out reason))
        {
            reason = "The saved campaign cannot be loaded by this AP identity: " + reason;
            return false;
        }

        return CanEmbark(localPlayer.Character, out reason);
    }

    public static void ObserveStartLobby(NCharacterSelectScreen screen)
    {
        if (PendingDestination != ApPlayDestination.Multiplayer)
            return;

        _observedStartLobbyScreen = screen;
        ApRunData.StageLocalPlayer(screen.Lobby);
        RefreshObservedStartLobby();
    }

    public static void StopObservingStartLobby(NCharacterSelectScreen screen)
    {
        if (ReferenceEquals(_observedStartLobbyScreen, screen))
            _observedStartLobbyScreen = null;
    }

    private static void RefreshObservedStartLobby()
    {
        NCharacterSelectScreen? screen = _observedStartLobbyScreen;
        if (screen == null || !GodotObject.IsInstanceValid(screen))
        {
            _observedStartLobbyScreen = null;
            return;
        }

        try
        {
            // Receipts can unlock characters while this screen is open; refresh the local
            // slot's visibility/unlocks before re-evaluating readiness.
            Patches_UnlockCharacters.OverrideCharacterSelectMenuOptions
                .RefreshForCurrentParticipation(screen);
            ApRunData.StageLocalPlayer(screen.Lobby);
            NConfirmButton embarkButton = screen.GetNode<NConfirmButton>("ConfirmButton");
            if (!CanEnterMultiplayerLobby(out _))
            {
                EnsureLocalPlayerUnready(screen);
                embarkButton.Disable();
                return;
            }

            if (screen.Lobby.NetService.Type == NetGameType.Host
                && !ApRunData.TryValidateHostLobbyContributions(
                    screen.Lobby,
                    out string hostBlockedReason))
            {
                bool wasReady = BetaMainCompatibility.IsLocalPlayerReady(screen.Lobby);
                EnsureLocalPlayerUnready(screen);
                embarkButton.Disable();
                if (wasReady)
                {
                    NotificationUtility.ShowRawText(
                        $"Host became unready: {hostBlockedReason}"
                    );
                }
                return;
            }

            if (BetaMainCompatibility.IsLocalPlayerReady(screen.Lobby))
                return;

            if (CanEmbark(BetaMainCompatibility.GetLocalCharacter(screen.Lobby), out _))
                embarkButton.Enable();
            else
                embarkButton.Disable();
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Could not refresh AP multiplayer lobby readiness: {ex.Message}");
        }
    }

    private static void EnsureLocalPlayerUnready(NCharacterSelectScreen screen)
    {
        if (!BetaMainCompatibility.IsLocalPlayerReady(screen.Lobby))
            return;

        // Use the native UI transition so auto-unready restores character buttons and the
        // waiting panel as well as changing the lobby flag.
        try
        {
            screen.OnUnreadyPressed(null!);
        }
        catch (Exception ex)
        {
            LogUtility.Warn(
                $"Native AP lobby auto-unready failed: {ex.GetBaseException().Message}"
            );
            screen.Lobby.SetReady(ready: false);
        }
    }

    public static IReadOnlyList<IndexedItemInfo> TakeDeferredItemsForSingleplayer()
    {
        if (PendingDestination != ApPlayDestination.Singleplayer || IsRealMultiplayerRun)
            return Array.Empty<IndexedItemInfo>();

        var items = DeferredItems.Values.OrderBy(item => item.Index).ToArray();
        DeferredItems.Clear();
        return items;
    }

    /// <summary>
    /// Binds AP ownership only after MegaCrit has assigned LocalContext in RunManager.Launch.
    /// </summary>
    public static Player? BeginRun(RunState runState)
    {
        EndRun();

        IsRealMultiplayerRun =
            RunManager.Instance.NetService.Type != NetGameType.Singleplayer;
        PendingDestination = IsRealMultiplayerRun
            ? ApPlayDestination.Multiplayer
            : ApPlayDestination.Singleplayer;

        if (!IsRealMultiplayerRun)
            return null;

        Player? localPlayer;
        try
        {
            localPlayer = LocalContext.GetMe(runState);
        }
        catch (Exception ex)
        {
            ClaimsInvalidated = true;
            LogUtility.Error($"Could not resolve the local multiplayer player: {ex.Message}");
            return null;
        }

        if (localPlayer == null)
        {
            ClaimsInvalidated = true;
            LogUtility.Error("Could not bind the local AP player after multiplayer launch");
            return null;
        }

        _activeParticipation = PendingParticipation;
        if (ApRunData.TryGetLocalPlayerState(runState, localPlayer.NetId, out var savedPlayerState))
        {
            var match = ParticipantAdapter.MatchReturning(
                savedPlayerState, PendingParticipation, PreparedSlotIdentity);
            if (match.IsOk)
            {
                _activeParticipation = (ApParticipationKind)match.ResultValue.Kind.WireValue;
            }
            else
            {
                ClaimsInvalidated = true;
                LogUtility.Error($"Saved AP multiplayer identity mismatch: {match.ErrorValue.Description}");
                Callable.From(() => NotificationUtility.ShowRawText(
                    "This saved campaign belongs to a different AP participation identity. "
                        + "AP progress and rewards are disabled for this run."
                )).CallDeferred();
                return localPlayer;
            }
        }

        if (_activeParticipation == ApParticipationKind.OwnApSlot
            && RunManager.Instance.NetService.Type == NetGameType.Host
            && ApRunData.TryGetSharedState(runState, out ApRunSharedState hostShared)
            && hostShared.HostSettings != null)
        {
            if (!ArchipelagoClient.TryUseMultiplayerHostSettings(
                    hostShared.HostSettings,
                    out string settingsReason))
            {
                InvalidateRunClaims(settingsReason);
            }
        }

        LogUtility.Info(
            $"Experimental AP multiplayer launched: netType={RunManager.Instance.NetService.Type}, "
                + $"localNetId={localPlayer.NetId}, "
                + $"players=[{string.Join(",", runState.Players.Select(p => p.NetId))}]"
        );
        return localPlayer;
    }

    private static bool ValidateReturningPlayerIdentity(
        ApPlayerRunState savedState,
        out string reason)
    {
        var match = ParticipantAdapter.MatchReturning(savedState, PendingParticipation, PreparedSlotIdentity);
        reason = match.IsOk ? string.Empty : match.ErrorValue.Description;
        return match.IsOk;
    }

    public static IReadOnlyList<ItemInfo> GetPreparedReceivedItems() => _preparedReceivedItems;

    /// <summary>
    /// Returns the current authoritative SDK history for a connected own-slot process. Lobby
    /// staging uses this instead of the connection-time snapshot so receipts received while the
    /// character screen is open are included before launch.
    /// </summary>
    public static IReadOnlyList<ItemInfo> GetCurrentOwnSlotReceivedItems()
    {
        if (!ArchipelagoClient.IsConnected)
            return _preparedReceivedItems;

        ArchipelagoSession? session = ArchipelagoClient.Session;
        if (session == null)
        {
            const string message = "Cannot read current AP receipts without an active session.";
            LogUtility.Error(message);
            throw new InvalidOperationException(message);
        }

        return session.Items.AllItemsReceived;
    }

    public static bool RestorePreparedReceiptView(out string reason)
    {
        if (_preparedSessionIdentity is not { } identity)
        {
            reason = "No AP receipt source is bound to the local multiplayer player.";
            return false;
        }

        // Login can complete before the SDK has replayed the slot's complete received-item
        // history. The launch boundary is later and must refresh an own-slot participant from
        // the authoritative SDK list instead of restoring the early connection snapshot.
        IReadOnlyList<ItemInfo> receivedItems = _preparedReceivedItems;
        ArchipelagoSession? session = ArchipelagoClient.Session;
        if (IsLocalOwnApSlot && ArchipelagoClient.IsConnected)
        {
            if (session == null)
            {
                reason = "The active AP session disappeared before receipt restoration.";
                LogUtility.Error(reason);
                return false;
            }
            receivedItems = session.Items.AllItemsReceived.ToArray();
        }

        if (!PrepareApSession(
            identity.RoomSeed,
            identity.ApTeamId,
            identity.ApSlotId,
            receivedItems,
            out reason
        ))
        {
            return false;
        }

        return true;
    }

    public static ArchipelagoSettings? CreateEffectiveHostSettingsSnapshot()
    {
        ArchipelagoSettings? source = ArchipelagoClient.Settings;
        if (source == null)
            return null;

        ClientSettings local = ArchipelagoClient.LocalSettings.Value;
        // TODO: is there seriously no automatic setter for this? where snapshot = source and then do slight modifications after
        var snapshot = new ArchipelagoSettings
        {
            PlayerCount = source.PlayerCount,
            PlayerNumber = source.PlayerNumber,
            AscensionLevel = source.AscensionLevel,
            ShouldShuffleAllCards = source.ShouldShuffleAllCards,
            IsSeeded = source.IsSeeded,
            NoCharactersLocked = source.NoCharactersLocked,
            NumCharsGoal = source.NumCharsGoal,
            TotalCharacters = source.TotalCharacters,
            NeowSanity = source.NeowSanity,
            AncientRelicLocation = AncientSettingsUtility.ForNewRun.Location,
            AncientRelicPool = AncientSettingsUtility.ForNewRun.Pool,
            RelicRewardsAvailableAnytime = local.OverrideRelicRewardsAvailableAnytime
                ? local.RelicRewardsAvailableAnytime
                : source.RelicRewardsAvailableAnytime,
            ReleaseOnVictory = source.ReleaseOnVictory,
            CampfireSanity = source.CampfireSanity,
            GoldSanity = source.GoldSanity,
            PotionSanity = source.PotionSanity,
            Floorsanity = source.Floorsanity,
            ProgressiveStarterCard = source.ProgressiveStarterCard,
            ProgressiveStarterRelic = source.ProgressiveStarterRelic,
            ShopSanity = source.ShopSanity,
            ShopCardSlots = source.ShopCardSlots,
            ShopNeutralSlots = source.ShopNeutralSlots,
            ShopRelicSlots = source.ShopRelicSlots,
            ShopPotionSlots = source.ShopPotionSlots,
            ShopRemoveSlots = source.ShopRemoveSlots,
            ShopSanityCosts = source.ShopSanityCosts,
            IsDeathLinkEnabled = local.OverrideDeathLinkOptions
                ? local.EnableDeathLink
                : source.IsDeathLinkEnabled,
            EnableDeathFragments = local.OverrideDeathLinkOptions
                ? local.EnableDeathFragments
                : source.EnableDeathFragments,
            DeathLinkDamagePercent = local.OverrideDeathLinkOptions
                ? local.DeathLinkPercentDamage
                : source.DeathLinkDamagePercent,
            APWorldVersion = source.APWorldVersion,
        };

        foreach ((string key, CharacterConfig config) in source.Characters)
            snapshot.Characters[key] = CloneCharacterConfig(config);
        return snapshot;
    }

    public static void RestoreFrozenHostSettingsForActiveRun()
    {
        if (!IsRealMultiplayerRun
            || RunManager.Instance.NetService.Type != NetGameType.Host
            || RunManager.Instance.DebugOnlyGetState() is not RunState runState
            || !ApRunData.TryGetSharedState(runState, out ApRunSharedState shared)
            || shared.HostSettings == null)
        {
            return;
        }
        if (!ArchipelagoClient.TryUseMultiplayerHostSettings(
                shared.HostSettings,
                out string settingsReason))
        {
            InvalidateRunClaims(settingsReason);
        }
    }

    private static CharacterConfig CloneCharacterConfig(CharacterConfig source) => new()
    {
        Name = source.Name,
        OptionName = source.OptionName,
        CharOffset = source.CharOffset,
        OfficialName = source.OfficialName,
        Seed = source.Seed,
        Locked = source.Locked,
        ModNum = source.ModNum,
        Ascension = new HashSet<string>(source.Ascension, StringComparer.Ordinal),
    };

    public static void EndRun()
    {
        IsRealMultiplayerRun = false;
        _activeParticipation = null;
        ClaimsInvalidated = false;
        _claimInvalidationNoticeShown = false;
        ApRunData.EndRun();
        ManagedActionRequestScheduler.EndRun();
        ApGrantDispatcher.EndRun();
        ApMirroredRewardDispatcher.EndRun();
        RelicReceiptMultiplayer.EndRun();
        ProgressiveStarterMultiplayer.EndRun();
        AscensionMultiplayer.EndRun();
        AncientMultiplayer.EndRun();
        DeathLinkMultiplayer.EndRun();
    }

    public static bool CanClaimGold(out string reason)
    {
        reason = string.Empty;
        if (!IsRealMultiplayerRun)
            return true;

        if (!IsExperimentalMultiplayerRun)
        {
            reason = "Experimental AP multiplayer is not enabled for this run.";
            return false;
        }

        if (!IsFeatureEnabled(MultiplayerFeature.GoldRewards))
        {
            reason = "Gold rewards are not enabled for this multiplayer profile.";
            return false;
        }

        if (ClaimsInvalidated)
        {
            reason = "This AP multiplayer run encountered an unrecoverable binding or grant failure.";
            return false;
        }

        if (!RunManager.Instance.NetService.IsConnected)
        {
            reason = "The local game is disconnected from its multiplayer session.";
            return false;
        }

        if (IsSynchronizedCombatActive)
        {
            reason = "Multiplayer gold can only be claimed outside combat.";
            return false;
        }

        return true;
    }

    /// <summary>Shared safety gate for discrete mirrored reward flows.</summary>
    public static bool CanClaimReceivedReward(ApMirroredRewardKind kind, out string reason)
    {
        reason = string.Empty;
        if (!IsRealMultiplayerRun)
            return true;

        MultiplayerFeature feature = kind switch
        {
            ApMirroredRewardKind.Card => MultiplayerFeature.CardRewards,
            ApMirroredRewardKind.Relic => MultiplayerFeature.RelicRewards,
            ApMirroredRewardKind.Potion => MultiplayerFeature.PotionRewards,
            ApMirroredRewardKind.Ancient => MultiplayerFeature.AncientRewardChoices,
            _ => MultiplayerFeature.UnknownReceivedItems,
        };
        if (!IsExperimentalMultiplayerRun)
        {
            reason = "Experimental AP multiplayer is not enabled for this run.";
            return false;
        }
        if (!IsFeatureEnabled(feature))
        {
            reason = $"{feature} is not enabled for this multiplayer profile.";
            return false;
        }
        if (ClaimsInvalidated)
        {
            reason = "This AP multiplayer run encountered an unrecoverable binding or grant failure.";
            return false;
        }
        if (!RunManager.Instance.NetService.IsConnected)
        {
            reason = "The local game is disconnected from its multiplayer session.";
            return false;
        }
        if (IsSynchronizedCombatActive)
        {
            reason = "Multiplayer AP rewards can only be claimed outside combat.";
            return false;
        }

        return true;
    }

    private static void InvalidateClaims(string reason)
    {
        ClaimsInvalidated = true;
        LogUtility.Error(
            $"Experimental AP multiplayer claims disabled for this run because {reason}. "
                + "A fresh run is required."
        );

        if (_claimInvalidationNoticeShown)
            return;

        _claimInvalidationNoticeShown = true;
        Callable.From(() => NotificationUtility.ShowRawText(
            "AP multiplayer rewards are disabled after an unrecoverable run error. Start a fresh run."
        )).CallDeferred();
    }

    public static void InvalidateRunClaims(string reason) => InvalidateClaims(reason);

}
