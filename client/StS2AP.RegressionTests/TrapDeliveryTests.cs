using System.Text.Json;
using StS2AP.Data;
using StS2AP.Persistence;
using StS2AP.Utils;
using Xunit;
using static StS2AP.Data.ItemTable;

namespace StS2AP.RegressionTests;

public sealed class TrapDeliveryTests
{
    [Fact]
    public void InterleavedReceiptsKeepIndependentCutoffsAcrossReconnect()
    {
        var buffs = new CombatEffectReceiptQueue();
        var traps = new CombatEffectReceiptQueue(traps: true);
        buffs.Enqueue(APItem.Strength, 2, false);
        traps.Enqueue(APItem.WeakTrap, 4, false);
        buffs.Enqueue(APItem.Buffer, 6, false);
        traps.Enqueue(APItem.DazedTrap, 8, false);
        traps.RestoreConsumedIndex(4);
        Assert.True(buffs.TryPeek(out var buff));
        Assert.Equal(2, buff.ItemIndex);
        buffs.RestoreConsumedIndex(6);
        Assert.True(traps.TryPeek(out var trap));
        Assert.Equal(8, trap.ItemIndex);

        var reconnected = new CombatEffectReceiptQueue(traps.LastConsumedIndex, traps: true);
        Assert.False(reconnected.Enqueue(APItem.WeakTrap, 4, false));
        Assert.True(reconnected.Enqueue(APItem.DazedTrap, 8, false));
        reconnected.RestoreConsumedIndex(-1); // A late storage response never rewinds history.
        Assert.Equal(4, reconnected.LastConsumedIndex);
        Assert.True(reconnected.TryPeek(out _));
    }

    [Fact]
    public void CategoriesRejectEachOthersReceiptsAndKeepUniversalIds()
    {
        var buffs = new CombatEffectReceiptQueue();
        var traps = new CombatEffectReceiptQueue(traps: true);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffs.Enqueue(APItem.WeakTrap, 1, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => traps.Enqueue(APItem.Strength, 1, false));
        var types = new[] { APItem.WeakTrap, APItem.FrailTrap, APItem.VulnerableTrap, APItem.NoDrawTrap,
            APItem.TangledTrap, APItem.VakuuTrap, APItem.ConfusedTrap, APItem.SlothTrap, APItem.DazedTrap };
        Assert.Equal(Enumerable.Range(700, 9), types.Select(t => (int)t));
        foreach (var type in types)
        for (int player = 1; player <= 4; player++)
        {
            var id = ArchipelagoIdCodec.ForPlayer((long)type, player);
            Assert.True(ArchipelagoIdCodec.IsUniversalItemId(id));
            Assert.True(IsUniversalTrap(id));
            Assert.False(IsUniversalCombatBuff(id));
            Assert.False(IsMultiplayerBuffGoldFallback(id));
        }
    }

    [Fact]
    public void TrapBacklogAdvancesEachTurnAndRetainsAllowanceAcrossReconnect()
    {
        var queue = new CombatEffectReceiptQueue(traps: true);
        queue.Enqueue(APItem.WeakTrap, 1, false);
        queue.Enqueue(APItem.FrailTrap, 2, false);
        var limit = new CombatEffectLimit();
        var firstTurn = new CombatEffectKey(0, 1, 0, Turn: 1);

        Assert.True(limit.TryBegin(firstTurn));
        Assert.False(limit.TryBegin(firstTurn));
        limit.Cancel(); // An unsuccessful application can retry without spending the trap.
        Assert.True(limit.TryBegin(firstTurn));
        Assert.True(queue.TryPeek(out var first));
        limit.Complete(firstTurn);
        queue.RestoreConsumedIndex(first.ItemIndex);

        var restored = JsonSerializer.Deserialize<CombatEffectLimit>(JsonSerializer.Serialize(limit))!;
        Assert.False(restored.TryBegin(firstTurn));
        var secondTurn = firstTurn with { Turn = 2 };
        Assert.True(restored.TryBegin(secondTurn));
        Assert.True(queue.TryPeek(out var second));
        Assert.Equal(APItem.FrailTrap, second.EffectType);
        restored.Complete(secondTurn);
        queue.RestoreConsumedIndex(second.ItemIndex);
        Assert.False(restored.CanConsume(secondTurn));
        Assert.False(queue.TryPeek(out _));
        Assert.True(restored.CanConsume(firstTurn with { Floor = 2 }));
    }

    [Fact]
    public void SerializedActionsRejectStaleWrongCategoryAndReplayedReceipts()
    {
        var run = Guid.NewGuid();
        var combat = new CombatEffectKey(0, 1, 0, Turn: 2);
        var message = new CombatEffectActionMessage(run, 4, APItem.DazedTrap, combat);
        foreach (var replica in new[] { message, JsonSerializer.Deserialize<CombatEffectActionMessage>(
                     JsonSerializer.Serialize(message))! })
        {
            Assert.True(replica.Matches(run, combat, -1, trap: true));
            Assert.False(replica.Matches(Guid.NewGuid(), combat, -1, trap: true));
            Assert.False(replica.Matches(run, new CombatEffectKey(0, 2, 0), -1, trap: true));
            Assert.False(replica.Matches(run, combat with { Turn = 1 }, -1, trap: true));
            Assert.False(replica.Matches(run, combat with { Turn = 3 }, -1, trap: true));
            Assert.False(replica.Matches(run, null, -1, trap: true));
            Assert.False(replica.Matches(run, combat, 4, trap: true));
            Assert.False(replica.Matches(run, combat, -1, trap: false));
            Assert.False((replica with { ItemIndex = 0 }).Matches(run, combat, -1, trap: true));
            Assert.False((replica with { Combat = null }).Matches(run, combat, -1, trap: true));
            Assert.False((replica with { EffectType = APItem.BonusWaxRelic }).Matches(run, combat, -1, trap: true));
        }
        var invalidTurn = combat with { Turn = 0 };
        Assert.False((message with { Combat = invalidTurn }).Matches(run, invalidTurn, -1, trap: true));
        var buff = message with { Combat = invalidTurn, EffectType = APItem.Strength };
        Assert.True(buff.Matches(run, invalidTurn, -1, trap: false));
        Assert.False((buff with { Combat = combat }).Matches(run, combat, -1, trap: false));
    }
}
