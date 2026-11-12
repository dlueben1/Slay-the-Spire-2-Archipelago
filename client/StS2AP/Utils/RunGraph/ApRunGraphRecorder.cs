using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace StS2AP.Utils.RunGraph;

/// <summary>
/// Records the local player's AP activity by floor for the end-of-run graph. Every entry point
/// swallows its own failures: a graph problem must never interrupt a run.
/// </summary>
internal static class ApRunGraphRecorder
{
    private static ApRunGraphLog? _active;

    internal static ApRunGraphStore Store => new(ProjectSettings.GlobalizePath(
        $"user://ArchipelagoRunGraphs/profile-{SaveManager.Instance.CurrentProfileId}"));

    /// <summary>Opens the log for a new or continued run, or stops recording for a non-AP run.</summary>
    internal static void BeginRun(RunState state)
    {
        _active = null;
        Guard(nameof(BeginRun), () =>
        {
            var session = ArchipelagoClient.Session;
            if (!ArchipelagoClient.HasAuthenticatedSlot || session == null)
                return;

            long startTime = RunManager.Instance._startTime;
            var store = Store;
            var log = store.Load(startTime, out string? error) ?? new ApRunGraphLog
            {
                StartTime = startTime,
                SlotChecksAtStart = ArchipelagoClient.CheckedLocations.Count,
            };
            if (error != null)
                LogUtility.Warn(error + " Starting a new run graph log.");

            var me = LocalContext.GetMe(state) ?? state.Players.FirstOrDefault();
            log.Seed = session.RoomState.Seed;
            log.SlotName = ArchipelagoClient.PlayerName;
            log.SlotLocationCount = ArchipelagoClient.SlotLocationIds.Count;
            log.IsMultiplayer = state.Players.Count > 1;
            if (me != null)
            {
                log.LocalNetId = me.NetId;
                log.Character = me.Character.Id.Entry;
            }
            _active = log;
            store.Save(log);
        });
    }

    internal static void RecordChecks(IEnumerable<long> locationIds)
    {
        if (_active == null)
            return;
        Guard(nameof(RecordChecks), () =>
        {
            int mySlot = ArchipelagoClient.Session?.ConnectionInfo.Slot ?? -1;
            foreach (long id in locationIds)
            {
                var e = new ApRunGraphEvent { Kind = ApRunGraphEventKind.CheckSent, LocationId = id };
                if (ArchipelagoClient.ScoutedLocations.TryGetValue(id, out ScoutedItemInfo? item))
                    Describe(e, item, item.Player.Slot == mySlot, game: item.ItemGame);
                else
                    e.ItemName = $"Location {id}";
                Append(e);
            }
        });
    }

    /// <summary>Records an AP item the local player took from a reward menu.</summary>
    internal static void RecordItemClaim(int itemIndex)
    {
        if (_active == null)
            return;
        Guard(nameof(RecordItemClaim), () =>
        {
            var receipt = ArchipelagoClient.Progress.AllReceivedItems.FirstOrDefault(r => r.Index == itemIndex);
            var e = new ApRunGraphEvent { Kind = ApRunGraphEventKind.ItemClaimed };
            if (receipt != null)
            {
                int mySlot = ArchipelagoClient.Session?.ConnectionInfo.Slot ?? -1;
                Describe(e, receipt.Item, receipt.Item.Player.Slot == mySlot, game: receipt.Item.LocationGame);
            }
            else
            {
                e.ItemName = $"Item #{itemIndex}";
            }
            Append(e);
        });
    }

    /// <summary>Gold is pooled from every sender, so it has an amount but no single source.</summary>
    internal static void RecordGoldClaim(int amount)
    {
        if (_active == null)
            return;
        Guard(nameof(RecordGoldClaim), () => Append(new ApRunGraphEvent
        {
            Kind = ApRunGraphEventKind.GoldClaimed,
            ItemName = "Gold",
            Amount = amount,
        }));
    }

    internal static void RecordDeathLinkSent()
    {
        if (_active == null)
            return;
        Guard(nameof(RecordDeathLinkSent), () =>
            Append(new ApRunGraphEvent { Kind = ApRunGraphEventKind.DeathLinkSent }));
    }

    internal static void RecordDeathLinkReceived(string source)
    {
        if (_active == null)
            return;
        Guard(nameof(RecordDeathLinkReceived), () =>
            Append(new ApRunGraphEvent { Kind = ApRunGraphEventKind.DeathLinkReceived, OtherPlayer = source }));
    }

    /// <summary>How many events the run's log holds now, for a save to remember; null when no log is open for it.</summary>
    internal static int? EventCountFor(long startTime) =>
        _active?.StartTime == startTime ? _active.Events.Count : null;

    /// <summary>
    /// A checkpoint reload rewinds the run but not the checks it already sent. Mark everything
    /// recorded after the checkpoint was saved so the graph can show it as a reloaded branch.
    /// <paramref name="eventCount"/> is the log's size when the checkpoint was saved; an older
    /// checkpoint without it falls back to marking what came after its floor.
    /// </summary>
    internal static void OnCheckpointRestored(long startTime, int completedFloors, int? eventCount)
    {
        Guard(nameof(OnCheckpointRestored), () =>
        {
            var store = Store;
            var log = _active?.StartTime == startTime ? _active : store.Load(startTime, out _);
            if (log == null)
                return;
            // A count larger than this log came from a log on another machine (a remote save).
            int marked = eventCount is { } count && count <= log.Events.Count
                ? log.MarkAbandonedFrom(count)
                : log.MarkAbandonedAfter(completedFloors);
            if (marked > 0)
                store.Save(log);
        });
    }

