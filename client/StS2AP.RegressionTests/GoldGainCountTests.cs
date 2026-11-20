using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class GoldGainCountTests
{
    private const long Ironclad = 10000;

    [Fact(DisplayName = "A gold claim counts each gain item it pays out once")]
    public void AGoldClaimCountsEachGainItemItPaysOutOnce()
    {
        // Receipts: Combat Gold (15), One Gold (1), Elite Gold (40), then Combat Gold (15).
        var ends = new Dictionary<long, List<int>>();
        GoldGainCount.RecordItemEnd(ends, Ironclad, 15);
        GoldGainCount.RecordItemEnd(ends, Ironclad, 56);
        GoldGainCount.RecordItemEnd(ends, Ironclad, 71);

        // The first claim takes the first three receipts, the second claim the last one.
        Assert.Equal(2, GoldGainCount.CountWithin(ends[Ironclad], 0, 56));
        Assert.Equal(1, GoldGainCount.CountWithin(ends[Ironclad], 56, 71));
        // A claim of filler alone, such as One Gold received after the last claim, counts none.
        Assert.Equal(0, GoldGainCount.CountWithin(ends[Ironclad], 71, 72));
        // A new run redeems the whole bank again from zero.
        Assert.Equal(3, GoldGainCount.CountWithin(ends[Ironclad], 0, 72));
    }

    [Fact(DisplayName = "A character without gain items counts none")]
    public void ACharacterWithoutGainItemsCountsNone()
    {
        Assert.Equal(0, GoldGainCount.CountWithin(null, 0, 100));
    }
}
