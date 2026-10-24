using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class SlotGoalPolicyTests
{
    private static readonly string[] Roster = ["IRONCLAD", "SILENT"];

    [Fact]
    public void SlotGoalCountsOnlyRosterCharactersAgainstTheQuota()
    {
        Assert.True(SlotGoalPolicy.IsComplete(["IRONCLAD"], Roster, 1));
        Assert.False(SlotGoalPolicy.IsComplete(["IRONCLAD"], Roster, 2));
        Assert.True(SlotGoalPolicy.IsComplete(["ironclad", "Silent"], Roster, 2));
        Assert.False(SlotGoalPolicy.IsComplete(["IRONCLAD", "UNKNOWN"], Roster, 2));
    }

    [Fact]
    public void ZeroQuotaRequiresTheWholeRoster()
    {
        Assert.False(SlotGoalPolicy.IsComplete(["IRONCLAD"], Roster, 0));
        Assert.True(SlotGoalPolicy.IsComplete(["IRONCLAD", "SILENT"], Roster, 0));
    }

    [Fact]
    public void UnreachableQuotaNeverCompletes()
    {
        Assert.False(SlotGoalPolicy.IsComplete(["IRONCLAD", "SILENT"], Roster, 3));
        Assert.False(SlotGoalPolicy.IsComplete([], [], 0));
    }
}
