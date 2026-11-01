using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ApRunEconomyTests
{
    [Fact]
    public void ApGoldIsTakenOutOfTheFloorsGameGold()
    {
        var floors = new[]
        {
            new ApEconomyFloor(ApEconomyRoom.Other, 0, 0, 0, 0, 99),
            new ApEconomyFloor(ApEconomyRoom.Combat, 90, 0, 0, 0, 189),
            new ApEconomyFloor(ApEconomyRoom.Other, 50, 0, 0, 0, 239),
        };
        var log = new ApRunGraphLog();
        log.Events.Add(Gold(2, 75));
        log.Events.Add(Gold(3, 50));

        var economy = ApRunEconomy.Build(floors, log);

        Assert.Equal(15, economy.CombatGold);
        Assert.Equal(0, economy.OtherGold);
        Assert.Equal(125, economy.ApGold);
        Assert.Equal(140, economy.Gained);
        Assert.Equal(new[] { 0, 75, 50 }, economy.ApGoldByFloor);
    }

    [Fact]
    public void GoldClaimsUndoneByAReloadAreLeftOut()
    {
        var floors = new[] { new ApEconomyFloor(ApEconomyRoom.Combat, 20, 0, 0, 0, 119) };
        var log = new ApRunGraphLog();
        log.Events.Add(Gold(1, 100, abandoned: true));

        var economy = ApRunEconomy.Build(floors, log);

        Assert.Equal(0, economy.ApGold);
        Assert.Equal(20, economy.CombatGold);
    }

    [Fact]
    public void AClaimTheHistoryMissedNeverMakesGameGoldNegative()
    {
        var floors = new[] { new ApEconomyFloor(ApEconomyRoom.Other, 10, 0, 0, 0, 109) };
        var log = new ApRunGraphLog();
        log.Events.Add(Gold(1, 40));

        var economy = ApRunEconomy.Build(floors, log);

        Assert.Equal(0, economy.OtherGold);
        Assert.Equal(40, economy.ApGold);
    }

    [Fact]
    public void SpendingSplitsShopsFromEverywhereElse()
    {
        var floors = new[]
        {
            new ApEconomyFloor(ApEconomyRoom.Combat, 30, 0, 0, 20, 109),
            new ApEconomyFloor(ApEconomyRoom.Shop, 0, 75, 0, 0, 34),
            new ApEconomyFloor(ApEconomyRoom.Other, 0, 10, 15, 0, 9),
        };

        var economy = ApRunEconomy.Build(floors, new ApRunGraphLog());

        Assert.Equal(75, economy.ShopSpent);
        Assert.Equal(10, economy.OtherSpent);
        Assert.Equal(85, economy.Spent);
        Assert.Equal(15, economy.Lost);
        Assert.Equal(20, economy.Stolen);
        // 109 at the end of the first floor, after +30 and 20 stolen.
        Assert.Equal(99, economy.StartGold);
        Assert.Equal(9, economy.EndGold);
        Assert.Equal(109, economy.PeakGold);
        Assert.Equal(0, economy.PeakFloor);
    }

    [Fact]
    public void ARunWithoutFloorsHasNoNumbers()
    {
        var economy = ApRunEconomy.Build(Array.Empty<ApEconomyFloor>(), new ApRunGraphLog());

        Assert.Equal(0, economy.Gained);
        Assert.Equal(-1, economy.PeakFloor);
        Assert.Empty(economy.ApGoldByFloor);
    }

    private static ApRunGraphEvent Gold(int floor, int amount, bool abandoned = false) => new()
    {
        Kind = ApRunGraphEventKind.GoldClaimed,
        Floor = floor,
        ItemName = "Gold",
        Amount = amount,
        Abandoned = abandoned,
    };
}
