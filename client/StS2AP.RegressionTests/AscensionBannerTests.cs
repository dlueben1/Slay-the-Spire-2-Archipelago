using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class AscensionBannerTests
{
    [Fact]
    public void BannerListsActiveLevelsAndPreservesGaps()
    {
        Assert.Equal("A0", AscensionBanner.FormatLevels([]));
        Assert.Equal("A1", AscensionBanner.FormatLevels([1]));
        Assert.Equal("A10", AscensionBanner.FormatLevels([10]));
        Assert.Equal("A1-A10", AscensionBanner.FormatLevels(Enumerable.Range(1, 10)));
        Assert.Equal("A1-A8 + A10", AscensionBanner.FormatLevels(
            Enumerable.Range(1, 10).Except([9])));
        Assert.Equal("A1-A3 + A5 + A7-A10", AscensionBanner.FormatLevels(
            [1, 2, 3, 5, 7, 8, 9, 10]));
        Assert.Equal("A1-A2 + A10", AscensionBanner.FormatLevels([10, 2, 1, 2]));
    }
}