    internal static void FinishRun(bool won)
    {
        if (_active == null)
            return;
        Guard(nameof(FinishRun), () =>
        {
            _active.Outcome = won ? ApRunOutcome.Won : ApRunOutcome.Lost;
            _active.SlotChecksAtEnd = ArchipelagoClient.CheckedLocations.Count;
            Store.Save(_active);
        });
    }

    /// <summary>
    /// Keeps the run as the game just saved it, so a run that never reaches a game-over screen
    /// can still open a full recap. Called at every room save, checkpoint or not.
    /// </summary>
    internal static void CaptureSnapshot(SerializableRun run)
    {
        if (_active == null || run.StartTime != _active.StartTime)
            return;
        Guard(nameof(CaptureSnapshot), () => WriteSnapshot(run));
    }

    /// <summary>
    /// For saves where the AP client skips the game's own <c>ToSave</c>: takes it here and returns
    /// it so a checkpoint on the same save can reuse it. Null when not recording or it failed.
    /// </summary>
    internal static SerializableRun? CaptureSnapshot(AbstractRoom? preFinishedRoom)
    {
        if (_active == null || !RunManager.Instance.ShouldSave)
            return null;
        SerializableRun? run = null;
        Guard(nameof(CaptureSnapshot), () =>
        {
            run = RunManager.Instance.ToSave(preFinishedRoom);
            if (run.StartTime == _active.StartTime)
                WriteSnapshot(run);
        });
        return run;
    }

    /// <summary>
    /// Save and quit writes no save of its own, so the last room save may be a floor behind where
    /// the player stopped. Take one more snapshot, with the current floor's HP, while the run still
    /// exists. A run that reached the game-over screen has its history instead.
    /// </summary>
    internal static void CaptureAtQuit()
    {
        if (_active == null)
            return;
        Guard(nameof(CaptureAtQuit), () =>
        {
            var manager = RunManager.Instance;
            if (manager.DebugOnlyGetState() is not RunState state || state.IsGameOver || manager.IsAbandoned
                || manager._startTime != _active.StartTime)
                return;
            // The game updates a floor's HP and gold only when it leaves the floor. The run is
            // ending, so updating them early changes nothing it will save.
            manager.UpdatePlayerStatsInMapPointHistory();
            WriteSnapshot(manager.ToSave(null));
        });
        // Nothing is recorded between runs; the next run opens its own log.
        _active = null;
    }

    /// <summary>
    /// Keeps only what the recap reads (players, floors, times). The rest of a save, such as the
    /// map, RNG and drawings, would make each snapshot several times larger.
    /// </summary>
    private static void WriteSnapshot(SerializableRun run)
    {
        if (_active == null)
            return;
        var recapOnly = new SerializableRun
        {
            SchemaVersion = run.SchemaVersion,
            StartTime = run.StartTime,
            SaveTime = run.SaveTime,
            RunTime = run.RunTime,
            WinTime = run.WinTime,
            Ascension = run.Ascension,
            CurrentActIndex = run.CurrentActIndex,
            GameMode = run.GameMode,
            Players = run.Players,
            MapPointHistory = run.MapPointHistory,
        };
        var store = Store;
        store.SaveSnapshot(run.StartTime, JsonSerializationUtility.ToJson(recapOnly));

        // The floor count from the map history, as the recap draws it: a save on entering a room
        // comes before the game adds that floor, so it counts the floors already done.
        _active.SavedFloor = run.FloorReached;
        _active.FurthestSavedFloor = Math.Max(_active.FurthestSavedFloor, run.FloorReached);
        store.Save(_active);
    }

    /// <summary>The run as last snapshotted, or null when it has none or it cannot be read.</summary>
    internal static SerializableRun? LoadSnapshot(long startTime)
    {
        string? json = Store.LoadSnapshot(startTime, out string? error);
        if (error != null)
            LogUtility.Warn(error);
        if (json == null)
            return null;
        var result = JsonSerializationUtility.FromJson<SerializableRun>(json);
        if (result.Success && result.SaveData != null)
            return result.SaveData;
        LogUtility.Warn($"Run snapshot {startTime} could not be read: {result.ErrorMessage ?? result.Status.ToString()}");
        return null;
    }

    private static void Describe(ApRunGraphEvent e, ItemInfo item, bool isSelf, string game)
    {
        e.ItemName = item.ItemDisplayName;
        e.OtherPlayer = item.Player.Name;
        e.OtherGame = game;
        e.IsSelf = isSelf;
        e.Classification = ApRunGraphLog.Classify(item.Advancement(), item.Useful(), item.Trap());
    }

    private static void Append(ApRunGraphEvent e)
    {
        if (_active == null)
            return;
        if (RunManager.Instance.DebugOnlyGetState() is RunState state)
        {
            e.Floor = state.TotalFloor;
            e.ActIndex = state.CurrentActIndex;
        }
        _active.Events.Add(e);
        Store.Save(_active);
    }

    private static void Guard(string operation, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Run graph {operation} failed: {ex}");
        }
    }
}
