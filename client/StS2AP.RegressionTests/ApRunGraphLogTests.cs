using System.Text.Json.Nodes;
using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ApRunGraphLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ap-run-graph-" + Guid.NewGuid().ToString("N"));
    private ApRunGraphStore Store => new(_root);

    [Fact]
    public void LogRoundTripsThroughTheStore()
    {
        var log = new ApRunGraphLog { StartTime = 1_780_000_000, Seed = "seed", SlotName = "Ironclad", SlotLocationCount = 120 };
        log.Events.Add(Check(4, ApRunGraphItemType.Progression));
        log.Events.Add(new ApRunGraphEvent { Kind = ApRunGraphEventKind.GoldClaimed, Floor = 6, Amount = 75, OtherPlayer = "Bob" });
        log.Outcome = ApRunOutcome.Lost;

        Store.Save(log);

        Assert.True(File.Exists(Path.Combine(_root, "1780000000.json")));
        var loaded = Store.Load(1_780_000_000, out string? error);
        Assert.Null(error);
        Assert.NotNull(loaded);
        Assert.Equal("Ironclad", loaded.SlotName);
        Assert.Equal(ApRunOutcome.Lost, loaded.Outcome);
        Assert.Equal(2, loaded.Events.Count);
        Assert.Equal(ApRunGraphItemType.Progression, loaded.Events[0].Classification);
        Assert.Equal(75, loaded.Events[1].Amount);
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public void MissingOrCorruptLogLoadsAsNull()
    {
        Assert.Null(Store.Load(1, out string? missingError));
        Assert.Null(missingError);

        Directory.CreateDirectory(_root);
        File.WriteAllText(Store.PathFor(2), "{ not json");
        Assert.Null(Store.Load(2, out string? corruptError));
        Assert.NotNull(corruptError);
    }

    [Fact]
    public void ListsLoggedRunsNewestFirstAndSkipsOtherFiles()
    {
        Assert.Empty(Store.ListStartTimes());
        Store.Save(new ApRunGraphLog { StartTime = 10 });
        Store.Save(new ApRunGraphLog { StartTime = 30 });
        Store.Save(new ApRunGraphLog { StartTime = 20 });
        File.WriteAllText(Path.Combine(_root, "notes.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "40.json.tmp-abc"), "{}");

        Assert.Equal(new long[] { 30, 20, 10 }, Store.ListStartTimes());
    }

    [Fact]
    public void LogFromAnotherRunIsRejected()
    {
        Store.Save(new ApRunGraphLog { StartTime = 3 });
        File.Copy(Store.PathFor(3), Store.PathFor(4));
        Assert.Null(Store.Load(4, out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void RestoreMarksOnlyLaterFloorsAndNeverUnmarks()
    {
        var log = new ApRunGraphLog { StartTime = 5 };
        log.Events.Add(Check(9, ApRunGraphItemType.Filler));
        log.Events.Add(Check(12, ApRunGraphItemType.Useful));
        log.Events.Add(Check(20, ApRunGraphItemType.Progression));

        // Reload the floor-9 checkpoint: checks on floor 9 itself stay solid.
        Assert.Equal(2, log.MarkAbandonedAfter(9));
        Assert.Equal(new[] { false, true, true }, log.Events.Select(e => e.Abandoned));

        // Play on and send a new check, then reload an even earlier checkpoint.
        log.Events.Add(Check(11, ApRunGraphItemType.Trap));
        Assert.Equal(2, log.MarkAbandonedAfter(5));
        Assert.All(log.Events, e => Assert.True(e.Abandoned));

        // A later reload never brings an abandoned event back.
        Assert.Equal(0, log.MarkAbandonedAfter(30));
        Assert.All(log.Events, e => Assert.True(e.Abandoned));
    }

    [Fact]
    public void EventsAfterARestoreStayUnmarked()
    {
        var log = new ApRunGraphLog { StartTime = 6 };
        log.Events.Add(Check(15, ApRunGraphItemType.Useful));
        log.MarkAbandonedAfter(10);
        log.Events.Add(Check(15, ApRunGraphItemType.Filler));
        Assert.True(log.Events[0].Abandoned);
        Assert.False(log.Events[1].Abandoned);
    }

    [Theory]
    [InlineData(true, true, true, ApRunGraphItemType.Progression)]
    [InlineData(false, true, true, ApRunGraphItemType.Useful)]
    [InlineData(false, false, true, ApRunGraphItemType.Trap)]
    [InlineData(false, false, false, ApRunGraphItemType.Filler)]
    public void ClassificationPrefersProgressionThenUsefulThenTrap(
        bool progression, bool useful, bool trap, ApRunGraphItemType expected)
    {
        Assert.Equal(expected, ApRunGraphLog.Classify(progression, useful, trap));
    }

    [Fact]
    public void DeleteRemovesOnlyThatRunsLog()
    {
        Store.Save(new ApRunGraphLog { StartTime = 1 });
        Store.Save(new ApRunGraphLog { StartTime = 2 });

        Store.Delete(1);

        Assert.Equal(new long[] { 2 }, Store.ListStartTimes());
    }

    [Fact]
    public void ExportBundleKeepsTheRunHistoryWholeAndExportsAreNotListed()
    {
        var log = new ApRunGraphLog { StartTime = 7, SlotName = "Ironclad" };
        log.Events.Add(Check(3, ApRunGraphItemType.Progression));
        Store.Save(log);

        var bundle = ApRunGraphStore.ExportBundle(log, """{ "win": true, "map_point_history": [[{ "unknown_field": 4 }]] }""");
        string path = Store.WriteExport("7-ironclad.json", bundle);

        var read = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal(ApRunGraphStore.ExportFormat, read["format"]!.GetValue<string>());
        Assert.Equal("Ironclad", read["ap"]!["slot_name"]!.GetValue<string>());
        Assert.Equal("progression", read["ap"]!["events"]![0]!["classification"]!.GetValue<string>());
        Assert.Equal(4, read["run"]!["map_point_history"]![0]![0]!["unknown_field"]!.GetValue<int>());
        Assert.Equal(new long[] { 7 }, Store.ListStartTimes());
    }

    [Fact]
    public void UnfinishedRunExportsWithNullHistory()
    {
        var bundle = ApRunGraphStore.ExportAllBundle(new[]
        {
            ApRunGraphStore.ExportBundle(new ApRunGraphLog { StartTime = 8 }, null),
        });

        var run = bundle["runs"]![0]!;
        Assert.Equal(ApRunGraphStore.ExportAllFormat, bundle["format"]!.GetValue<string>());
        Assert.Null(run["run"]);
        Assert.Equal(8, run["ap"]!["start_time"]!.GetValue<long>());
    }

    private static ApRunGraphEvent Check(int floor, ApRunGraphItemType classification) => new()
    {
        Kind = ApRunGraphEventKind.CheckSent,
        Floor = floor,
        ItemName = "Item",
        OtherPlayer = "Bob",
        Classification = classification,
    };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
