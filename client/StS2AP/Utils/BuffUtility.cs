using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.Enums;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib;
using static StS2AP.Data.ItemTable;

namespace StS2AP.Utils
{
    /// <summary>
    /// Manages ephemeral one-time-use buff items received from the Archipelago Multiworld.
    ///
    /// Buff items are a "third category" of AP item alongside run-replenishing rewards
    /// (cards, gold, relics) and permanent unlocks (characters, ascension). Unlike those
    /// categories, each buff is applied exactly once — at the start of the player's next
    /// combat turn — and is never applied again, even across reconnects, game restarts, or
    /// different clients.
    ///
    /// ─── Lifecycle ──────────────────────────────────────────────────────────────────
    ///  1. On connect:   <see cref="LoadFromStorageAsync"/> reads the index of the last
    ///                   consumed buff from the server's DataStorage and populates
    ///                   <see cref="_lastConsumedBuffIndex"/>. This must be called early
    ///                   so that reconnect replays skip already-applied buffs.
    ///                   <see cref="ProcessQueuedBuffsAsync"/> awaits the storage load
    ///                   task as an additional safety net for race conditions.
    ///
    ///  2. On item recv: <see cref="EnqueueBuff"/> is called from
    ///                   <see cref="ArchipelagoClient.ProcessItem"/> when a buff item
    ///                   arrives. The buff is added to <see cref="_buffQueue"/> unless
    ///                   we already know it was consumed (fast path). If storage hasn't
    ///                   loaded yet, it is enqueued anyway and re-checked at apply time.
    ///
    ///  3. On turn start: <see cref="ProcessQueuedBuffsAsync"/> is triggered by a
    ///                    <see cref="SideTurnStartingEvent"/> subscription (set up in
    ///                    <see cref="Initialize"/>). It drains the queue, applies each
    ///                    buff via <see cref="ApplyBuff"/>, then writes the consumed
    ///                    index to DataStorage.
    ///
    ///  4. On disconnect: <see cref="ClearQueue"/> empties <see cref="_buffQueue"/> so
    ///                    stale entries don't carry over. The consumed-index set is
    ///                    preserved because it represents permanent history.
    /// </summary>
    public static class BuffUtility
    {
        #region State

        /// <summary>
        /// Queue of buff items that have been received from AP but not yet applied in combat.
        /// Items stay here until the player's next combat turn starts.
        /// </summary>
        private static readonly Queue<(APItem BuffType, int ItemIndex)> _buffQueue = new();

        /// <summary>
        /// The Archipelago item index of the most recently applied (consumed) buff.
        /// Any buff item with a global item index less than or equal to this value is
        /// considered already consumed and will be skipped.
        ///
        /// <para>
        /// This single integer is sufficient to deduplicate ALL previously consumed buffs
        /// because the Archipelago protocol guarantees items are always replayed in the
        /// same sequential order on every reconnect. Since buffs are always enqueued and
        /// applied in ascending index order (items are received in order, and the queue
        /// is FIFO), <see cref="_lastConsumedBuffIndex"/> increases monotonically. Once
        /// buff N is consumed, every buff with a lower index than N has already been
        /// consumed too, so a single "high-water mark" is all that is needed.
        /// </para>
        ///
        /// -1 means no buffs have been consumed yet in this slot - easier than a nullable int.
        /// </summary>
        private static int _lastConsumedBuffIndex = -1;

        /// <summary>
        /// Tracks the async task that loads <see cref="_lastConsumedBuffIndex"/> from DataStorage.
        /// <see cref="ProcessQueuedBuffsAsync"/> awaits this before applying any buffs, handling
        /// the race condition where buff items arrive before the DataStorage read finishes.
        /// Reset to null by <see cref="ClearQueue"/> so each reconnect starts a fresh load.
        /// </summary>
        private static Task? _storageLoadTask;

        /// <summary>
        /// DataStorage key used to persist the last consumed buff item index on the AP server.
        /// Stores a single <c>int</c> (the high-water mark). Scoped to the player's slot
        /// so it is shared across clients and sessions but not across multiworld slots.
        /// </summary>
        private const string StorageKey = "StS2AP_LastConsumedBuffIdx";

        #endregion

        #region Initialization

