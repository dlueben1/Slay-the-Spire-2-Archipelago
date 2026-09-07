using StS2AP.DomainAdapters;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class RewardMaterializationInteropTests
{
    private static ApMirroredRewardSpec Configuration(string strategy, bool replay) => new()
    {
        ApSlotId = 2, ReceivedItemIndex = 42, Kind = ApMirroredRewardKind.Card,
        MaterializationStrategyId = strategy, RequiresNativeMaterialization = replay,
    };

    [Fact]
    public void AdapterReturnsPolicyWithCallableCSharpHandlers()
    {
        var policy = MirroredRewardAdapter.CardConfiguration(Configuration("ap_rng_owner_final_v1", false)).Policy;

        string result = policy.Match(
            () => "owner",
            () => throw new InvalidOperationException("Unexpected restore handler."));

        Assert.Equal("owner", result);
    }

    [Theory]
    [InlineData("unknown", false, "used an unknown materialization strategy.")]
    [InlineData("ap_rng_owner_final_v1", true, "requested removed replica-native generation.")]
    public void AdapterMapsDomainErrorsToExistingExceptions(string strategy, bool replay, string message)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => MirroredRewardAdapter.CardConfiguration(Configuration(strategy, replay)));

        Assert.Equal($"AP reward 2:42 {message}", error.Message);
    }
}
