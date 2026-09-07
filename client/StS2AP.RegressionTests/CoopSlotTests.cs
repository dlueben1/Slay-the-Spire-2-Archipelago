using StS2AP.Data;
using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class CoopSlotTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void PlayerBlocksPreserveUniversalAndCharacterIdentity(int player)
    {
        long buff = ArchipelagoIdCodec.ForPlayer(500, player);
        long relic = ArchipelagoIdCodec.ForPlayer(100003, player);
        Assert.True(ArchipelagoIdCodec.IsUniversalItemId(buff));
        Assert.False(ArchipelagoIdCodec.IsCharacterItemId(buff));
        Assert.Equal(500, ArchipelagoIdCodec.WithoutPlayer(buff));
        Assert.Equal(10, ArchipelagoIdCodec.GetAPCharacterNumberFromItemId(relic));
        Assert.Equal(3, ArchipelagoIdCodec.GetCharacterItemTypeId(relic));
        Assert.Equal(player, ArchipelagoIdCodec.GetPlayerNumber(buff));
        Assert.Equal(player, ArchipelagoIdCodec.GetPlayerNumber(relic));
        Assert.True(ArchipelagoIdCodec.TryComposeLocationId(88, 10, out long location));
        Assert.Equal(90088 + (player - 1) * 1000000L, ArchipelagoIdCodec.ForPlayer(location, player));
        Assert.Equal(88, ArchipelagoIdCodec.GetBaseLocationId(ArchipelagoIdCodec.ForPlayer(location, player)));
    }

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
