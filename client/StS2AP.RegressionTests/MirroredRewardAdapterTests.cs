using System.Text.Json;
using StS2AP.Domain;
using StS2AP.DomainAdapters;
using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class MirroredRewardAdapterTests
{
    private static ApMirroredRewardSpec Card() => new()
    {
        ApSlotId = 7, ReceivedItemIndex = 42, OwnerNetId = 123,
        Kind = ApMirroredRewardKind.Card, CardRewardActIndex = 1,
        CardHasBeenRevealed = true, MaterializationStrategyId = "ap_rng_replicated_card_v1",
        SerializedModels = ["{\"id\":\"CARD.A\"}", "{\"id\":\"CARD.B\"}"],
        SenderName = "sender", FoundLocation = "location",
    };

    private static CardRewardData AsCard(MirroredReward reward) => reward.Match(
        card => card,
        _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException(),
        _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException(),
        _ => throw new InvalidOperationException());

    [Fact]
    public void ExistingWireJsonRoundTripsWithoutDomainSerialization()
    {
        const string json = """
            {"ApSlotId":7,"ReceivedItemIndex":42,"OwnerNetId":123,
             "Kind":0,"ItemName":"cards","SenderName":"sender","FoundLocation":"location",
             "IsRareCardReward":false,"CardRewardActIndex":1,"CardCanReroll":true,
             "CardHasBeenRevealed":true,"MaterializationStrategyId":"ap_rng_replicated_card_v1",
             "SerializedModels":["{\"id\":\"CARD.A\"}","{\"id\":\"CARD.B\"}"]}
            """;
        var wire = JsonSerializer.Deserialize<ApMirroredRewardSpec>(json)!;
        string original = JsonSerializer.Serialize(wire);
        MirroredReward reward = MirroredRewardAdapter.Decode(wire, 3);
        CardRewardData card = AsCard(reward);
        Assert.True(card.Configuration.HasBeenRevealed);
        Assert.True(card.Configuration.CanReroll);
        Assert.Equal(1, card.Configuration.Recipe.ActIndex);
        Assert.Equal("7:42", reward.Origin.ReceiptIdentity);
        Assert.Equal(original, JsonSerializer.Serialize(wire));
        Assert.Equal(wire.SerializedModels, card.Models);
    }

    [Fact]
    public void MutatingWireCannotChangeExecutionSnapshot()
    {
        ApMirroredRewardSpec wire = Card();
        MirroredReward reward = MirroredRewardAdapter.Decode(wire, 3);
        CardRewardData card = AsCard(reward);
        string first = card.Models[0];
        wire.SerializedModels[0] = "{}";
        wire.Kind = ApMirroredRewardKind.Potion;
        wire.ReceivedItemIndex = 0;
        wire.SenderName = "changed";
        Assert.Equal(first, card.Models[0]);
        Assert.Equal(42, reward.Origin.ReceivedItemIndex);
        Assert.Equal("sender", reward.Origin.SenderName);
        Assert.Same(card, AsCard(reward));
    }

    [Fact]
    public void SavedRevealedCardsRestoreWithoutReroll()
    {
        var saved = new ApCardAssignmentState
        {
            SerializedCards = Card().SerializedModels, CanReroll = true,
            IsRare = false, RewardActIndex = null, HasBeenRevealed = true,
            MaterializationStrategyId = "ap_rng_replicated_card_v1",
        };
        // Exercise the production save decoder with its actual snake_case JSON contract.
        string json = JsonSerializer.Serialize(saved);
        Assert.Contains("\"has_been_revealed\":true", json);
        saved = JsonSerializer.Deserialize<ApCardAssignmentState>(json)!;
        var reward = MirroredRewardAdapter.DecodeSavedCardAssignment(42, saved, 123);
        CardRewardData card = reward.Card;
        Assert.Equal(42, reward.Origin.ReceivedItemIndex);
        Assert.Equal(123UL, reward.Origin.OwnerNetId);
        Assert.True(card.Configuration.HasBeenRevealed);
        Assert.True(card.Configuration.CanReroll);
        Assert.Null(card.Configuration.Recipe.ActIndex);
        Assert.Equal(saved.SerializedCards, card.Models);
        Assert.Equal("ap_rng_replicated_card_v1", card.Configuration.Policy.StrategyId);
        Assert.Equal("replicated", card.Configuration.Policy.Match(
            () => "owner", () => "replicated"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("broken")]
    [InlineData("")]
    [InlineData(null)]
    public void MalformedModelJsonIsRejectedAtTheCodec(string? json)
    {
        var spec = Card();
        spec.SerializedModels = [json!];
        Assert.Contains("7:42", Assert.Throws<InvalidOperationException>(() => MirroredRewardAdapter.Decode(spec, 3)).Message);
        var saved = new ApCardAssignmentState { SerializedCards = [json!] };
        Assert.Contains("0:42", Assert.Throws<InvalidOperationException>(() =>
            MirroredRewardAdapter.DecodeSavedCardAssignment(42, saved, 123)).Message);
    }

    [Fact]
    public void SavedCardDecoderPreservesValidationAndSnapshotIsolation()
    {
        var saved = new ApCardAssignmentState { SerializedCards = ["{}"],
            HasBeenRevealed = true, MaterializationStrategyId = "ap_rng_replicated_card_v1" };
        var restored = MirroredRewardAdapter.DecodeSavedCardAssignment(42, saved, 123);
        saved.SerializedCards[0] = "{\"changed\":true}";
        Assert.Equal("{}", restored.Card.Models[0]);

        Assert.Throws<InvalidOperationException>(() => MirroredRewardAdapter.DecodeSavedCardAssignment(-1, saved, 123));
        saved.SerializedCards.Clear();
        Assert.Throws<InvalidOperationException>(() => MirroredRewardAdapter.DecodeSavedCardAssignment(42, saved, 123));
        saved.SerializedCards.Add("{}");
        saved.IsRare = true;
        saved.RewardActIndex = 1;
        Assert.Throws<InvalidOperationException>(() => MirroredRewardAdapter.DecodeSavedCardAssignment(42, saved, 123));
        saved.RewardActIndex = null;
        saved.HasBeenRevealed = false;
        Assert.Throws<InvalidOperationException>(() => MirroredRewardAdapter.DecodeSavedCardAssignment(42, saved, 123));
    }

    [Theory]
    [InlineData("unknown", "used an unknown materialization strategy.")]
    [InlineData("replica_native_v1", "used an unknown materialization strategy.")]
    [InlineData("ap_rng_owner_final_v1", "had an unsupported card materialization strategy.")]
    public void ExistingStrategyDiagnosticsRetainReceiptContext(string strategy, string message)
    {
        var spec = Card();
        spec.MaterializationStrategyId = strategy;
        Assert.Equal($"AP reward 7:42 {message}",
            Assert.Throws<InvalidOperationException>(() => MirroredRewardAdapter.Decode(spec, 3)).Message);
    }

    [Theory]
    [InlineData(ApMirroredRewardKind.Potion, 1)]
    [InlineData(ApMirroredRewardKind.Relic, 1)]
    [InlineData(ApMirroredRewardKind.Bonus, 1)]
    [InlineData(ApMirroredRewardKind.Ancient, 3)]
    [InlineData(ApMirroredRewardKind.Unavailable, 0)]
    public void OtherRewardKindsUseTheirNativePayload(ApMirroredRewardKind kind, int count)
    {
        var spec = Card();
        spec.Kind = kind;
        spec.MaterializationStrategyId = "ap_rng_owner_final_v1";
        spec.SerializedModels = Enumerable.Repeat("{}", count).ToList();
        spec.UnavailableReason = "No valid Ancient relic choice is available for this receipt.";
        MirroredReward reward = MirroredRewardAdapter.Decode(spec, 3);
        Assert.Equal(kind.ToString(), reward.Match(
            _ => "Card", _ => "Potion", _ => "Relic", _ => "Ancient", _ => "Unavailable", _ => "Bonus"));
        Assert.Equal(kind == ApMirroredRewardKind.Relic, reward.IsRelic);
    }

    [Fact]
    public void MissingEntryAndUnknownKindFailBeforeNativeExecution()
    {
        Assert.Throws<InvalidOperationException>(() => MirroredRewardAdapter.Decode(null!, 3));
        var spec = Card();
        spec.Kind = (ApMirroredRewardKind)100;
        Assert.Throws<InvalidOperationException>(() => MirroredRewardAdapter.Decode(spec, 3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ap_rng_owner_final_v1")]
    [InlineData("replica_native_v1")]
    public void SavedCardStrategyMustBeCurrentWithoutFallback(string? strategy)
    {
        var saved = new ApCardAssignmentState
        {
            SerializedCards = Card().SerializedModels, HasBeenRevealed = true,
            MaterializationStrategyId = strategy!,
        };
        Assert.Contains("0:42", Assert.Throws<InvalidOperationException>(() =>
            MirroredRewardAdapter.DecodeSavedCardAssignment(42, saved, 123)).Message);
    }
}
