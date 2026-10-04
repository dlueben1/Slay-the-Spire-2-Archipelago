using StS2AP.Data;
using StS2AP.Utils;
using Xunit;
using static StS2AP.Data.ItemTable;

namespace StS2AP.RegressionTests
{

public sealed class BuffReceiptQueueTests
{
    [Fact]
    public void ReplaysAreOrderedAndSuccessfulConsumptionNeverRewinds()
    {
        var queue = new BuffReceiptQueue();
        Assert.True(queue.Enqueue(APItem.Strength, 12, notificationShown: true));
        Assert.True(queue.Enqueue(APItem.Dexterity, 4, notificationShown: false));
        Assert.False(queue.Enqueue(APItem.Strength, 12, notificationShown: false));

        queue.RestoreConsumedIndex(3);
        Assert.True(queue.TryPeek(out var next));
        Assert.Equal((APItem.Dexterity, 4, false), next);
        // No acknowledgement: a dead target, rejected request, or failed effect retains the head.
        Assert.True(queue.TryPeek(out var stillPending));
        Assert.Equal(next, stillPending);
        queue.RestoreConsumedIndex(4);
        queue.RestoreConsumedIndex(-1); // A late storage read cannot reopen a completed receipt.
        Assert.False(queue.Enqueue(APItem.Dexterity, 4, notificationShown: false));
        Assert.True(queue.TryPeek(out next));
        Assert.Equal((APItem.Strength, 12, true), next);
        queue.RestoreConsumedIndex(12);
        Assert.False(queue.TryPeek(out _));
        Assert.Equal(12, queue.LastConsumedIndex);
    }

    [Fact]
    public void PendingBuffSurvivesNewRunAndReplayButConsumptionIsOwnerSpecific()
    {
        var firstOwner = new BuffReceiptQueue();
        var secondOwner = new BuffReceiptQueue();
        firstOwner.Enqueue(APItem.Buffer, 9, notificationShown: false);
        secondOwner.Enqueue(APItem.Vigor, 9, notificationShown: false);
        // Starting another run does not reset this slot's pending queue.
        Assert.True(firstOwner.TryPeek(out var next));
        Assert.Equal(9, next.ItemIndex);
        firstOwner.RestoreConsumedIndex(9);
        var reconnect = new BuffReceiptQueue(firstOwner.LastConsumedIndex);
        Assert.False(reconnect.Enqueue(APItem.Buffer, 9, notificationShown: false));
        Assert.True(secondOwner.TryPeek(out _));
    }

    [Fact]
    public void InvalidOrConflictingReceiptsCannotChangeConsumption()
    {
        var queue = new BuffReceiptQueue();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            queue.Enqueue(APItem.Strength, 0, notificationShown: false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            queue.Enqueue(APItem.BonusWaxRelic, 1, notificationShown: false));
        queue.Enqueue(APItem.Strength, 1, notificationShown: false);
        Assert.Throws<InvalidDataException>(() =>
            queue.Enqueue(APItem.Buffer, 1, notificationShown: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.RestoreConsumedIndex(-2));
        Assert.Equal(-1, queue.LastConsumedIndex);
        Assert.True(queue.TryPeek(out var next));
        Assert.Equal(APItem.Strength, next.BuffType);
    }

    [Fact]
    public void CombatBuffTrialsDoNotContributeToMultiplayerGold()
    {
        foreach (APItem item in Enum.GetValues<APItem>())
        {
            long id = ArchipelagoIdCodec.ForPlayer((long)item, 2);
            Assert.False(ItemTable.IsMultiplayerBuffGoldFallback(id));
        }
        int converted = Enum.GetValues<APItem>().Count(item =>
            ItemTable.IsUniversalCombatBuff((long)item)
            && ItemTable.IsMultiplayerBuffGoldFallback((long)item));
        var bank = new Dictionary<long, int>();
        UniversalBuffGold.AddToBank(bank, new long[] { 1000 }, 0, converted);
        Assert.Equal(0, bank[1000]);
    }
}
}

namespace StS2AP.Utils
{
    // ItemTable also contains a game-only pickup predicate. These tests exercise its IDs
    // and buff policy; reaching the unrelated game settings path must fail explicitly.
    internal static class AncientSettingsUtility
    {
        public static StS2AP.Models.AncientRewardSettings Current =>
            throw new NotSupportedException("Game settings are unavailable in receipt tests.");
    }
}
