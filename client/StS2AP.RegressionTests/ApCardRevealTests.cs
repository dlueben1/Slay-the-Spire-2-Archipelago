using System.Text.Json;
using StS2AP.DomainAdapters;
using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ApCardRevealTests
{
    private static ApMirroredRewardSpec Recipe() => new()
    {
        ApSlotId = 7, ReceivedItemIndex = 42, OwnerNetId = 123,
        Kind = ApMirroredRewardKind.Card, CardRewardActIndex = 1,
        MaterializationStrategyId = "ap_rng_replicated_card_v1",
    };

    private static ApMirroredRewardSpec Final()
    {
        var spec = Recipe();
        spec.CardHasBeenRevealed = true;
        spec.SerializedModels = ["""{"id":"CARD.A","upgrade":1,"enchantment":{"id":"GLAM"}}""",
                                 """{"id":"CARD.B","upgrade":0}"""];
        return spec;
    }

    [Fact]
    public void TwelveUnopenedRewardsRoundTripAsRecipesWithoutAssignments()
    {
        var menu = new ApRewardMenuSpec();
        for (int index = 0; index < 12; index++)
        {
            var spec = Recipe();
            spec.ReceivedItemIndex = index;
            menu.Rewards.Add(spec);
        }
        foreach (var spec in RoundTrip(menu).Rewards)
        {
            var reward = MirroredRewardAdapter.Decode(spec, 3);
            Assert.True(reward.Match(card => card.IsDeferred, _ => false, _ => false, _ => false, _ => false, _ => false));
        }
        Assert.Throws<InvalidOperationException>(() =>
            MirroredRewardAdapter.DecodeSavedCardAssignment(42, new ApCardAssignmentState(), 123));
    }

    [Fact]
    public void ModelsMatchAcrossOperatingSystemNewlinesAndPropertyOrder()
    {
        const string windows = "{\r\n  \"id\": \"CARD.A\",\r\n  \"upgrade\": 1\r\n}";
        const string linux = "{\n  \"id\": \"CARD.A\",\n  \"upgrade\": 1\n}";
        const string reordered = """{"upgrade":1,"id":"CARD.A"}""";
        Assert.True(ApCardRevealCodec.SameModels([windows], [linux]));
        Assert.True(ApCardRevealCodec.SameModels([windows], [reordered]));
    }

    [Fact]
    public void ModelsThatReallyDifferStillDisagree()
    {
        const string a = """{"id":"CARD.A","upgrade":0}""";
        const string upgraded = """{"id":"CARD.A","upgrade":1}""";
        const string b = """{"id":"CARD.B","upgrade":0}""";
        Assert.False(ApCardRevealCodec.SameModels([a], [upgraded]));
        Assert.False(ApCardRevealCodec.SameModels([a, b], [b, a]));
        Assert.False(ApCardRevealCodec.SameModels([a], [a, b]));
    }

    [Fact]
    public void RefreshedOfferSurvivesSaveAndProgressDeltaWithoutReplayInstructions()
    {
        var before = Final();
        var owner = RoundTrip(before);
        owner.SerializedModels[1] = owner.SerializedModels[1].Replace("\"upgrade\":0", "\"upgrade\":1");
        var replica = RoundTrip(owner); // Represents independently refreshed final cards.
        var oldProgress = new ApRunProgressState { CardAssignments = { [42] = Assignment(before) } };
        var newProgress = new ApRunProgressState { CardAssignments = { [42] = Assignment(replica) } };
        ApProgressDelta delta = RoundTrip(ApProgressDelta.Between(oldProgress, newProgress));
        Assert.Single(delta.CardAssignmentUpserts);
        Assert.Empty(delta.UsedItemsAdded);
        ApRunProgressState restored = RoundTrip(delta.ApplyToCopy(oldProgress));
        var decoded = MirroredRewardAdapter.DecodeSavedCardAssignment(42, restored.CardAssignments[42], 123).Card;
        Assert.Equal(owner.SerializedModels, decoded.Models);
        Assert.True(decoded.Configuration.HasBeenRevealed);
        Assert.Equal("ap_rng_replicated_card_v1", decoded.Configuration.Policy.StrategyId);
        Assert.False(ApProgressDelta.Between(restored, newProgress).HasChanges);
    }

    private static ApCardAssignmentState Assignment(ApMirroredRewardSpec spec) => new()
    {
        SerializedCards = spec.SerializedModels.ToList(), CanReroll = spec.CardCanReroll,
        IsRare = spec.IsRareCardReward, RewardActIndex = spec.CardRewardActIndex,
        HasBeenRevealed = spec.CardHasBeenRevealed, MaterializationStrategyId = spec.MaterializationStrategyId,
    };

    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
