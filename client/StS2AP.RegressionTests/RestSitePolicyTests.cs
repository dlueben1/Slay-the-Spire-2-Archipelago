using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class RestSitePolicyTests
{
    // Availability is input to AP's policy. These values do not simulate native Smith logic.
    private sealed record Option(string Id, bool Enabled = true);

    private static List<Option> NativeOptions(bool smithEnabled) =>
        [new("HEAL"), new("MEND"), new("SMITH", smithEnabled)];

    private static bool Apply(RestSitePolicy policy, ICollection<Option> options) =>
        policy.ApplyLocks(options, option => option.Id, option => option.Enabled, () => new Option("NOTHING"));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void UnlockedButDisabledSmithAlwaysLeavesASafeExit(int act)
    {
        var options = NativeOptions(smithEnabled: false);
        Assert.True(Apply(new RestSitePolicy(act, 0, 3), options));
        Assert.Equal(new[] { new Option("NOTHING"), new Option("SMITH", false) }, options);
    }

    [Theory]
    [InlineData(0, 1, true, false, "SMITH")]
    [InlineData(1, 1, false, false, "HEAL,MEND,SMITH")]
    [InlineData(0, 0, true, true, "NOTHING")]
    [InlineData(1, 0, false, false, "HEAL,MEND")]
    public void ProgressionLocksPreserveUsableActions(
        int rest, int smith, bool smithEnabled, bool fallback, string expectedIds)
    {
        var options = NativeOptions(smithEnabled);
        Assert.Equal(fallback, Apply(new RestSitePolicy(1, rest, smith), options));
        Assert.Equal(expectedIds.Split(','), options.Select(option => option.Id));
        Assert.Contains(options, option => option.Enabled);
    }

    [Theory]
    [InlineData(1, false, "SMITH,DIG")]
    [InlineData(0, true, "NOTHING,DIG")]
    public void RelicActionsRemainUsableAndBothLockedFallbackIsPreserved(
        int smith, bool fallback, string expectedIds)
    {
        var options = NativeOptions(smithEnabled: false);
        options.Add(new Option("DIG"));
        Assert.Equal(fallback, Apply(new RestSitePolicy(1, 0, smith), options));
        Assert.Equal(expectedIds.Split(','), options.Select(option => option.Id));
        Assert.Contains(new Option("DIG"), options);
    }

    [Fact]
    public void EmptyOrDisabledCollectionsGetAnExit()
    {
        foreach (var options in new ICollection<Option>[]
                 {
                     new List<Option>(),
                     new List<Option> { new("SMITH", false), new("MEND", false) },
                     new HashSet<Option> { new("SMITH", false) },
                 })
        {
            Assert.True(Apply(new RestSitePolicy(1, 1, 1), options));
            Assert.Single(options, option => option.Id == "NOTHING");
            Assert.Contains(options, option => option.Enabled);
        }
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 6)]
    [InlineData(4, 6)]
    public void OnlyReachedActsSupplyChecksInStableOrder(int act, int expectedCount)
    {
        var policy = new RestSitePolicy(act, 3, 3);
        var visited = new List<(int, int)>();
        var checks = policy.GetAvailableChecks(new HashSet<long>(), (a, c) =>
        {
            visited.Add((a, c));
            return SuppliedLocationId(a, c);
        }).ToArray();
        Assert.Equal(expectedCount, checks.Length);
        Assert.Equal(new[] { (1, 1), (1, 2), (2, 1), (2, 2), (3, 1), (3, 2) }.Take(expectedCount), visited);
        Assert.Equal(checks, policy.GetAvailableChecks(new HashSet<long>(), SuppliedLocationId));
    }

    [Fact]
    public void AvailableChecksNeverReplaceTheSafeExitOrConsumeProgress()
    {
        var policy = new RestSitePolicy(2, 0, 2);
        var checkedIds = new HashSet<long> { SuppliedLocationId(1, 1) };
        var options = NativeOptions(smithEnabled: false);
        Assert.True(Apply(policy, options));
        var checks = policy.GetAvailableChecks(checkedIds, SuppliedLocationId).ToArray();
        Assert.Equal("NOTHING", options[0].Id);
        Assert.Equal(3, checks.Length);
        Assert.DoesNotContain(checks, check => checkedIds.Contains(check.LocationId));
        Assert.Equal(new[] { SuppliedLocationId(1, 1) }, checkedIds);
    }

    [Fact]
    public void CollectedChecksStayHiddenWithoutAffectingAnotherPlayer()
    {
        var policy = new RestSitePolicy(3, 3, 3);
        var collected = policy.GetAvailableChecks(new HashSet<long>(), SuppliedLocationId)
            .Select(check => check.LocationId).ToHashSet();
        Assert.Empty(policy.GetAvailableChecks(collected, SuppliedLocationId));
        Assert.Equal(6, policy.GetAvailableChecks(new HashSet<long>(), SuppliedLocationId).Count());

        var blocked = NativeOptions(smithEnabled: false);
        var available = NativeOptions(smithEnabled: true);
        Assert.True(Apply(new RestSitePolicy(1, 0, 1), blocked));
        Assert.False(Apply(new RestSitePolicy(1, 0, 1), available));
    }

    // Deliberately arbitrary: the adapter supplies real AP IDs; the policy must preserve them.
    private static long SuppliedLocationId(int act, int campfire) => (act, campfire) switch
    {
        (1, 1) => 81, (1, 2) => 7, (2, 1) => 902,
        (2, 2) => 43, (3, 1) => 56, (3, 2) => 1004,
        _ => throw new ArgumentOutOfRangeException(nameof(act)),
    };
}
