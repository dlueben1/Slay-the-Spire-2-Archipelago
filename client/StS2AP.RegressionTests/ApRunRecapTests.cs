using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ApRunRecapTests
{
    [Fact]
    public void ChecksOnAnUndoneBranchStillCountAsSent()
    {
        var log = new ApRunGraphLog();
        log.Events.Add(Sent(2, "Mira", ApRunGraphItemType.Filler));
        log.Events.Add(Sent(5, "Mira", ApRunGraphItemType.Progression, abandoned: true));
        log.Events.Add(Sent(6, "Me", ApRunGraphItemType.Useful, self: true));

        var recap = ApRunRecap.Build(log, Array.Empty<ApRecapFloorHp>());

        Assert.Equal(3, recap.ChecksSent);
        Assert.Equal(1, recap.SentToSelf);
        Assert.Equal(2, recap.SentToOthers);
        Assert.Equal(1, recap.SentOnUndoneBranch);
        Assert.Equal(1, recap.SentByType[ApRunGraphItemType.Progression]);
        Assert.Equal(0, recap.SentByType[ApRunGraphItemType.Trap]);
    }

    [Fact]
    public void PlayersAreOrderedByChecksAndKeepTheirMostImportantFind()
    {
        var log = new ApRunGraphLog();
        log.Events.Add(Sent(1, "Tobias", ApRunGraphItemType.Filler));
        log.Events.Add(Sent(3, "Mira", ApRunGraphItemType.Filler, game: "A Link to the Past"));
        log.Events.Add(Sent(7, "Mira", ApRunGraphItemType.Progression, item: "Hookshot"));
        log.Events.Add(Sent(9, "Mira", ApRunGraphItemType.Progression, item: "Bombos"));

        var recap = ApRunRecap.Build(log, Array.Empty<ApRecapFloorHp>());

        Assert.Equal(new[] { "Mira", "Tobias" }, recap.Players.Select(p => p.Name));
        Assert.Equal("A Link to the Past", recap.Players[0].Game);
        Assert.Equal(3, recap.Players[0].Checks);
        Assert.Equal("Hookshot", recap.Players[0].BestFind.ItemName);
    }

    [Fact]
    public void TheFloorALostRunEndedOnIsNotTheClosestCall()
    {
        var log = new ApRunGraphLog { Outcome = ApRunOutcome.Lost };
        var floors = new[] { new ApRecapFloorHp(70, 75), new ApRecapFloorHp(12, 75), new ApRecapFloorHp(3, 75) };

        var closest = ApRunRecap.Build(log, floors).Highlights
            .Single(h => h.Kind == ApRecapHighlightKind.ClosestCall);

        Assert.Equal("12 HP", closest.Value);
    }

    [Fact]
    public void HighlightsWithNothingToShowAreLeftOut()
    {
        var log = new ApRunGraphLog { Outcome = ApRunOutcome.Won };
        log.Events.Add(Sent(4, "Me", ApRunGraphItemType.Filler, self: true));

        var kinds = ApRunRecap.Build(log, Array.Empty<ApRecapFloorHp>()).Highlights.Select(h => h.Kind);

        Assert.Equal(new[] { ApRecapHighlightKind.HomeCooking }, kinds);
    }

    [Fact]
    public void ClaimsAReloadUndidAreNotCounted()
    {
        var log = new ApRunGraphLog();
        log.Events.Add(new ApRunGraphEvent { Kind = ApRunGraphEventKind.GoldClaimed, Floor = 1, Amount = 116, Abandoned = true });
        log.Events.Add(new ApRunGraphEvent { Kind = ApRunGraphEventKind.ItemClaimed, Floor = 1, ItemName = "Relic", Abandoned = true });
        log.Events.Add(new ApRunGraphEvent { Kind = ApRunGraphEventKind.GoldClaimed, Floor = 1, Amount = 116 });
        log.Events.Add(new ApRunGraphEvent { Kind = ApRunGraphEventKind.ItemClaimed, Floor = 1, ItemName = "Relic" });

        var recap = ApRunRecap.Build(log, Array.Empty<ApRecapFloorHp>());

        Assert.Single(recap.Claims);
        Assert.Equal(116, recap.GoldClaimed);
    }

    [Fact]
    public void ReloadingACheckpointUndoesEverythingRecordedAfterIt()
    {
        // The checkpoint was saved on floor 1 with one event in the log; the claims after it,
        // also on floor 1, are made again after the reload.
        var log = new ApRunGraphLog();
        log.Events.Add(Sent(1, "Me", ApRunGraphItemType.Filler, self: true));
        log.Events.Add(new ApRunGraphEvent { Kind = ApRunGraphEventKind.ItemClaimed, Floor = 1 });
        log.Events.Add(Sent(3, "Mira", ApRunGraphItemType.Progression));

        Assert.Equal(2, log.MarkAbandonedFrom(1));
        Assert.Equal(new[] { false, true, true }, log.Events.Select(e => e.Abandoned));
        Assert.Equal(0, log.MarkAbandonedFrom(1));
    }

    private static ApRunGraphEvent Sent(
        int floor,
        string player,
        ApRunGraphItemType type,
        bool abandoned = false,
        bool self = false,
        string item = "Item",
        string game = "") => new()
    {
        Kind = ApRunGraphEventKind.CheckSent,
        Floor = floor,
        OtherPlayer = player,
        OtherGame = game,
        IsSelf = self,
        Classification = type,
        ItemName = item,
        Abandoned = abandoned,
    };
}
