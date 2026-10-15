using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class UniversalBuffGoldTests
{
    [Fact(DisplayName = "Fifty buffs per configured character yield 250 gold each")]
    public void FiftyBuffsPerConfiguredCharacterYield250GoldEach()
    {
        foreach (int characterCount in new[] { 1, 2, 3, 5, 8 })
        {
            long[] offsets = Offsets(characterCount);
            var bank = new Dictionary<long, int>();
            UniversalBuffGold.AddToBank(bank, offsets, 0, 50 * characterCount);
            Assert.Equal(characterCount, bank.Count);
            Assert.All(bank.Values, gold => Assert.Equal(250, gold));
        }
    }

    [Fact(DisplayName = "Fractional buff shares carry forward instead of rounding each receipt")]
    public void FractionalBuffSharesCarryForwardInsteadOfRoundingEachReceipt()
    {
        foreach (var (characterCount, expectedDeltas) in new[]
        {
            (2, new[] { 2, 3 }),
            (3, new[] { 1, 2, 2 }),
            (6, new[] { 0, 1, 1, 1, 1, 1 }),
        })
        {
            var bank = new Dictionary<long, int>();
            for (int i = 0; i < expectedDeltas.Length; i++)
                Assert.Equal(expectedDeltas[i], UniversalBuffGold.AddToBank(bank, Offsets(characterCount), i, 1));
            Assert.All(bank.Values, gold => Assert.Equal(5, gold));
        }
    }

    [Fact(DisplayName = "Live buff receipts match a history rebuild after every receipt")]
    public void LiveBuffReceiptsMatchAHistoryRebuildAfterEveryReceipt()
    {
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

    [Fact(DisplayName = "The first receipt after a rebuild preserves the previous fractional share")]
    public void TheFirstReceiptAfterARebuildPreservesThePreviousFractionalShare()
    {
        long[] offsets = Offsets(2);
        var rebuilt = new Dictionary<long, int>();
        UniversalBuffGold.AddToBank(rebuilt, offsets, 0, 101);
        Assert.All(rebuilt.Values, gold => Assert.Equal(252, gold));
        Assert.Equal(3, UniversalBuffGold.AddToBank(rebuilt, offsets, 101, 1));
        Assert.All(rebuilt.Values, gold => Assert.Equal(255, gold));

        // A different slot starts from its own history, without the old remainder.
        var nextSlot = new Dictionary<long, int>();
        Assert.Equal(2, UniversalBuffGold.AddToBank(nextSlot, offsets, 0, 1));
    }

    [Fact(DisplayName = "Buff division leaves ordinary character gold untouched")]
    public void BuffDivisionLeavesOrdinaryCharacterGoldUntouched()
    {
        long[] offsets = Offsets(3);
        var bank = new Dictionary<long, int> { [offsets[0]] = 37, [offsets[2]] = 11, [999999] = 23 };
        UniversalBuffGold.AddToBank(bank, offsets, 0, 1);
        bank[offsets[1]] += 5; // An ordinary character-specific gold receipt.
        UniversalBuffGold.AddToBank(bank, offsets, 1, 2);
        Assert.Equal(42, bank[offsets[0]]);
        Assert.Equal(10, bank[offsets[1]]);
        Assert.Equal(16, bank[offsets[2]]);
        Assert.Equal(23, bank[999999]);
    }

    [Fact(DisplayName = "Configured offsets are counted once, regardless of order or gaps")]
    public void ConfiguredOffsetsAreCountedOnceRegardlessOfOrderOrGaps()
    {
        var bank = new Dictionary<long, int>();
        UniversalBuffGold.AddToBank(bank, new long[] { 90000, 10000, 90000 }, 0, 2);
        Assert.Equal(2, bank.Count);
        Assert.Equal(5, bank[10000]);
        Assert.Equal(5, bank[90000]);
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