        /// <summary>
        /// Registers the <see cref="SideTurnStartingEvent"/> lifecycle subscription so that
        /// <see cref="ProcessQueuedBuffsAsync"/> is called automatically at the start of
        /// every player combat turn.
        ///
        /// Call this once from <see cref="ModEntry.Initialize"/> at mod startup.
        /// </summary>
        public static void Initialize()
        {
            /// Subscribe to RitsuLib's SideTurnStartingEvent, which fires at the beginning of
            /// every combat side's turn (player and enemies). We filter to the player's side only.
            RitsuLibFramework.SubscribeLifecycle<SideTurnStartingEvent>(evt =>
            {
                // Only process queued buffs when we're in a run, and when it is the player starting their turn
                if (GameUtility.CurrentPlayer == null || evt.Side != CombatSide.Player)
                    return;

                LogUtility.Info(
                    "[BuffUtility] Player combat turn started — checking for queued buff(s) to apply."
                );

                /// Fire-and-forget is intentional here: the processing is async (DataStorage
                /// reads/writes), but we don't want to block the game's main thread.
                _ = ProcessQueuedBuffsAsync(GameUtility.CurrentPlayer);
            });

            LogUtility.Info("[BuffUtility] Initialized — subscribed to SideTurnStartingEvent.");
        }

        #endregion

        #region Storage

        /// <summary>
        /// Reads the index of the last consumed buff from the AP server's DataStorage
        /// and populates <see cref="_lastConsumedBuffIndex"/>.
        ///
        /// This must be called early in the connect flow (in <see cref="ArchipelagoClient.OnConnected"/>)
        /// so that reconnect replays do not re-queue already-applied buffs. The load task is
        /// stored in <see cref="_storageLoadTask"/> so that <see cref="ProcessQueuedBuffsAsync"/>
        /// can await it as a safety net if the combat turn fires before the load completes.
        /// </summary>
        public static async Task LoadFromStorageAsync()
        {
            if (!ArchipelagoClient.IsConnected)
            {
                LogUtility.Warn(
                    "[BuffUtility] LoadFromStorageAsync called while not connected — skipping."
                );
                return;
            }

            // Store the task so ProcessQueuedBuffsAsync can await it if needed.
            _storageLoadTask = LoadFromStorageInternalAsync();
            await _storageLoadTask;
        }

        /// <summary>
        /// Internal implementation of the DataStorage read. Separated from
        /// <see cref="LoadFromStorageAsync"/> so the task can be awaited by
        /// <see cref="ProcessQueuedBuffsAsync"/> independently.
        /// </summary>
        private static async Task LoadFromStorageInternalAsync()
        {
            try
            {
                /// Initialize to -1 ("nothing consumed yet") if this key has never been written.
                /// This is a no-op if the key already holds a value.
                ArchipelagoClient.Session.DataStorage[Scope.Slot, StorageKey].Initialize(-1);

                /// Read the stored high-water mark — the index of the most recently applied buff.
                /// Any buff at an index <= this value is guaranteed to be already consumed.
                var stored = await ArchipelagoClient
                    .Session.DataStorage[Scope.Slot, StorageKey]
                    .GetAsync<int>();

                _lastConsumedBuffIndex = stored;

                LogUtility.Info(
                    $"[BuffUtility] Loaded last consumed buff index: {_lastConsumedBuffIndex} (-1 means no buffs consumed yet)."
                );
            }
            catch (Exception ex)
            {
                /// If we can't read from DataStorage, default to -1 (nothing consumed).
                /// The worst outcome is a previously-applied buff being re-applied once.
                /// We log a warning so the issue is visible in the logs.
                LogUtility.Warn(
                    $"[BuffUtility] Failed to load last consumed buff index from DataStorage: {ex.Message}. Defaulting to -1."
                );
                _lastConsumedBuffIndex = -1;
            }
        }

        #endregion

        #region Queuing

