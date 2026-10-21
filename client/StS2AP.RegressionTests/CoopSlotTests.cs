using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class CoopSlotTests
{
    [Fact]
    public void DifferentNumberedPlayersHaveDifferentPersistentIdentities()
    {
        var first = ApSessionIdentity.Create("localhost:38281", "seed", 0, 1);
        var second = ApSessionIdentity.Create("localhost:38281", "seed", 0, 1, 2);
        Assert.NotEqual(first, second);
        Assert.NotEqual(first.GetFileKey(), second.GetFileKey());
        Assert.Equal(second, ApSessionIdentity.Create("localhost:38281", "seed", 0, 1, 2));
        Assert.Equal(second, System.Text.Json.JsonSerializer.Deserialize<ApSessionIdentity>(
            System.Text.Json.JsonSerializer.Serialize(second)));
    }

    [Fact]
    public void SlotGoalRequiresEachPlayerToMeetTheirOwnQuota()
    {
        string[] roster = ["IRONCLAD", "SILENT"];
        string[] allFirstPlayer = ["IRONCLAD", "SILENT"];
        Assert.False(CoopGoalPolicy.IsComplete(allFirstPlayer, roster, 2, 1));
        Assert.True(CoopGoalPolicy.IsComplete(["IRONCLAD", "P2 SILENT"], roster, 2, 1));
        Assert.False(CoopGoalPolicy.IsComplete(["IRONCLAD", "P2 SILENT"], roster, 2, 0));
        Assert.True(CoopGoalPolicy.IsComplete(["IRONCLAD", "SILENT", "P2 IRONCLAD", "P2 SILENT"], roster, 2, 0));
        Assert.False(CoopGoalPolicy.IsComplete(["IRONCLAD", "P2 UNKNOWN"], roster, 2, 1));
    }
}
