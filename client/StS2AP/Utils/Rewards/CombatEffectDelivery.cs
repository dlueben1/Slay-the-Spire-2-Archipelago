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
using STS2RitsuLib.Networking.ManagedActions;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Utils;

/// <summary>
/// Manages one-time combat effects received from Archipelago. Consumption is recorded against
/// the AP slot so reconnects can skip previously applied receipts; unapplied combat effects can carry
/// into later combats and runs.
/// Singleplayer applies combat effects at player turn start. In multiplayer, the receiving player
/// requests a native action at DeathLink's safe play-phase boundary, and every peer applies
/// the effect to that player's creature in its own combat state. Each player consumes at
/// most one buff per combat and one trap per turn; the rest remain queued in receipt order.
/// </summary>
internal sealed class CombatEffectDelivery
{
    private readonly bool _trap;
    private readonly RitsuLibManagedNetActionDescriptor<CombatEffectActionMessage> ActionDescriptor;

    public CombatEffectDelivery(bool trap)
    {
        _trap = trap;
        _queue = new CombatEffectReceiptQueue(traps: trap);
        ActionDescriptor = new(
            ModuleId: ModEntry.ModId,
            ActionKey: trap ? "universal_combat_trap" : "universal_combat_buff",
            Serialize: static message => JsonSerializer.SerializeToUtf8Bytes(message),
            Deserialize: DeserializeMessage,
            Execute: ExecuteAction,
            ActionType: GameActionType.CombatPlayPhaseOnly);
    }

    private int ConsumedIndex(ApPlayerRunState owner) => _trap
        ? owner.LastConsumedTrapIndex : owner.LastConsumedBuffIndex;
    private CombatEffectLimit Limit(ApPlayerRunState owner) => _trap
        ? owner.CombatTrapLimit : owner.CombatBuffLimit;

    /// <summary>
    /// Pending receipts in ascending AP item index order. LastConsumedIndex is a monotonic
    /// cutoff for replayed receipts, with -1 meaning none consumed. Each entry also tracks
    /// whether its received-item notification has already been shown.
    /// </summary>
    // Keep AP library types out of static generic fields: their eager assembly resolution can
    // run before the game's mod loader has configured the Archipelago assembly context.
    private CombatEffectReceiptQueue _queue;
    private bool _storageReady;
    private bool _consumptionWritePending;
    private bool _storageWriteFailed;
    private CombatEffectReceiptQueue? _processingSingleplayer;
    private bool _initialized;
    private CombatEffectActionMessage? _pendingAction;
    private string? _lastRequestError;
    // Consumption belongs to the AP slot, including its numbered co-op identity, across runs.
    private string StorageKey => CoopSlot.StorageKey(_trap ? "StS2AP_LastConsumedTrapIdx" : "StS2AP_LastConsumedBuffIdx");