        /// <summary>
        /// Enqueues a buff item to be applied on the player's next combat turn.
        ///
        /// <para>
        /// Called from <see cref="ArchipelagoClient.ProcessItem"/> when a buff-type AP item
        /// is received. If the storage load has already completed and we know this index was
        /// previously consumed, the buff is skipped immediately. Otherwise it is enqueued and
        /// the consumed check is deferred to <see cref="ProcessQueuedBuffsAsync"/>, which always
        /// awaits the storage load before applying anything.
        /// </para>
        /// </summary>
        /// <param name="buffType">The AP item type identifying which buff to apply.</param>
        /// <param name="itemIndex">
        ///   The Archipelago item index. This is the server-assigned sequential index that
        ///   uniquely identifies this item receive event across all sessions and clients.
        ///   It is stable across reconnects, making it safe to use as a permanent consumed key.
        /// </param>
        public static void EnqueueBuff(APItem buffType, int itemIndex)
        {
            /// Fast-path consumed check: if storage is already loaded, any buff at or below
            /// the last consumed index is guaranteed to be already applied and can be skipped
            /// immediately without touching the queue.
            if (
                _storageLoadTask != null
                && _storageLoadTask.IsCompleted
                && itemIndex <= _lastConsumedBuffIndex
            )
            {
                LogUtility.Info(
                    $"[BuffUtility] Buff '{buffType}' (index {itemIndex}) is at or below last consumed index ({_lastConsumedBuffIndex}) — skipping (fast path)."
                );
                return;
            }

            /// Enqueue for later application on the next player combat turn.
            /// If storage hasn't loaded yet, we enqueue anyway; ProcessQueuedBuffsAsync will
            /// await the storage load and perform the consumed check before applying.
            _buffQueue.Enqueue((buffType, itemIndex));
            LogUtility.Info(
                $"[BuffUtility] Buff '{buffType}' (index {itemIndex}) enqueued. Queue size: {_buffQueue.Count}."
            );
        }

        #endregion

        #region Apply

        /// <summary>
        /// Drains the buff queue and applies each pending buff to the player.
        ///
        /// Awaits <see cref="_storageLoadTask"/> before processing, ensuring that the
        /// consumed-index set is populated from DataStorage even if the combat turn fires
        /// before the initial storage load completes (a rare but possible race condition on
        /// fast reconnects).
        ///
        /// Buffs are applied first, then marked as consumed and persisted to DataStorage.
        /// This is intentional: we prefer a buff being applied twice over being silently
        /// lost. The only way a double-apply can occur is if the game disconnects between
        /// the application and the DataStorage write, which is extremely unlikely.
        /// </summary>
        /// <param name="player">The active Player instance for the current run.</param>
        public static async Task ProcessQueuedBuffsAsync(Player player)
        {
            /// Safety net: if storage hasn't finished loading yet, wait for it now.
            /// This handles the edge case where the player was already in combat when they
            /// reconnected, and a SideTurnStartingEvent fired before LoadFromStorageAsync
            /// completed.
            if (_storageLoadTask != null && !_storageLoadTask.IsCompleted)
            {
                LogUtility.Info(
                    "[BuffUtility] Waiting for DataStorage load to complete before applying buffs..."
                );
                await _storageLoadTask;
            }

            if (_buffQueue.Count == 0)
                return;

            LogUtility.Info(
                $"[BuffUtility] Applying {_buffQueue.Count} queued buff(s) to the player."
            );

            // Drain the queue and apply each buff
            while (_buffQueue.TryDequeue(out var entry))
            {
                var (buffType, itemIndex) = entry;

                /// Final consumed check now that storage is guaranteed to be loaded.
                /// This catches buffs that were enqueued before the storage load finished
                /// (i.e., they were received during the connect flow before LoadFromStorageAsync
                /// completed and were therefore not caught by EnqueueBuff's fast-path check).
                if (itemIndex <= _lastConsumedBuffIndex)
                {
                    LogUtility.Info(
                        $"[BuffUtility] Buff '{buffType}' (index {itemIndex}) is at or below last consumed index ({_lastConsumedBuffIndex}) — skipping (deferred check)."
                    );
                    continue;
                }

                // Update the in-memory high-water mark. The last time this is updated is what will be sync'd to the server.
                _lastConsumedBuffIndex = itemIndex;

                try
                {
                    /// Apply the buff effect to the player first.
                    /// We apply BEFORE marking as consumed so that if the game crashes or
                    /// disconnects mid-apply, the buff can be reapplied on the next session
                    /// rather than being silently lost.
                    LogUtility.Info(
                        $"[BuffUtility] Applying buff '{buffType}' (index {itemIndex}) to player."
                    );
                    await ApplyBuff(buffType, player);
                }
                catch (Exception ex)
                {
                    LogUtility.Error(
                        $"[BuffUtility] Failed to apply buff '{buffType}' (index {itemIndex}): {ex.Message}"
                    );
                }
            }

            // Sync the last applied buff index to DataStorage so it is persisted across sessions.
            if (ArchipelagoClient.IsConnected)
            {
                ArchipelagoClient.Session.DataStorage[Scope.Slot, StorageKey] =
                    _lastConsumedBuffIndex;
                LogUtility.Info(
                    $"[BuffUtility] Last consumed buff index is now {_lastConsumedBuffIndex}."
                );
            }
            else
            {
                LogUtility.Warn(
                    $"[BuffUtility] Buff(s) could NOT be persisted. They may re-apply next session."
                );
            }
        }

