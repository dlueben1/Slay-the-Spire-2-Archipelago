using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ApCardRewardGroupsTests
{
    [Fact]
    public void ATakenRewardAndASkippedOneSplitIntoThrees()
    {
        var groups = ApCardRewardGroups.Split(new[] { true, false, false, false, false, false });

        Assert.Equal(new[] { new ApCardRewardGroup(0, 3), new ApCardRewardGroup(3, 3) }, groups);
    }

    [Fact]
    public void FourCardOffersAreRecognised()
    {
        // A pick in the middle of a three-card split rules threes out.
        var groups = ApCardRewardGroups.Split(new[] { false, false, false, false, true, false, false, false });

        Assert.Equal(new[] { new ApCardRewardGroup(0, 4), new ApCardRewardGroup(4, 4) }, groups);
    }

    [Fact]
    public void OddListsStartANewRewardAtEachTakenCard()
    {
        var groups = ApCardRewardGroups.Split(new[] { true, false, false, false, true, false, false });

        Assert.Equal(new[] { new ApCardRewardGroup(0, 4), new ApCardRewardGroup(4, 3) }, groups);
    }

    [Fact]
    public void AShopKeepsItsStockApartFromRewardsTakenThere()
    {
        var groups = ApCardRewardGroups.SplitShop(new[] { true, false, false, true, false, false, false, false });

        Assert.Equal(new[] { new ApCardRewardGroup(0, 3), new ApCardRewardGroup(3, 3) }, groups);
    }

    [Fact]
    public void AShopRewardEndsAtTheNextTakenCard()
    {
        var groups = ApCardRewardGroups.SplitShop(new[] { true, true, false, false });

        Assert.Equal(new[] { new ApCardRewardGroup(0, 1), new ApCardRewardGroup(1, 3) }, groups);
    }

    [Fact]
    public void NoChoicesMeanNoRewards()
    {
        Assert.Empty(ApCardRewardGroups.Split(Array.Empty<bool>()));
    }

    [Fact]
    public void OnlyClaimedCardRewardItemsCount()
    {
        var log = new ApRunGraphLog();
        log.Events.Add(Claim(3, "Defect Card Reward"));
        log.Events.Add(Claim(3, "Rare Card Reward"));
        log.Events.Add(Claim(3, "Additional Card Reward"));
        log.Events.Add(Claim(4, "Defect Relic"));
        log.Events.Add(Claim(5, "Silent Card Reward", abandoned: true));
        log.Events.Add(new ApRunGraphEvent { Kind = ApRunGraphEventKind.CheckSent, Floor = 6, ItemName = "Ironclad Card Reward" });

        var byFloor = ApCardRewardGroups.ApRewardsByFloor(log, 10);

        Assert.Equal(new Dictionary<int, int> { [2] = 2 }, byFloor);
    }

    private static ApRunGraphEvent Claim(int floor, string item, bool abandoned = false) => new()
    {
        Kind = ApRunGraphEventKind.ItemClaimed,
        Floor = floor,
        ItemName = item,
        Abandoned = abandoned,
    };
}
