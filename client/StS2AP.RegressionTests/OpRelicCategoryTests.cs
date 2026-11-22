using Xunit;

namespace StS2AP.RegressionTests;

public sealed class OpRelicCategoryTests
{
    [Fact]
    public void MissingOrEmptySlotDataKeepsEveryRelic()
    {
        Assert.Equal(OpRelicCategories.None, OpRelicCategoryNames.Parse(null));
        Assert.Equal(OpRelicCategories.None, OpRelicCategoryNames.Parse([]));
    }

    [Fact]
    public void SlotDataCategoryNamesCombine()
    {
        // The APWorld sends a sorted list of its case-sensitive option keys.
        Assert.Equal(
            OpRelicCategories.Ancient | OpRelicCategories.Rare,
            OpRelicCategoryNames.Parse(["Ancient", "Rare"])
        );
        Assert.Equal(OpRelicCategories.Rare, OpRelicCategoryNames.Parse(["rare"]));
    }

    [Fact]
    public void UnknownCategoriesAreReportedWithoutDroppingKnownOnes()
    {
        var unknown = new List<string>();
        // A newer APWorld may add categories that this client cannot map to relics yet.
        var categories = OpRelicCategoryNames.Parse(
            ["Ancient", "Shop", "None", null, "3"],
            unknown.Add
        );

        Assert.Equal(OpRelicCategories.Ancient, categories);
        Assert.Equal(["Shop", "None", "<null>", "3"], unknown);
    }
}