    /// <summary>
    /// Registers this category's multiplayer action once at mod startup.
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
            return;
        RitsuLibManagedNetActions.Register(ActionDescriptor);
        _initialized = true;
    }

    /// <summary>
    /// Loads the slot's consumed index before either mode can apply queued combat effects.
    /// Item history can arrive before this read finishes, so processing waits for storage
    /// readiness. A failed read leaves receipts pending instead of treating history as empty.
    /// </summary>
    public async Task LoadFromStorageAsync()
    {
        if (!ArchipelagoClient.IsConnected || ArchipelagoClient.Session is not { } session)
            return;
        _storageReady = false;
        await LoadFromStorageInternalAsync(session, _queue, StorageKey);
    }

    private async Task LoadFromStorageInternalAsync(
        Archipelago.MultiClient.Net.ArchipelagoSession session,
        CombatEffectReceiptQueue queue,
        string storageKey)
    {
        try
        {
            session.DataStorage[Scope.Slot, storageKey].Initialize(-1);
            int stored = await session.DataStorage[Scope.Slot, storageKey].GetAsync<int>();
            if (stored < -1)
                throw new InvalidDataException($"Invalid consumed combat effect index {stored}.");

            // Publish on the game thread before the load task completes. A stale read must
            // neither unlock a replacement queue nor rewind already completed actions.
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Callable.From(() =>
            {
                if (ReferenceEquals(ArchipelagoClient.Session, session)
                    && ReferenceEquals(_queue, queue))
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
            LogUtility.Warn($"Combat effect consumption history could not be loaded; combat effects remain pending: {ex.Message}");
        }
    }

    /// <summary>
    /// Queues an incoming combat effect unless its receipt is already consumed or queued.
    /// If storage is not ready, the received-item notification is deferred until the receipt
    /// passes the consumed check, avoiding notifications for old combat effects replayed on reconnect.
    /// </summary>
    /// <param name="effectType">The AP combat effect to apply.</param>
    /// <param name="itemIndex">One-based position in AP received-item history, stable across replays.</param>
    public void Enqueue(APItem effectType, int itemIndex)
    {
        if (_queue.Enqueue(effectType, itemIndex, notificationShown: _storageReady)
            && _storageReady)
        {
            ShowReceived(itemIndex);
        }
    }

    /// <summary>
    /// Processes singleplayer combat effects at player turn start, after consumption history is loaded.
    /// Each receipt is consumed only after its power command completes. An unavailable target
    /// or failed command leaves the oldest receipt pending for a later turn.
    /// </summary>
    public async Task ProcessQueuedAsync(Player player)
    {
        if (MultiplayerSupport.IsMultiplayerScope
            || ReferenceEquals(_processingSingleplayer, _queue))
            return;
        // Reconnect replaces the queue; this task must never consume its replacement's receipts.
        var queue = _queue;
        _processingSingleplayer = queue;
        try
        {
            // Never hold up the native turn while waiting for an AP network read.
            if (!_storageReady || !ReferenceEquals(queue, _queue))
                return;

            if (player.RunState is not RunState run || GetEffectKey(player) is not { } combat)
                return;
            var owner = ApRunData.GetSingleplayerBuffState(run, player.NetId);
            if (ConsumedIndex(owner) > queue.LastConsumedIndex)
                ConsumeLocal(ConsumedIndex(owner));
            if (!queue.TryPeek(out var entry)
                || MultiplayerSupport.IsMultiplayerScope
                || !ReferenceEquals(player, GameUtility.CurrentPlayer)
                || !await ApplyQueuedEffect(entry.EffectType, entry.ItemIndex, combat, player, run,
                    owner, new BlockingPlayerChoiceContext()))
            {
                return;
            }
            if (!ReferenceEquals(queue, _queue))
                return;
            if (!entry.NotificationShown)
                ShowReceived(entry.ItemIndex);
            ConsumeLocal(entry.ItemIndex);
        }
        catch (Exception ex)
        {
            // Keep the failed receipt at the head; advancing past it would lose it permanently.
            LogUtility.Error($"Combat effect application failed; the receipt remains pending: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_processingSingleplayer, queue))
                _processingSingleplayer = null;
        }
    }

    /// <summary>
    /// Called by the existing NRun item-processing tick to submit the receiving player's next
    /// combat effect through the native action queue. Checking each tick allows admission later in the
    /// current play phase. Receipts remain pending in the lobby, between combats, while dead,
    /// and across runs until a native action applies them.
    /// </summary>
    internal void ProcessMultiplayer()
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

        CombatEffectKey? combat = GetEffectKey(player);
        // Dropped requests must not block later combats/turns; the key also rejects late execution.
        if (_pendingAction?.RunId != shared.RunId || _pendingAction?.Combat != combat)
            _pendingAction = null;
        // Saved run data can be ahead of AP storage after a rejoin. Reconcile before replaying.
        if (ConsumedIndex(owner) > _queue.LastConsumedIndex)
            ConsumeLocal(ConsumedIndex(owner));

        while (_queue.TryPeek(out var entry))
        {
            // A pending singleplayer combat effect can survive a switch into multiplayer. Its gold
            // has already been included by the multiplayer history rebuild.
            if (IsMultiplayerBuffGoldFallback((long)entry.EffectType))
            {
                _queue.Discard(entry.ItemIndex);
                continue;
            }
            if (combat == null
                || !Limit(owner).CanConsume(combat)
                || _pendingAction != null || player.Creature.IsDead
                || (entry.EffectType != APItem.DazedTrap && !player.Creature.CanReceivePowers)
                || !DeathLinkMultiplayer.CanAdmitCombatAction(out _))
            {
                return;
            }

            // Request acceptance only means submission. Keep the receipt until execution
            // completes, with one request in flight to preserve receipt order.
            var message = new CombatEffectActionMessage(shared.RunId, entry.ItemIndex, entry.EffectType, combat);
            _pendingAction = message;
            try
            {
                if (RitsuLibManagedNetActions.Request(
                        RunManager.Instance, ActionDescriptor, message, player.NetId))
                {
                    _lastRequestError = null;
                    return;
                }
                LogRequestError("Combat effect action transport is not ready; the receipt remains pending.");
            }
            catch (Exception ex)
            {
                LogRequestError($"Combat effect action request failed; the receipt remains pending: {ex.Message}");
            }
            if (_pendingAction == message)
                _pendingAction = null;
            return;
        }
    }

    private bool IsCurrentOwner(ApPlayerRunState owner) =>
        MultiplayerSupport.IsLocalOwnApSlot
        && ParticipantAdapter.MatchReturning(
            owner, ApParticipationKind.OwnApSlot, MultiplayerSupport.PreparedSlotIdentity).IsOk;

    private CombatEffectActionMessage DeserializeMessage(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<CombatEffectActionMessage>(bytes)
                ?? new CombatEffectActionMessage(Guid.Empty, 0, default, null);
        }
        catch (JsonException)
        {
            return new CombatEffectActionMessage(Guid.Empty, 0, default, null);
        }
    }

    /// <summary>
    /// Runs on every peer against the native action's receiving player. Every replica records
    /// consumption in run data; only that player's client updates its AP storage and notification.
    /// </summary>
    private async Task ExecuteAction(RitsuLibManagedNetActionContext<CombatEffectActionMessage> context)
    {
        var message = context.Message;
        Player player = context.Player; // The native action owner is the only combat effect target.
        try
        {
            // Revalidate against synchronized run data at execution time. A peer's local AP
            // connection must not decide whether it executes another player's effect.
            if (player.RunState is not RunState run
                || !ReferenceEquals(run, RunManager.Instance.DebugOnlyGetState())
                || !ReferenceEquals(player, run.GetPlayer(player.NetId))
                || !ApRunData.TryGetSharedState(run, out ApRunSharedState shared)
                || !ApRunData.TryGetPlayerState(run, player.NetId, out ApPlayerRunState owner)
                || owner.Participation != ApParticipationKind.OwnApSlot
                || owner.SlotSettings == null
                || !message.Matches(shared.RunId, GetEffectKey(player), ConsumedIndex(owner), _trap))
            {
                return;
            }

            if (!await ApplyQueuedEffect(message.EffectType, message.ItemIndex, message.Combat!,
                    player, run, owner, context.PlayerChoiceContext))
                return;

            if (LocalContext.IsMe(player) && IsCurrentOwner(owner))
            {
                if (_queue.TryPeek(out var entry)
                    && entry.ItemIndex == message.ItemIndex && !entry.NotificationShown)
                {
                    ShowReceived(message.ItemIndex);
                }
                ConsumeLocal(message.ItemIndex);
            }
            LogUtility.Info($"Applied combat effect {message.EffectType} receipt {message.ItemIndex} to player {player.NetId}.");
        }
        catch (Exception ex)
        {
            // A command may have partially changed combat; blindly retrying could repeat effects.
            MultiplayerSupport.InvalidateRunClaims($"a synchronized combat effect action failed: {ex.Message}");
            throw;
        }
        finally
        {
            if (LocalContext.IsMe(player) && _pendingAction == message)
                _pendingAction = null;
        }
    }

    private CombatEffectKey? GetEffectKey(Player player)
    {
        if (player.RunState is not RunState run
            || run.CurrentRoom is not CombatRoom { Id: int roomId } room
            || !ReferenceEquals(player.Creature.CombatState, room.CombatState))
        {
            return null;
        }
        int turn = _trap ? player.PlayerCombatState?.TurnNumber ?? 0 : 0;
        if (_trap && turn < 1)
            return null;
        return new CombatEffectKey(run.CurrentActIndex, run.TotalFloor, roomId, turn);
    }

    private async Task<bool> ApplyQueuedEffect(APItem effectType, int itemIndex,
        CombatEffectKey combat, Player player, RunState run, ApPlayerRunState owner,
        PlayerChoiceContext context)
    {
        // Check at execution as well as admission: duplicate/delayed network requests and
        // reconnects must not spend a second receipt on the same player's combat/turn.
        if (!Limit(owner).TryBegin(combat))
            return false;
        try
        {
            if (!await ApplyEffect(effectType, player, context))
                return false;
            if (!ApRunData.RecordConsumedCombatEffect(run, player.NetId, itemIndex, combat, _trap))
                throw new InvalidOperationException("Applied combat effect could not be recorded in run data.");
            return true;
        }
        finally
        {
            Limit(owner).Cancel();
        }
    }

    /// <summary>
    /// Applies the queued combat effect. Multiplayer supplies the managed action's choice
    /// context so power hooks and any choices stay within the synchronized action.
    /// </summary>
    /// <returns>False when the target cannot receive the effect, leaving its receipt pending.</returns>
    private async Task<bool> ApplyEffect(APItem effectType, Player player, PlayerChoiceContext context)
    {
        if (player.Creature.IsDead
            || (effectType != APItem.DazedTrap && !player.Creature.CanReceivePowers)
            || player.Creature.CombatState == null || CombatManager.Instance.IsEnding)
        {
            return false;
        }

        if (_trap)
            return await TrapEffects.Apply(effectType, player, context);

        if (effectType == APItem.AdditionalCardReward)
        {
            if (player.RunState.CurrentRoom is not CombatRoom room)
                return false;
            // The Hunt power is only an indicator; its card stores the reward on the room.
            await PowerCmd.Apply<TheHuntPower>(context, player.Creature, 1, player.Creature, null);
            room.AddExtraReward(player,
                new CardReward(CardCreationOptions.ForRoom(player, room.RoomType), 3, player));
            return true;
        }

        Task effect = effectType switch
        {
            APItem.FreeAttack => PowerCmd.Apply<FreeAttackPower>(context, player.Creature, 1, player.Creature, null),
            APItem.FreePower => PowerCmd.Apply<FreePowerPower>(context, player.Creature, 1, player.Creature, null),
            APItem.FreeSkill => PowerCmd.Apply<FreeSkillPower>(context, player.Creature, 1, player.Creature, null),
            APItem.Artifact => PowerCmd.Apply<ArtifactPower>(context, player.Creature, 1, player.Creature, null),
            APItem.Dexterity => PowerCmd.Apply<DexterityPower>(context, player.Creature, 1, player.Creature, null),
            APItem.Strength => PowerCmd.Apply<StrengthPower>(context, player.Creature, 1, player.Creature, null),
            APItem.Plating => PowerCmd.Apply<PlatingPower>(context, player.Creature, 4, player.Creature, null),
            APItem.Thorns => PowerCmd.Apply<ThornsPower>(context, player.Creature, 3, player.Creature, null),
            APItem.Vigor => PowerCmd.Apply<VigorPower>(context, player.Creature, 8, player.Creature, null),
            APItem.Buffer => PowerCmd.Apply<BufferPower>(context, player.Creature, 1, player.Creature, null),
            APItem.Friendship => PowerCmd.Apply<FriendshipPower>(context, player.Creature, 1, player.Creature, null),
            // These effects remain available in singleplayer. Multiplayer exclusions live in
            // ItemTable's gold fallback list; removing an entry enables its effect for trials.
            APItem.PostCombatCardUpgrade => PowerCmd.Apply<ImprovementPower>(context, player.Creature, 1, player.Creature, null),
            APItem.PostCombatCardRemoval => PowerCmd.Apply<ForbiddenGrimoirePower>(context, player.Creature, 1, player.Creature, null),
            _ => throw new ArgumentOutOfRangeException(nameof(effectType)),
        };
        await effect;
        return true;
    }

    /// <summary>
    /// Advances local consumption and schedules persistence after a completed power command
    /// or reconciliation with the receiving player's saved run history.
    /// </summary>
    private void ConsumeLocal(int itemIndex)
    {
        _queue.RestoreConsumedIndex(itemIndex);
        _consumptionWritePending = true;
        PersistConsumedIndex();
    }

    private void PersistConsumedIndex()
    {
        if (!_consumptionWritePending || !ArchipelagoClient.IsConnected
            || !_storageReady || ArchipelagoClient.Session is not { } session)
            return;
        try
        {
            session.DataStorage[Scope.Slot, StorageKey] = _queue.LastConsumedIndex;
            _consumptionWritePending = false;
            _storageWriteFailed = false;
        }
        catch (Exception ex)
        {
            // Keep the pending write so the item-processing tick can retry without reapplying.
            if (!_storageWriteFailed)
                LogUtility.Warn($"Consumed combat effect index could not be persisted to AP; retrying: {ex.Message}");
            _storageWriteFailed = true;
        }
    }

    private void ShowReceived(int itemIndex)
    {
        try
        {
            var item = ArchipelagoClient.Session?.Items.AllItemsReceived.ElementAtOrDefault(itemIndex - 1);
            if (_trap)
                NotificationUtility.ShowTrapReceived(item);
            else
                NotificationUtility.ShowBuffReceived(item);
        }
        catch (Exception ex)
        {
            LogUtility.Warn($"Combat effect notification could not be displayed: {ex.Message}");
        }
    }

    private void LogRequestError(string reason)
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
    public void ClearQueue()
    {
        _queue = new CombatEffectReceiptQueue(_queue.LastConsumedIndex, traps: _trap);
        _storageReady = false;
        _storageWriteFailed = false;
        _pendingAction = null;
        _lastRequestError = null;
    }

    /// <summary>
    /// Starts fresh consumption history when changing AP slots, whose receipt indices are independent.
    /// </summary>
    internal void ResetSlotState()
    {
        ClearQueue();
        _queue = new CombatEffectReceiptQueue(traps: _trap);
        _consumptionWritePending = false;
    }
}
