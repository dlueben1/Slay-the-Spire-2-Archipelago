using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class BlitzMerchantRollTests
{
    [Fact]
    public void ZeroNeverAndHundredAlwaysGiveABlitzMerchant()
    {
        for (int row = 0; row < 20; row++)
        {
            Assert.False(BlitzMerchantRoll.IsBlitzMerchant(12345UL, 0, 3, row, 0));
            Assert.True(BlitzMerchantRoll.IsBlitzMerchant(12345UL, 0, 3, row, 100));
        }
    }

    [Fact]
    public void TheSameShopAlwaysGetsTheSameAnswer()
    {
        // The game rebuilds a shop on Continue, so the roll must not depend on anything else.
        bool first = BlitzMerchantRoll.IsBlitzMerchant(987654321UL, 1, 4, 9, 50);
        for (int attempt = 0; attempt < 10; attempt++)
            Assert.Equal(first, BlitzMerchantRoll.IsBlitzMerchant(987654321UL, 1, 4, 9, 50));
    }

    [Fact]
    public void TheShareOfBlitzMerchantsFollowsTheChance()
    {
        int blitzMerchants = 0;
        int shops = 0;
        for (ulong seed = 1; seed <= 200; seed++)
            for (int act = 0; act < 3; act++)
                for (int column = 0; column < 7; column++)
                    for (int row = 0; row < 15; row += 5)
                    {
                        shops++;
                        if (BlitzMerchantRoll.IsBlitzMerchant(seed, act, column, row, 30))
                            blitzMerchants++;
                    }

        double share = (double)blitzMerchants / shops;
        Assert.InRange(share, 0.27, 0.33);
    }

    [Fact]
    public void AHigherChanceKeepsEveryBlitzMerchantOfALowerOne()
    {
        for (ulong seed = 1; seed <= 50; seed++)
            for (int row = 0; row < 15; row++)
                if (BlitzMerchantRoll.IsBlitzMerchant(seed, 0, 2, row, 20))
                    Assert.True(BlitzMerchantRoll.IsBlitzMerchant(seed, 0, 2, row, 60));
    }
}
