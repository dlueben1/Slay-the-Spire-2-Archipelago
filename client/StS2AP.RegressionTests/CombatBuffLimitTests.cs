using System.Text.Json;
using StS2AP.Persistence;
using StS2AP.Utils;
using Xunit;
using static StS2AP.Data.ItemTable;

namespace StS2AP.RegressionTests;

public sealed class CombatBuffLimitTests
{
    private static readonly CombatEffectKey FirstCombat = new(0, 1, 0);

    [Fact]
    public void BacklogConsumesOneReceiptPerCombatAndKeepsItsOrder()
    {
        var limit = new CombatEffectLimit();
        var queue = new CombatEffectReceiptQueue();
        queue.Enqueue(APItem.Strength, 1, false);
        queue.Enqueue(APItem.Buffer, 2, false);
        queue.Enqueue(APItem.Dexterity, 3, false);

        Assert.True(limit.TryBegin(FirstCombat));
        Assert.True(queue.TryPeek(out var entry));
        limit.Complete(FirstCombat);
        queue.RestoreConsumedIndex(entry.ItemIndex);
        for (int turn = 0; turn < 5; turn++)
            Assert.False(limit.TryBegin(FirstCombat));
        Assert.True(queue.TryPeek(out entry));
        Assert.Equal(2, entry.ItemIndex);

        var nextCombat = new CombatEffectKey(0, 2, 0);
        Assert.True(limit.TryBegin(nextCombat));
        limit.Complete(nextCombat);
        queue.RestoreConsumedIndex(entry.ItemIndex);
        Assert.True(queue.TryPeek(out entry));
        Assert.Equal(3, entry.ItemIndex);
    }

    [Fact]
    public void UnavailableTargetCanRetryButOverlappingApplicationsCannot()
    {
        var limit = new CombatEffectLimit();
        Assert.True(limit.TryBegin(FirstCombat));
        Assert.False(limit.TryBegin(FirstCombat));
        Assert.False(limit.TryBegin(new CombatEffectKey(0, 2, 0)));
        limit.Cancel();
        Assert.True(limit.TryBegin(FirstCombat));
        limit.Complete(FirstCombat);
        limit.Cancel();
        Assert.False(limit.CanConsume(FirstCombat));
    }

    [Fact]
    public void SavedPlayerStateRetainsAllowanceAcrossRejoin()
    {
        var limit = new CombatEffectLimit();
        Assert.True(limit.TryBegin(FirstCombat));
        limit.Complete(FirstCombat);
        var restored = JsonSerializer.Deserialize<CombatEffectLimit>(JsonSerializer.Serialize(limit))!;
        Assert.False(restored.TryBegin(FirstCombat));
        Assert.True(restored.TryBegin(new CombatEffectKey(0, 2, 0)));
    }

    [Fact]
    public void EveryPeerAgreesAndEachPlayerHasAnIndependentAllowance()
    {
        var host = new[] { new CombatEffectLimit(), new CombatEffectLimit() };
        var guest = new[] { new CombatEffectLimit(), new CombatEffectLimit() };
        foreach (var replica in new[] { host, guest })
        {
            Assert.True(replica[0].TryBegin(FirstCombat));
            replica[0].Complete(FirstCombat);
            Assert.False(replica[0].TryBegin(FirstCombat));
            Assert.True(replica[1].TryBegin(FirstCombat));
            replica[1].Complete(FirstCombat);
        }
        Assert.Equal(JsonSerializer.Serialize(host), JsonSerializer.Serialize(guest));
    }

    [Theory]
    [InlineData(0, 1, 1)] // Another fight nested in the same event/map point.
    [InlineData(0, 2, 0)] // Room IDs restart at the next map point.
    [InlineData(1, 1, 0)] // A new act cannot reuse the previous act's allowance.
    public void DistinctCombatsHaveNewAllowances(int act, int floor, int room)
    {
        var limit = new CombatEffectLimit();
        Assert.True(limit.TryBegin(FirstCombat));
        limit.Complete(FirstCombat);
        Assert.True(limit.TryBegin(new CombatEffectKey(act, floor, room)));
        Assert.True(new CombatEffectLimit().TryBegin(FirstCombat)); // A fresh run.
    }
}
