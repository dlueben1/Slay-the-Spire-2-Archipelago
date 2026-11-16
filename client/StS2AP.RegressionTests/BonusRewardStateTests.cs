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
    public void BonusAssignmentsSurviveTheProgressCheckpointWithoutReservingRelicCoupons()
    {
        var assigned = new ApRunProgressState { CombatsSinceLastWaxMelt = 2 };
        assigned.BonusRelicAssignments[42] = "{\"IsWax\":true,\"id\":\"RELIC.THE_BOOT\"}";
        var restored = JsonSerializer.Deserialize<ApRunProgressState>(JsonSerializer.Serialize(assigned))!;
        Assert.Equal(assigned.BonusRelicAssignments[42], restored.BonusRelicAssignments[42]);
        Assert.Equal(2, restored.CombatsSinceLastWaxMelt);
        Assert.Empty(restored.RelicChoiceAssignments);
    }
}
