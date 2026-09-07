using System.Text.Json;
using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class RelicReceiptTests
{
    [Fact(DisplayName = "Host approves only known receipts with funding and caps fresh bank reservations")]
    public void HostApprovesOnlyKnownReceiptsWithFundingAndCapsFreshBankReservations()
    {
        var state = new ApRelicReceiptState();
        var progress = new ApRunProgressState { RelicRewardsAttempted = 1, RelicRewardsAvailableAnytimeForRun = 1 };
        var result = state.ApproveMenu(11, new[] { 99, 100, 101, 102, 101 }, new[] { 100, 101, 102 }, progress);
        Assert.True(result.SequenceEqual(new[] { 100, 101 }));
        Assert.True(!state.Find(11, 100)!.RequiresBank && state.Find(11, 101)!.RequiresBank);
        Assert.True(state.Find(11, 99) == null && state.Find(11, 102) == null);
    }

    [Fact(DisplayName = "An approval retry reuses its reservation without spending another bank")]
    public void AnApprovalRetryReusesItsReservationWithoutSpendingAnotherBank()
    {
        var state = new ApRelicReceiptState();
        var progress = new ApRunProgressState { RelicRewardsAttempted = 1 };
        state.ApproveMenu(11, new[] { 100 }, new[] { 100, 101 }, progress);
        var retry = state.ApproveMenu(11, new[] { 100, 101 }, new[] { 100, 101 }, progress);
        Assert.True(retry.SequenceEqual(new[] { 100 }));
        state.AssignMenu(11, 100, "stable choice");
        state.Consume(11, 100, ApRelicReceiptState.MenuDestination);
        var stale = new ApRunProgressState { RelicRewardsAttempted = 1, BankedRelicRewards = 1 };
        Assert.True(state.ApproveMenu(11, new[] { 100, 101 }, new[] { 100, 101 }, stale).Count == 0);
    }

    [Fact(DisplayName = "A receipt missing from the host catalog can be approved after it arrives")]
    public void AReceiptMissingFromTheHostCatalogCanBeApprovedAfterItArrives()
    {
        var state = new ApRelicReceiptState();
        var progress = new ApRunProgressState { RelicRewardsAttempted = 1 };
        Assert.True(state.ApproveMenu(11, new[] { 100 }, Array.Empty<int>(), progress).Count == 0);
        Assert.True(progress.UsedItems.Count == 0 && progress.BankedRelicRewards == 1);
        Assert.True(state.ApproveMenu(11, new[] { 100 }, new[] { 100 }, progress).SequenceEqual(new[] { 100 }));
    }

    [Fact(DisplayName = "Chest reservation wins over a later menu request")]
    public void ChestReservationWinsOverALaterMenuRequest()
    {
        var state = NewChest();
        Assert.False(state.TryReserve(11, 100, ApRelicReceiptState.MenuDestination));
        Assert.False(state.CanUseMenu(11, 100));
        Assert.True(state.Find(11, 100) is { Consumed: false });
    }

    [Fact(DisplayName = "Menu reservation wins before a chest is frozen")]
    public void MenuReservationWinsBeforeAChestIsFrozen()
    {
        var state = new ApRelicReceiptState();
        Assert.True(state.TryReserve(11, 100, ApRelicReceiptState.MenuDestination));
        Assert.Throws<InvalidOperationException>(() => state.AddChest(Decision()));
        Assert.True(state.Chests.Count == 0 && state.CanUseMenu(11, 100));
    }

    [Fact(DisplayName = "Direct AP players sharing a receipt index have independent spending")]
    public void DirectAPPlayersSharingAReceiptIndexHaveIndependentSpending()
    {
        var state = NewChest();
        Assert.True(state.TryReserve(22, 100, ApRelicReceiptState.MenuDestination));
        state.Consume(22, 100, ApRelicReceiptState.MenuDestination);
        Assert.True(!state.Find(11, 100)!.Consumed && state.Find(22, 100)!.Consumed);
    }

    [Fact(DisplayName = "Delayed duplicate decisions preserve already-consumed receipts")]
    public void DelayedDuplicateDecisionsPreserveAlreadyConsumedReceipts()
    {
        var state = NewChest();
        state.Consume(11, 100, "chest:0:7");
        state.AddChest(Decision());
        Assert.True(state.Find(11, 100)!.Consumed);
        Assert.False(state.TryReserve(11, 100, "chest:1:7"));
    }

    [Fact(DisplayName = "Empty and vanilla candidate masks remain unchanged after new receipts")]
    public void EmptyAndVanillaCandidateMasksRemainUnchangedAfterNewReceipts()
    {
        var state = NewChest();
        var frozen = state.Chests["chest:0:7"];
        Assert.True(frozen.Candidates.Select(c => c.Keep).SequenceEqual(new[] { true, false, true, false }));
        state.TryReserve(22, 101, ApRelicReceiptState.MenuDestination, requiresBank: true);
        Assert.True(frozen.Candidates[1].ReceiptIndex == null && !frozen.Candidates[1].Keep);
    }

    [Fact(DisplayName = "Save round trip preserves candidate identity, reservations and exact assignments")]
    public void SaveRoundTripPreservesCandidateIdentityReservationsAndExactAssignments()
    {
        var state = NewChest();
        state.TryReserve(22, 101, ApRelicReceiptState.MenuDestination, requiresBank: true);
        state.AssignMenu(22, 101, "exact saved relic JSON");
        state.Consume(11, 100, "chest:0:7");
        var restored = JsonSerializer.Deserialize<ApRelicReceiptState>(JsonSerializer.Serialize(state))!;
        Assert.True(restored.Find(11, 100)!.Consumed);
        Assert.True(restored.Find(22, 101)!.MenuRelicAssignment == "exact saved relic JSON");
        Assert.True(restored.Chests["chest:0:7"].NativeRelicIds!.SequenceEqual(new[] { "A", "B", "C" }));
        Assert.True(restored.Chests["chest:0:7"].Candidates.Select(c => c.Keep).SequenceEqual(new[] { true, false, true, false }));
    }

    [Fact(DisplayName = "A stale progress snapshot cannot unspend a chest receipt or create an extra bank")]
    public void AStaleProgressSnapshotCannotUnspendAChestReceiptOrCreateAnExtraBank()
    {
        var state = NewChest();
        state.Chests["chest:0:7"].SettledPlayers.Add(11);
        state.Consume(11, 100, "chest:0:7");
        var progress = new ApRunProgressState();
        state.ReconcileProgress(11, progress, new[] { 100 });
        state.ReconcileProgress(11, progress, new[] { 100 });
        Assert.True(progress.RelicRewardsAttempted == 1 && progress.BankedRelicRewards == 0);
        Assert.True(progress.UsedItems.SequenceEqual(new[] { 100 }));
    }

    [Fact(DisplayName = "An unfunded chest preserves its bank for a late receipt")]
    public void AnUnfundedChestPreservesItsBankForALateReceipt()
    {
        var state = NewChest();
        state.Chests["chest:0:7"].SettledPlayers.Add(22);
        var progress = new ApRunProgressState();
        state.ReconcileProgress(22, progress, Array.Empty<int>());
        Assert.True(progress.BankedRelicRewards == 1 && progress.UsedItems.Count == 0);
        state.TryReserve(22, 101, ApRelicReceiptState.MenuDestination, requiresBank: true);
        state.AssignMenu(22, 101, "late relic");
        state.ReconcileProgress(22, progress, new[] { 101 });
        Assert.True(progress.BankedRelicRewards == 0 && progress.RelicChoiceAssignments[101].Single() == "late relic");
    }

    [Fact(DisplayName = "Consumed menu assignments survive older snapshots without regranting or banking twice")]
    public void ConsumedMenuAssignmentsSurviveOlderSnapshotsWithoutRegrantingOrBankingTwice()
    {
        var state = new ApRelicReceiptState();
        state.TryReserve(11, 100, ApRelicReceiptState.MenuDestination, requiresBank: true);
        state.AssignMenu(11, 100, "stable choice");
        var progress = new ApRunProgressState { RelicRewardsAttempted = 2, BankedRelicRewards = 2 };
        state.ReconcileProgress(11, progress, new[] { 100 });
        Assert.True(progress.BankedRelicRewards == 1 && progress.RelicChoiceAssignments.ContainsKey(100));
        state.Consume(11, 100, ApRelicReceiptState.MenuDestination);
        state.ReconcileProgress(11, progress, new[] { 100 });
        Assert.True(progress.BankedRelicRewards == 1 && !progress.RelicChoiceAssignments.ContainsKey(100));
        Assert.True(progress.UsedItems.Contains(100) && !state.CanUseMenu(11, 100));
    }

    [Fact(DisplayName = "Anytime relic spending does not consume a natural reward bank")]
    public void AnytimeRelicSpendingDoesNotConsumeANaturalRewardBank()
    {
        var state = new ApRelicReceiptState();
        state.TryReserve(11, 100, ApRelicReceiptState.MenuDestination);
        state.AssignMenu(11, 100, "anytime relic");
        state.Consume(11, 100, ApRelicReceiptState.MenuDestination);
        var progress = new ApRunProgressState { RelicRewardsAttempted = 1, BankedRelicRewards = 1 };
        state.ReconcileProgress(11, progress, new[] { 101 });
        Assert.True(progress.BankedRelicRewards == 1);
    }

    [Fact(DisplayName = "Conflicting replays and assignment rerolls are rejected without replacing the original")]
    public void ConflictingReplaysAndAssignmentRerollsAreRejectedWithoutReplacingTheOriginal()
    {
        var state = NewChest();
        var conflict = Decision();
        conflict.Candidates[0].ReceiptIndex = 101;
        Assert.Throws<InvalidOperationException>(() => state.AddChest(conflict));
        conflict = Decision();
        conflict.NativeRelicIds = new() { "different" };
        Assert.Throws<InvalidOperationException>(() => state.AddChest(conflict));
        state.TryReserve(22, 101, ApRelicReceiptState.MenuDestination);
        state.AssignMenu(22, 101, "first");
        Assert.Throws<InvalidOperationException>(() => state.AssignMenu(22, 101, "reroll"));
        Assert.True(state.Find(22, 101)!.MenuRelicAssignment == "first");
    }

    private static ApRelicReceiptState NewChest()
    {
        var state = new ApRelicReceiptState();
        state.AddChest(Decision());
        return state;
    }

    private static ApRelicReceiptState.ChestDecision Decision() => new()
    {
        RoomKey = "chest:0:7", NativeRelicIds = new() { "A", "B", "C" },
        Candidates = new()
        {
            new() { PlayerNetId = 11, GeneratesRelic = true, ApGated = true, ReceiptIndex = 100, RewardNumber = 1 },
            new() { PlayerNetId = 22, GeneratesRelic = true, ApGated = true, RewardNumber = 1 },
            new() { PlayerNetId = 33, GeneratesRelic = true, ApGated = false },
            new() { PlayerNetId = 44, GeneratesRelic = false, ApGated = false },
        },
    };
}
