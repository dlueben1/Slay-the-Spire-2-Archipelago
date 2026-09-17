using StS2AP.DomainAdapters;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class RewardMaterializationInteropTests
{
    private static ApMirroredRewardSpec Configuration(string strategy) => new()
    {
        ApSlotId = 2, ReceivedItemIndex = 42, Kind = ApMirroredRewardKind.Card,
        MaterializationStrategyId = strategy,
    };

    [Fact]
    public void AdapterReturnsPolicyWithCallableCSharpHandlers()
    {
        var policy = MirroredRewardAdapter.CardConfiguration(Configuration("ap_rng_replicated_card_v1")).Policy;

        string result = policy.Match(
            () => throw new InvalidOperationException("Unexpected owner handler."),
            () => "replicated");

        Assert.Equal("replicated", result);
    }

    [Theory]
    [InlineData("unknown", "used an unknown materialization strategy.")]
    [InlineData("replica_native_v1", "used an unknown materialization strategy.")]
    [InlineData("ap_rng_owner_final_v1", "had an unsupported card materialization strategy.")]
    public void AdapterMapsDomainErrorsToExistingExceptions(string strategy, string message)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => MirroredRewardAdapter.CardConfiguration(Configuration(strategy)));

        Assert.Equal($"AP reward 2:42 {message}", error.Message);
    }
}
