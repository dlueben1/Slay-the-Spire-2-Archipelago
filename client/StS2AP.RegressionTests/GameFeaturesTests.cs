using Newtonsoft.Json.Linq;
using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class GameFeaturesTests
{
    private static readonly IReadOnlySet<string> NoFeatures = new HashSet<string>();
    private static readonly IReadOnlySet<string> WithDread = new HashSet<string> { GameFeatures.Dread };

    [Fact]
    public void SlotsFromOlderApWorldsRequireNothing()
    {
        var slot = new Dictionary<string, object> { ["characters"] = new JArray() };
        Assert.Empty(ApSlotData.ReadRequiredGameFeatures(slot));
    }

    [Fact]
    public void ADreadSlotLacksItsFeatureOnlyOnABuildWithoutDread()
    {
        var slot = new Dictionary<string, object> { ["required_game_features"] = JArray.Parse("[\"dread\"]") };
        IReadOnlyList<string> required = ApSlotData.ReadRequiredGameFeatures(slot);
        Assert.Equal(["dread"], required);
        Assert.Empty(GameFeatures.FindMissing(required, WithDread));
        Assert.Equal(["dread"], GameFeatures.FindMissing(required, NoFeatures));
        // The player reads what the slot plays without, not the feature's internal name.
        Assert.Contains("Dread Mode (runs are standard runs)", GameFeatures.DescribeMissing(["dread"]));
        Assert.Contains("future_feature", GameFeatures.DescribeMissing(["future_feature"]));
    }

    [Fact]
    public void AFeatureFromANewerApWorldIsReportedAsMissing()
    {
        // An APWorld newer than the client can name a feature no build of this client knows.
        var slot = new Dictionary<string, object>
        {
            ["required_game_features"] = JArray.Parse("[\"dread\",\"future\",\"future\"]"),
        };
        Assert.Equal(
            ["future"],
            GameFeatures.FindMissing(ApSlotData.ReadRequiredGameFeatures(slot), WithDread)
        );
    }
}
