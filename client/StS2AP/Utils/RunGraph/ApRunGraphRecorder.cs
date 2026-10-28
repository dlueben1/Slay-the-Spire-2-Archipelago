using Archipelago.MultiClient.Net.Models;
using Godot;
using MegaCrit.Sts2.Core.Context;
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
                    Describe(e, item, item.Player.Slot == mySlot);
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
                Describe(e, receipt.Item, receipt.Item.Player.Slot == mySlot);
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

    /// <summary>
    /// A checkpoint reload rewinds the run but not the checks it already sent. Mark everything
    /// after the restored floor so the graph can show those checks as a reloaded branch.
    /// </summary>
    internal static void OnCheckpointRestored(long startTime, int completedFloors)
    {
        Guard(nameof(OnCheckpointRestored), () =>
        {
            var store = Store;
            var log = _active?.StartTime == startTime ? _active : store.Load(startTime, out _);
            if (log == null || log.MarkAbandonedAfter(completedFloors) == 0)
                return;
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

    private static void Describe(ApRunGraphEvent e, ItemInfo item, bool isSelf)
    {
        e.ItemName = item.ItemDisplayName;
        e.OtherPlayer = item.Player.Name;
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
