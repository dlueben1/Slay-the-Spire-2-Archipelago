using System.Text.Json;
using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class RelicReceiptTests
{
    private const string Chest = "chest:0:7";
    private const string Elite = "elite:0:9";

    [Fact(DisplayName = "A chest reservation wins over a later menu request")]
    public void ChestReservationWinsOverALaterMenuRequest()
    {
        var state = new ApRelicReceiptState();
        Assert.True(state.TryReserve(11, 100, Chest, rewardNumber: 1));
        Assert.False(state.TryReserve(11, 100, ApRelicReceiptState.MenuDestination));
        Assert.False(state.CanUseMenu(11, 100));
    }

    [Fact(DisplayName = "A menu reservation keeps its receipt from a natural relic reward")]
    public void MenuReservationKeepsItsReceiptFromANaturalReward()
    {
        var state = new ApRelicReceiptState();
        Assert.True(state.TryReserve(11, 100, ApRelicReceiptState.MenuDestination));
        Assert.False(state.TryReserve(11, 100, Elite, rewardNumber: 3));
        Assert.True(state.CanUseMenu(11, 100));
    }

    [Fact(DisplayName = "Players sharing a receipt index have independent reservations")]
    public void PlayersSharingAReceiptIndexHaveIndependentReservations()
    {
        var state = new ApRelicReceiptState();
        state.TryReserve(11, 100, Chest, rewardNumber: 1);
        state.Consume(11, 100, Chest);
        Assert.True(state.TryReserve(22, 100, ApRelicReceiptState.MenuDestination));
        Assert.True(state.Find(11, 100)!.Consumed && !state.Find(22, 100)!.Consumed);
    }

    [Fact(DisplayName = "A chest keeps its native relic only when ungated or paired with a receipt")]
    public void ChestCandidateMask()
    {
        var decision = new ApRelicReceiptState.ChestDecision
        {
            RoomKey = Chest,
            Candidates =
            {
                new() { PlayerNetId = 11, GeneratesRelic = true, ApGated = true, ReceiptIndex = 100, RewardNumber = 1 },
                new() { PlayerNetId = 22, GeneratesRelic = true, ApGated = true, RewardNumber = 1 },
                new() { PlayerNetId = 33, GeneratesRelic = true, ApGated = false },
                new() { PlayerNetId = 44, GeneratesRelic = false, ApGated = false },
            },
        };
        Assert.Equal(new[] { true, false, true, false }, decision.Candidates.Select(c => c.Keep));
    }

    [Fact(DisplayName = "A paired natural relic reward spends its own bank; an unpaired one keeps it")]
    public void NaturalRewardsAndBanks()
    {
        var state = new ApRelicReceiptState();
        // Two chests: the first paired receipt 100, the second had no receipt waiting.
        state.TryReserve(11, 100, Chest, rewardNumber: 1);
        state.Consume(11, 100, Chest);
        Assert.Equal(1, state.AvailableBanks(11, relicRewardsAttempted: 2, maxRelicRewards: 10));
    }

    [Fact(DisplayName = "Only a menu receipt past the anytime relics spends a bank")]
    public void MenuReceiptsAndBanks()
    {
        var state = new ApRelicReceiptState();
        state.TryReserve(11, 100, ApRelicReceiptState.MenuDestination);
        state.TryReserve(11, 101, ApRelicReceiptState.MenuDestination, requiresBank: true);
        Assert.Equal(1, state.AvailableBanks(11, relicRewardsAttempted: 2, maxRelicRewards: 10));
        // Claiming it changes nothing further.
        state.Consume(11, 101, ApRelicReceiptState.MenuDestination);
        Assert.Equal(1, state.AvailableBanks(11, relicRewardsAttempted: 2, maxRelicRewards: 10));
    }

    [Fact(DisplayName = "Natural relic rewards past the tenth earn no bank")]
    public void BanksStopAtTheTenthNaturalReward() =>
        Assert.Equal(10, new ApRelicReceiptState().AvailableBanks(11, relicRewardsAttempted: 14, maxRelicRewards: 10));

    [Fact(DisplayName = "An Elite reservation spends nothing until it settles, and an unused one is released")]
    public void EliteReservationsSettle()
    {
        var state = new ApRelicReceiptState();
        state.TryReserve(11, 100, Elite, rewardNumber: 3);
        state.TryReserve(11, 101, Elite, rewardNumber: 4);
        // The combat ended with two natural rewards so far and two reserved receipts.
        Assert.Equal(2, state.AvailableBanks(11, relicRewardsAttempted: 2, maxRelicRewards: 10));
        Assert.False(state.TryReserve(11, 101, ApRelicReceiptState.MenuDestination));

        // The rewards screen built one relic reward, which kept receipt 100.
        state.Consume(11, 100, Elite);
        state.Release(11, 101);
        Assert.Equal(2, state.AvailableBanks(11, relicRewardsAttempted: 3, maxRelicRewards: 10));
        Assert.Null(state.Find(11, 101));
        Assert.True(state.TryReserve(11, 101, ApRelicReceiptState.MenuDestination, requiresBank: true));
    }

    [Fact(DisplayName = "Release leaves menu reservations and settled receipts alone")]
    public void ReleaseOnlyFreesUnsettledNaturalReservations()
    {
        var state = new ApRelicReceiptState();
        state.TryReserve(11, 100, ApRelicReceiptState.MenuDestination);
        state.TryReserve(11, 101, Chest, rewardNumber: 1);
        state.Consume(11, 101, Chest);
        state.Release(11, 100);
        state.Release(11, 101);
        Assert.NotNull(state.Find(11, 100));
        Assert.True(state.Find(11, 101)!.Consumed);
    }

    [Fact(DisplayName = "A save round trip keeps reservations, assignments, chests and unsettled Elites")]
    public void SaveRoundTrip()
    {
        var state = new ApRelicReceiptState();
        state.TryReserve(22, 101, ApRelicReceiptState.MenuDestination, requiresBank: true);
        state.AssignMenu(22, 101, "exact saved relic JSON");
        state.TryReserve(11, 100, Chest, rewardNumber: 1);
        state.Consume(11, 100, Chest);
        state.Chests[Chest] = new()
        {
            RoomKey = Chest,
            Candidates = { new() { PlayerNetId = 11, GeneratesRelic = true, ApGated = true, ReceiptIndex = 100, RewardNumber = 1 } },
        };
        state.Elites[Elite] = new()
        {
            RoomKey = Elite,
            Players = { new() { PlayerNetId = 11, BaseAttempted = 1, Reserved = { 102 }, Sources = { 102, null } } },
        };

        var restored = JsonSerializer.Deserialize<ApRelicReceiptState>(JsonSerializer.Serialize(state))!;
        Assert.True(restored.Find(11, 100)!.Consumed);
        Assert.Equal("exact saved relic JSON", restored.Find(22, 101)!.MenuRelicAssignment);
        Assert.True(restored.Chests[Chest].Candidates.Single().Keep);
        Assert.Equal(new int?[] { 102, null }, restored.Elites[Elite].Players.Single().Sources);
    }

    [Fact(DisplayName = "An assignment cannot be rerolled")]
    public void AssignmentRerollsAreRejected()
    {
        var state = new ApRelicReceiptState();
        state.TryReserve(22, 101, ApRelicReceiptState.MenuDestination);
        state.AssignMenu(22, 101, "first");
        Assert.Throws<InvalidOperationException>(() => state.AssignMenu(22, 101, "reroll"));
        Assert.Equal("first", state.Find(22, 101)!.MenuRelicAssignment);
    }
}
