using System.Text.Json;
using Archipelago.MultiClient.Net.Enums;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.DomainAdapters;
using STS2RitsuLib;
using STS2RitsuLib.Networking.ManagedActions;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Utils;

/// <summary>
/// Manages one-time combat buffs received from Archipelago. Consumption is recorded against
/// the AP slot so reconnects can skip previously applied receipts; unapplied buffs can carry
/// into later combats and runs.
/// Singleplayer applies buffs at player turn start. In multiplayer, the receiving player
/// requests a native action at DeathLink's safe play-phase boundary, and every peer applies
/// the effect to that player's creature in its own combat state.
/// </summary>
public static class BuffUtility
{
    private sealed record BuffActionMessage(Guid RunId, int ItemIndex, APItem BuffType);

    private static readonly RitsuLibManagedNetActionDescriptor<BuffActionMessage> ActionDescriptor = new(
        ModuleId: ModEntry.ModId,
        ActionKey: "universal_combat_buff",
        Serialize: static message => JsonSerializer.SerializeToUtf8Bytes(message),
        Deserialize: DeserializeMessage,
        Execute: ExecuteAction,
        ActionType: GameActionType.CombatPlayPhaseOnly
    );

    /// <summary>
    /// Pending receipts in ascending AP item index order. LastConsumedIndex is a monotonic
    /// cutoff for replayed receipts, with -1 meaning none consumed. Each entry also tracks
    /// whether its received-item notification has already been shown.
    /// </summary>
    // Keep AP library types out of static generic fields: their eager assembly resolution can
    // run before the game's mod loader has configured the Archipelago assembly context.
    private static BuffReceiptQueue _buffQueue = new();
    private static Task? _storageLoadTask;
    private static bool _storageReady;
    private static bool _consumptionWritePending;
    private static bool _storageWriteFailed;
    private static BuffReceiptQueue? _processingSingleplayer;
    private static bool _initialized;
    private static BuffActionMessage? _pendingAction;
    private static string? _lastRequestError;
    // Consumption belongs to the AP slot, including its numbered co-op identity, across runs.
    private static string StorageKey => CoopSlot.StorageKey("StS2AP_LastConsumedBuffIdx");

