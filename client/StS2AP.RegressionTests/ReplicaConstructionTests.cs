using System.Text.Json;
using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ReplicaConstructionTests
{
    [Fact]
    public void LiveOwnerUpdatesDoNotAdvanceAnotherReplicasConstruction()
    {
        var slowReplica = new ApReplicaConstructionState();
        var checkpointActs = new HashSet<int> { 1 };
        Assert.True(slowReplica.EnsureInitialized(18, 4, 7, 6, checkpointActs));
        checkpointActs.Add(3);
        Assert.True(slowReplica.MultiplayerBossCompensatedActs.SetEquals([1]));

        // The owner reached Act 2 first. Its update must not advance this replica.
        Assert.False(slowReplica.EnsureInitialized(19, 5, 8, 7, [1, 2]));
        Assert.Equal((18, 4, 7, 6), (
            slowReplica.CardRewardsAttempted, slowReplica.RareCardRewardsAttempted,
            slowReplica.GoldRewardsAttempted, slowReplica.PotionRewardsAttempted));
        Assert.True(slowReplica.MultiplayerBossCompensatedActs.SetEquals([1]));

        // This replica catches up by performing its own construction once.
        Assert.True(slowReplica.TryMarkBossCompensation(2));
        Assert.False(slowReplica.TryMarkBossCompensation(2));
        Assert.Equal(19, slowReplica.IncrementCardRewards());
        Assert.Equal(5, slowReplica.IncrementRareCardRewards());
        Assert.Equal(8, slowReplica.IncrementGoldRewards());
        Assert.Equal(7, slowReplica.IncrementPotionRewards());
        Assert.True(slowReplica.MultiplayerBossCompensatedActs.SetEquals([1, 2]));

        var rejoinedReplica = new ApReplicaConstructionState();
        Assert.True(rejoinedReplica.EnsureInitialized(19, 5, 8, 7, [1, 2]));
        Assert.Equal(19, rejoinedReplica.CardRewardsAttempted);
        Assert.True(rejoinedReplica.MultiplayerBossCompensatedActs.SetEquals([1, 2]));

        var restored = JsonSerializer.Deserialize<ApReplicaConstructionState>(
            JsonSerializer.Serialize(rejoinedReplica));
        Assert.NotNull(restored);
        Assert.True(restored.Initialized);
        Assert.Equal(19, restored.CardRewardsAttempted);
        Assert.True(restored.MultiplayerBossCompensatedActs.SetEquals([1, 2]));
        Assert.False(restored.EnsureInitialized(20, 6, 9, 8, [1, 2, 3]));
    }
}
