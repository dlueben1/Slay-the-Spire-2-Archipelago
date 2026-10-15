using System.Text.Json;
using StS2AP.DomainAdapters;
using StS2AP.Persistence;
using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class BonusRewardStateTests
{
    [Fact]
    public void RandomBonusRelicKeySeparatesMultiplayerSlotsButPreservesSingleplayerContract()
    {
        const string runSeed = "RUN-SEED";
        const int ordinal = 2;
        const string relicId = "RELIC.THE_BOOT";

        string singleplayer = BonusRewardSelectionKey.Create(runSeed, ordinal, relicId);
        string firstPlayer = BonusRewardSelectionKey.Create(runSeed, ordinal, relicId, 0);
        string secondPlayer = BonusRewardSelectionKey.Create(runSeed, ordinal, relicId, 1);

        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    "sts2ap-bonus-relic-v1|RUN-SEED|WAX_RELIC:2|RELIC.THE_BOOT"
                )
            )),
            singleplayer
        );
        Assert.NotEqual(singleplayer, firstPlayer);
        Assert.NotEqual(firstPlayer, secondPlayer);
        Assert.Equal(firstPlayer, BonusRewardSelectionKey.Create(runSeed, ordinal, relicId, 0));
    }

    [Fact]
    public void BonusAssignmentsSurviveProgressDeltasAndSaveWithoutReservingRelicCoupons()
    {
        var before = new ApRunProgressState { CombatsSinceLastWaxMelt = 2 };
        var assigned = new ApProgressDelta().ApplyToCopy(before);
        assigned.BonusRelicAssignments[42] = "{\"IsWax\":true,\"id\":\"RELIC.THE_BOOT\"}";
        var delta = ApProgressDelta.Between(before, assigned);
        Assert.True(delta.HasChanges);
        var received = JsonSerializer.Deserialize<ApProgressDelta>(JsonSerializer.Serialize(delta))!.ApplyToCopy(before);
        var restored = JsonSerializer.Deserialize<ApRunProgressState>(JsonSerializer.Serialize(received))!;
        Assert.Equal(assigned.BonusRelicAssignments[42], restored.BonusRelicAssignments[42]);
        Assert.Equal(2, restored.CombatsSinceLastWaxMelt);
        Assert.Empty(restored.RelicChoiceAssignments);
        Assert.Empty(before.BonusRelicAssignments);
        Assert.False(ApProgressDelta.Between(restored, received).HasChanges);

        received.BonusRelicAssignments.Clear();
        var removed = ApProgressDelta.Between(restored, received).ApplyToCopy(restored);
        Assert.Empty(removed.BonusRelicAssignments);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void BonusRequiresExactlyOneSerializedRelic(int models)
    {
        var spec = new ApMirroredRewardSpec
        {
            Kind = ApMirroredRewardKind.Bonus,
            SerializedModels = Enumerable.Repeat("{}", models).ToList(),
        };
        Assert.Throws<InvalidOperationException>(() => MirroredRewardAdapter.Decode(spec, 3));
    }
}
