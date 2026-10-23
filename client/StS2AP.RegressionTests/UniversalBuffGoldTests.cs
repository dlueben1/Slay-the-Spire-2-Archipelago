using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class UniversalBuffGoldTests
{
    [Fact(DisplayName = "Buffs split their gold evenly between configured characters")]
    public void BuffsSplitTheirGoldEvenlyBetweenConfiguredCharacters()
    {
        foreach (int characterCount in new[] { 1, 2, 3, 5, 8 })
        {
            long[] offsets = Offsets(characterCount);
            var bank = new Dictionary<long, int>();
            UniversalBuffGold.AddToBank(bank, offsets, 0, 50 * characterCount);
            Assert.Equal(characterCount, bank.Count);
            Assert.All(bank.Values, gold => Assert.Equal(50 * UniversalBuffGold.ValuePerBuff, gold));
        }
    }

    [Fact(DisplayName = "Live buff receipts match a history rebuild after every receipt")]
    public void LiveBuffReceiptsMatchAHistoryRebuildAfterEveryReceipt()
    {
        // Rounding each receipt's share separately would leave live banks behind the rebuild.
        for (int characterCount = 1; characterCount <= 10; characterCount++)
        {
            long[] offsets = Offsets(characterCount);
            var live = new Dictionary<long, int>();
            for (int i = 0; i < 201; i++)
            {
                UniversalBuffGold.AddToBank(live, offsets, i, 1);
                var rebuilt = new Dictionary<long, int>();
                UniversalBuffGold.AddToBank(rebuilt, offsets, 0, i + 1);
                Assert.Equal(rebuilt.OrderBy(p => p.Key), live.OrderBy(p => p.Key));
            }
        }
    }

    [Fact(DisplayName = "Buff division leaves ordinary character gold untouched")]
    public void BuffDivisionLeavesOrdinaryCharacterGoldUntouched()
    {
        long[] offsets = Offsets(3);
        var bank = new Dictionary<long, int> { [offsets[0]] = 37, [offsets[2]] = 11, [999999] = 23 };
        UniversalBuffGold.AddToBank(bank, offsets, 0, 1);
        bank[offsets[1]] += 5; // An ordinary character-specific gold receipt.
        UniversalBuffGold.AddToBank(bank, offsets, 1, 2);
        // Three buffs between three characters give each one buff's worth.
        int share = UniversalBuffGold.ValuePerBuff;
        Assert.Equal(37 + share, bank[offsets[0]]);
        Assert.Equal(5 + share, bank[offsets[1]]);
        Assert.Equal(11 + share, bank[offsets[2]]);
        Assert.Equal(23, bank[999999]);
    }

    [Fact(DisplayName = "Configured offsets are counted once, regardless of order or gaps")]
    public void ConfiguredOffsetsAreCountedOnceRegardlessOfOrderOrGaps()
    {
        var bank = new Dictionary<long, int>();
        UniversalBuffGold.AddToBank(bank, new long[] { 90000, 10000, 90000 }, 0, 2);
        Assert.Equal(2, bank.Count);
        Assert.Equal(UniversalBuffGold.ValuePerBuff, bank[10000]);
        Assert.Equal(UniversalBuffGold.ValuePerBuff, bank[90000]);
    }

    [Fact(DisplayName = "An empty buff history adds no gold")]
    public void AnEmptyBuffHistoryAddsNoGold()
    {
        var bank = new Dictionary<long, int> { [10000] = 17 };
        Assert.Equal(0, UniversalBuffGold.AddToBank(bank, Offsets(1), 0, 0));
        Assert.Equal(17, bank[10000]);
    }

    [Fact(DisplayName = "Missing character configuration cannot silently consume buff gold")]
    public void MissingCharacterConfigurationCannotSilentlyConsumeBuffGold()
    {
        var bank = new Dictionary<long, int>();
        Assert.Throws<ArgumentException>(() => UniversalBuffGold.AddToBank(bank, Array.Empty<long>(), 0, 1));
        Assert.Empty(bank);
    }

    private static long[] Offsets(int count) =>
        Enumerable.Range(1, count).Select(i => (long)i * 10000).ToArray();
}