    /// <summary>
    /// Registers the multiplayer action and singleplayer player-turn hook once at mod startup.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;
        RitsuLibManagedNetActions.Register(ActionDescriptor);
        RitsuLibFramework.SubscribeLifecycle<SideTurnStartingEvent>(evt =>
        {
            if (!MultiplayerSupport.IsMultiplayerScope
                && evt.Side == CombatSide.Player
                && GameUtility.CurrentPlayer is Player player)
            {
                _ = ProcessQueuedBuffsAsync(player);
            }
        });
        _initialized = true;
    }

    /// <summary>
    /// Loads the slot's consumed index before either mode can apply queued buffs.
    /// Item history can arrive before this read finishes, so processing waits for storage
    /// readiness. A failed read leaves receipts pending instead of treating history as empty.
    /// </summary>
    public static async Task LoadFromStorageAsync()
    {
        if (!ArchipelagoClient.IsConnected || ArchipelagoClient.Session is not { } session)
            return;
        _storageReady = false;
        _storageLoadTask = LoadFromStorageInternalAsync(session, _buffQueue, StorageKey);
        await _storageLoadTask;
    }

    private static async Task LoadFromStorageInternalAsync(
        Archipelago.MultiClient.Net.ArchipelagoSession session,
        BuffReceiptQueue queue,
        string storageKey)
    {
        try
        {
            session.DataStorage[Scope.Slot, storageKey].Initialize(-1);
            int stored = await session.DataStorage[Scope.Slot, storageKey].GetAsync<int>();
            if (stored < -1)
                throw new InvalidDataException($"Invalid consumed buff index {stored}.");

            // Publish on the game thread before the load task completes. A stale read must
            // neither unlock a replacement queue nor rewind already completed actions.
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Callable.From(() =>
            {
                if (ReferenceEquals(ArchipelagoClient.Session, session)
                    && ReferenceEquals(_buffQueue, queue))
                {
                    queue.RestoreConsumedIndex(stored);
                    _storageReady = true;
                    if (queue.LastConsumedIndex > stored)
                        ConsumeLocal(queue.LastConsumedIndex);
                }
                completion.SetResult();
            }).CallDeferred();
            await completion.Task;
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Buff consumption history could not be loaded; buffs remain pending: {ex.Message}");
        }
    }

    /// <summary>
    /// Queues an incoming buff unless its receipt is already consumed or queued.
    /// If storage is not ready, the received-item notification is deferred until the receipt
    /// passes the consumed check, avoiding notifications for old buffs replayed on reconnect.
    /// </summary>
    /// <param name="buffType">The AP combat buff to apply.</param>
    /// <param name="itemIndex">One-based position in AP received-item history, stable across replays.</param>
    public static void EnqueueBuff(APItem buffType, int itemIndex)
    {
        if (_buffQueue.Enqueue(buffType, itemIndex, notificationShown: _storageReady)
            && _storageReady)
        {
            ShowReceived(itemIndex);
        }
    }

    /// <summary>
    /// Processes singleplayer buffs at player turn start, after consumption history is loaded.
    /// Each receipt is consumed only after its power command completes. An unavailable target
    /// or failed command leaves the oldest receipt pending for a later turn.
    /// </summary>
    public static async Task ProcessQueuedBuffsAsync(Player player)
    {
        if (MultiplayerSupport.IsMultiplayerScope
            || ReferenceEquals(_processingSingleplayer, _buffQueue))
            return;
        // Reconnect replaces the queue; this task must never consume its replacement's receipts.
        var queue = _buffQueue;
        _processingSingleplayer = queue;
        try
        {
            if (_storageLoadTask != null)
                await _storageLoadTask;
            if (!_storageReady || !ReferenceEquals(queue, _buffQueue))
                return;

            while (queue.TryPeek(out var entry))
            {
                if (MultiplayerSupport.IsMultiplayerScope
                    || !ReferenceEquals(player, GameUtility.CurrentPlayer)
                    || !await ApplyBuff(entry.BuffType, player, new BlockingPlayerChoiceContext()))
                {
                    return;
                }
                if (!ReferenceEquals(queue, _buffQueue))
                    return;
                if (!entry.NotificationShown)
                    ShowReceived(entry.ItemIndex);
                ConsumeLocal(entry.ItemIndex);
            }
        }
        catch (Exception ex)
        {
            // Keep the failed receipt at the head; advancing past it would lose it permanently.
            LogUtility.Error($"Buff application failed; the receipt remains pending: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_processingSingleplayer, queue))
                _processingSingleplayer = null;
        }
    }

    /// <summary>
    /// Called by the existing NRun item-processing tick to submit the receiving player's next
    /// buff through the native action queue. Checking each tick allows admission later in the
    /// current play phase. Receipts remain pending in the lobby, between combats, while dead,
    /// and across runs until a native action applies them.
    /// </summary>
    internal static void ProcessMultiplayerBuffs()
    {
        PersistConsumedIndex();
        if (!MultiplayerSupport.IsRealMultiplayerRun
            || !MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.CombatEffects)
            || MultiplayerSupport.ClaimsInvalidated
            || !_storageReady
            || GameUtility.CurrentPlayer is not Player player
            || !MultiplayerLocationChecks.IsLocalProgressOwner(player)
            || player.RunState is not RunState run
            || !ApRunData.TryGetSharedState(run, out ApRunSharedState shared)
            || shared.RunId == Guid.Empty
            || !ApRunData.TryGetPlayerState(run, player.NetId, out ApPlayerRunState owner)
            || owner.Participation != ApParticipationKind.OwnApSlot
            || !IsCurrentOwner(owner))
        {
            return;
        }

        if (_pendingAction?.RunId != shared.RunId)
            _pendingAction = null;
        // Saved run data can be ahead of AP storage after a rejoin. Reconcile before replaying.
        if (owner.LastConsumedBuffIndex > _buffQueue.LastConsumedIndex)
            ConsumeLocal(owner.LastConsumedBuffIndex);

        while (_buffQueue.TryPeek(out var entry))
        {
            // A pending singleplayer buff can survive a switch into multiplayer. Its gold
            // has already been included by the multiplayer history rebuild.
            if (IsMultiplayerBuffGoldFallback((long)entry.BuffType))
            {
                _buffQueue.Discard(entry.ItemIndex);
                continue;
            }
            if (_pendingAction != null || player.Creature.IsDead
                || !player.Creature.CanReceivePowers
                || !DeathLinkMultiplayer.CanAdmitCombatAction(out _))
            {
                return;
            }

            // Request acceptance only means submission. Keep the receipt until execution
            // completes, with one request in flight to preserve receipt order.
            var message = new BuffActionMessage(shared.RunId, entry.ItemIndex, entry.BuffType);
            _pendingAction = message;
            try
            {
                if (RitsuLibManagedNetActions.Request(
                        RunManager.Instance, ActionDescriptor, message, player.NetId))
                {
                    _lastRequestError = null;
                    return;
                }
                LogRequestError("Buff action transport is not ready; the receipt remains pending.");
            }
            catch (Exception ex)
            {
                LogRequestError($"Buff action request failed; the receipt remains pending: {ex.Message}");
            }
            if (_pendingAction == message)
                _pendingAction = null;
            return;
        }
    }

    private static bool IsCurrentOwner(ApPlayerRunState owner) =>
        MultiplayerSupport.IsLocalOwnApSlot
        && ParticipantAdapter.MatchReturning(
            owner, ApParticipationKind.OwnApSlot, MultiplayerSupport.PreparedSlotIdentity).IsOk;

    private static BuffActionMessage DeserializeMessage(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<BuffActionMessage>(bytes)
                ?? new BuffActionMessage(Guid.Empty, 0, default);
        }
        catch (JsonException)
        {
            return new BuffActionMessage(Guid.Empty, 0, default);
        }
    }

    /// <summary>
    /// Runs on every peer against the native action's receiving player. Every replica records
    /// consumption in run data; only that player's client updates its AP storage and notification.
    /// </summary>
    private static async Task ExecuteAction(RitsuLibManagedNetActionContext<BuffActionMessage> context)
    {
        var message = context.Message;
        Player player = context.Player; // The native action owner is the only buff target.
        try
        {
            // Revalidate against synchronized run data at execution time. A peer's local AP
            // connection must not decide whether it executes another player's buff.
            if (message.RunId == Guid.Empty || message.ItemIndex <= 0
                || !IsUniversalCombatBuff((long)message.BuffType)
                || IsMultiplayerBuffGoldFallback((long)message.BuffType)
                || player.RunState is not RunState run
                || !ReferenceEquals(run, RunManager.Instance.DebugOnlyGetState())
                || !ReferenceEquals(player, run.GetPlayer(player.NetId))
                || !ApRunData.TryGetSharedState(run, out ApRunSharedState shared)
                || shared.RunId != message.RunId
                || !ApRunData.TryGetPlayerState(run, player.NetId, out ApPlayerRunState owner)
                || owner.Participation != ApParticipationKind.OwnApSlot
                || owner.SlotSettings == null
                || message.ItemIndex <= owner.LastConsumedBuffIndex)
            {
                return;
            }

            if (!await ApplyBuff(message.BuffType, player, context.PlayerChoiceContext))
                return;
            if (!ApRunData.RecordConsumedBuff(run, player.NetId, message.ItemIndex))
                throw new InvalidOperationException("Applied buff could not be recorded in run data.");

            if (LocalContext.IsMe(player) && IsCurrentOwner(owner))
            {
                if (_buffQueue.TryPeek(out var entry)
                    && entry.ItemIndex == message.ItemIndex && !entry.NotificationShown)
                {
                    ShowReceived(message.ItemIndex);
                }
                ConsumeLocal(message.ItemIndex);
            }
            LogUtility.Info($"Applied buff {message.BuffType} receipt {message.ItemIndex} to player {player.NetId}.");
        }
        catch (Exception ex)
        {
            // A command may have partially changed combat; blindly retrying could repeat effects.
            MultiplayerSupport.InvalidateRunClaims($"a synchronized buff action failed: {ex.Message}");
            throw;
        }
        finally
        {
            if (LocalContext.IsMe(player) && _pendingAction == message)
                _pendingAction = null;
        }
    }

    /// <summary>
    /// Applies the buff's combat effect. Multiplayer supplies the managed action's choice
    /// context so power hooks and any choices stay within the synchronized action.
    /// </summary>
    /// <returns>False when the target cannot receive the buff, leaving its receipt pending.</returns>
    private static async Task<bool> ApplyBuff(APItem buffType, Player player, PlayerChoiceContext context)
    {
        if (player.Creature.IsDead || !player.Creature.CanReceivePowers
            || player.Creature.CombatState == null || CombatManager.Instance.IsEnding)
        {
            return false;
        }

        if (buffType == APItem.AdditionalCardReward)
        {
            if (player.RunState.CurrentRoom is not CombatRoom room)
                return false;
            // The Hunt power is only an indicator; its card stores the reward on the room.
            await PowerCmd.Apply<TheHuntPower>(context, player.Creature, 1, player.Creature, null);
            room.AddExtraReward(player,
                new CardReward(CardCreationOptions.ForRoom(player, room.RoomType), 3, player));
            return true;
        }

        Task effect = buffType switch
        {
            APItem.FreeAttack => PowerCmd.Apply<FreeAttackPower>(context, player.Creature, 1, player.Creature, null),
            APItem.FreePower => PowerCmd.Apply<FreePowerPower>(context, player.Creature, 1, player.Creature, null),
            APItem.FreeSkill => PowerCmd.Apply<FreeSkillPower>(context, player.Creature, 1, player.Creature, null),
            APItem.Artifact => PowerCmd.Apply<ArtifactPower>(context, player.Creature, 2, player.Creature, null),
            APItem.Dexterity => PowerCmd.Apply<DexterityPower>(context, player.Creature, 2, player.Creature, null),
            APItem.Strength => PowerCmd.Apply<StrengthPower>(context, player.Creature, 2, player.Creature, null),
            APItem.Plating => PowerCmd.Apply<PlatingPower>(context, player.Creature, 5, player.Creature, null),
            APItem.Thorns => PowerCmd.Apply<ThornsPower>(context, player.Creature, 3, player.Creature, null),
            APItem.Vigor => PowerCmd.Apply<VigorPower>(context, player.Creature, 8, player.Creature, null),
            APItem.Buffer => PowerCmd.Apply<BufferPower>(context, player.Creature, 1, player.Creature, null),
            APItem.Friendship => PowerCmd.Apply<FriendshipPower>(context, player.Creature, 1, player.Creature, null),
            // These effects remain available in singleplayer. Multiplayer exclusions live in
            // ItemTable's gold fallback list; removing an entry enables its effect for trials.
            APItem.PostCombatCardUpgrade => PowerCmd.Apply<ImprovementPower>(context, player.Creature, 1, player.Creature, null),
            APItem.PostCombatCardRemoval => PowerCmd.Apply<ForbiddenGrimoirePower>(context, player.Creature, 1, player.Creature, null),
            _ => throw new ArgumentOutOfRangeException(nameof(buffType)),
        };
        await effect;
        return true;
    }

    /// <summary>
    /// Advances local consumption and schedules persistence after a completed power command
    /// or reconciliation with the receiving player's saved run history.
    /// </summary>
    private static void ConsumeLocal(int itemIndex)
    {
        _buffQueue.RestoreConsumedIndex(itemIndex);
        _consumptionWritePending = true;
        PersistConsumedIndex();
    }

    private static void PersistConsumedIndex()
    {
        if (!_consumptionWritePending || !ArchipelagoClient.IsConnected
            || !_storageReady || ArchipelagoClient.Session is not { } session)
            return;
        try
        {
            session.DataStorage[Scope.Slot, StorageKey] = _buffQueue.LastConsumedIndex;
            _consumptionWritePending = false;
            _storageWriteFailed = false;
        }
        catch (Exception ex)
        {
            // Keep the pending write so the item-processing tick can retry without reapplying.
            if (!_storageWriteFailed)
                LogUtility.Warn($"Consumed buff index could not be persisted to AP; retrying: {ex.Message}");
            _storageWriteFailed = true;
        }
    }

    private static void ShowReceived(int itemIndex)
    {
        try
        {
            var item = ArchipelagoClient.Session?.Items.AllItemsReceived.ElementAtOrDefault(itemIndex - 1);
            NotificationUtility.ShowBuffReceived(item);
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Buff notification could not be displayed: {ex.Message}");
        }
    }

    private static void LogRequestError(string reason)
    {
        if (_lastRequestError != reason)
            LogUtility.Warn(reason);
        _lastRequestError = reason;
    }

    /// <summary>
    /// Clears transient state on disconnect while preserving consumed history. AP item replay
    /// restores outstanding receipts on reconnect. Replacing the queue also invalidates older
    /// asynchronous loads and processing tasks.
    /// </summary>
    public static void ClearQueue()
    {
        _buffQueue = new BuffReceiptQueue(_buffQueue.LastConsumedIndex);
        _storageLoadTask = null;
        _storageReady = false;
        _storageWriteFailed = false;
        _pendingAction = null;
        _lastRequestError = null;
    }

    /// <summary>
    /// Starts fresh consumption history when changing AP slots, whose receipt indices are independent.
    /// </summary>
    internal static void ResetSlotState()
    {
        ClearQueue();
        _buffQueue = new BuffReceiptQueue();
        _consumptionWritePending = false;
    }
}