        /// <summary>
        /// Applies the specified buff to the player. This method is intentionally stubbed
        /// and should be implemented with the actual game logic for each buff type.
        /// </summary>
        /// <param name="buffType">The type of buff to apply.</param>
        /// <param name="player">The active Player instance for the current run.</param>
        private static async Task ApplyBuff(APItem buffType, Player player)
        {
            // Load the correct power to apply
            switch (buffType)
            {
                case APItem.FreeAttack:
                    await PowerCmd.Apply<FreeAttackPower>(
                        new BlockingPlayerChoiceContext(),
                        player.Creature,
                        1,
                        player.Creature,
                        null
                    );
                    break;
                case APItem.FreePower:
                    await PowerCmd.Apply<FreePowerPower>(
                        new BlockingPlayerChoiceContext(),
                        player.Creature,
                        1,
                        player.Creature,
                        null
                    );
                    break;
                case APItem.FreeSkill:
                    await PowerCmd.Apply<FreeSkillPower>(
                        new BlockingPlayerChoiceContext(),
                        player.Creature,
                        1,
                        player.Creature,
                        null
                    );
                    break;
                case APItem.Dexterity:
                    await PowerCmd.Apply<DexterityPower>(
                        new BlockingPlayerChoiceContext(),
                        player.Creature,
                        2,
                        player.Creature,
                        null
                    );
                    break;
                case APItem.Strength:
                    await PowerCmd.Apply<StrengthPower>(
                        new BlockingPlayerChoiceContext(),
                        player.Creature,
                        2,
                        player.Creature,
                        null
                    );
                    break;
                case APItem.Plating:
                    await PowerCmd.Apply<PlatingPower>(
                        new BlockingPlayerChoiceContext(),
                        player.Creature,
                        5,
                        player.Creature,
                        null
                    );
                    break;
                case APItem.Friendship:
                    await PowerCmd.Apply<FriendshipPower>(
                        new BlockingPlayerChoiceContext(),
                        player.Creature,
                        1,
                        player.Creature,
                        null
                    );
                    break;
                case APItem.PostCombatCardUpgrade:
                    await PowerCmd.Apply<ImprovementPower>(
                        new BlockingPlayerChoiceContext(),
                        player.Creature,
                        1,
                        player.Creature,
                        null
                    );
                    break;
                default:
                    LogUtility.Warn(
                        $"[BuffUtility] ApplyBuff: unrecognized buff type '{buffType}'."
                    );
                    break;
            }
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Clears the pending buff queue on disconnect or when resetting state.
        ///
        /// Only the application queue is cleared; <see cref="_lastConsumedBuffIndex"/> is
        /// intentionally preserved in memory, and a fresh value will be loaded from DataStorage
        /// on the next connect. Any buffs that were queued but not yet applied will be
        /// re-enqueued from the server's item replay, and those at or below the reloaded
        /// high-water mark will be filtered out at that time.
        /// </summary>
        public static void ClearQueue()
        {
            int pendingCount = _buffQueue.Count;
            _buffQueue.Clear();

            /// Reset the storage load task so that LoadFromStorageAsync runs fresh on the
            /// next connect. Without this, a stale completed task from the previous session
            /// would cause ProcessQueuedBuffsAsync to skip the load-await guard, potentially
            /// processing buffs before the DataStorage read for the new session completes.
            _storageLoadTask = null;

            LogUtility.Info(
                $"[BuffUtility] Queue cleared ({pendingCount} pending buff(s) discarded). Last consumed buff index ({_lastConsumedBuffIndex}) will be reloaded from DataStorage on next connect."
            );
        }

        #endregion
    }
}
