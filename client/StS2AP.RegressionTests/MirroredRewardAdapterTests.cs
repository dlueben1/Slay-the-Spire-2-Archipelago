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
        MaterializationStrategyId = "ap_rng_owner_final_v1",
        SerializedModels = ["{\"id\":\"CARD.A\"}", "{\"id\":\"CARD.B\"}"],
        AppliedEffects = [new() { EffectId = "silver_crucible_times_used_v1", BeforeValue = 3, AfterValue = 4 }],
        SenderName = "sender", FoundLocation = "location",
    };

    private static CardRewardData AsCard(MirroredReward reward) => reward.Match(
        card => card,
        _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException(),
        _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException());

    [Fact]
    public void ExistingWireJsonRoundTripsWithoutDomainSerialization()
    {
        const string json = """
            {"SchemaVersion":5,"ApSlotId":7,"ReceivedItemIndex":42,"OwnerNetId":123,
             "Kind":0,"ItemName":"cards","SenderName":"sender","FoundLocation":"location",
             "IsRareCardReward":false,"CardRewardActIndex":1,"CardCanReroll":true,
             "CardHasBeenRevealed":true,"MaterializationStrategyId":"ap_rng_owner_final_v1",
             "RequiresNativeMaterialization":false,"StateBeforeMaterialization":"","StateAfterMaterialization":"",
             "AppliedEffects":[{"EffectId":"silken_tress_used_v1","BeforeValue":0,"AfterValue":1}],
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
        Assert.Equal(JsonSerializer.Serialize(wire.AppliedEffects),
            JsonSerializer.Serialize(MirroredRewardAdapter.EncodeEffects(card.Configuration.Effects)));
    }

    [Fact]
    public void MutatingWireOrEncodedEffectsCannotChangeExecutionSnapshot()
    {
        ApMirroredRewardSpec wire = Card();
        MirroredReward reward = MirroredRewardAdapter.Decode(wire, 3);
        CardRewardData card = AsCard(reward);
        string first = card.Models[0];
        wire.SerializedModels[0] = "{}";
        wire.AppliedEffects[0].AfterValue = 100;
        wire.Kind = ApMirroredRewardKind.Potion;
        wire.ReceivedItemIndex = 0;
        wire.SenderName = "changed";
        MirroredRewardAdapter.EncodeEffects(reward.Effects)[0].BeforeValue = 500;
        Assert.Equal(first, card.Models[0]);
        Assert.Equal(4, reward.Effects[0].AfterValue);
        Assert.Equal(3, reward.Effects[0].BeforeValue);
        Assert.Equal(42, reward.Origin.ReceivedItemIndex);
        Assert.Equal("sender", reward.Origin.SenderName);
        Assert.Same(card, AsCard(reward));
    }

    [Theory]
    [InlineData("replica_native_v1")]
    [InlineData("ap_rng_owner_final_v1")]
    [InlineData("")]
    public void SavedRevealedCardsRestoreWithoutReplayOrReroll(string strategy)
    {
        var saved = new ApCardAssignmentState
        {
            SerializedCards = Card().SerializedModels, CanReroll = true,
            IsRare = false, RewardActIndex = null, HasBeenRevealed = true,
            MaterializationStrategyId = strategy,
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
        Assert.Equal(strategy == "" ? "ap_rng_owner_final_v1" : strategy, card.Configuration.Policy.StrategyId);
        Assert.Equal(strategy == "replica_native_v1" ? "restore" : "owner", card.Configuration.Policy.Match(
            () => "owner", () => "restore"));
    }

    [Fact]
    public void RevealAndEffectsSurviveSaveEncodingAndReopen()
    {
        CardRewardConfiguration revealed = AsCard(MirroredRewardAdapter.Decode(Card(), 3)).Configuration.WithRevealed();
        var saved = new ApCardAssignmentState
        {
            SerializedCards = Card().SerializedModels,
            IsRare = revealed.Recipe.IsRareReward, RewardActIndex = revealed.Recipe.ActIndex,
            HasBeenRevealed = revealed.HasBeenRevealed, CanReroll = revealed.CanReroll,
            MaterializationStrategyId = revealed.Policy.StrategyId,
            AppliedEffects = MirroredRewardAdapter.EncodeEffects(revealed.Effects),
        };
        var roundTrip = JsonSerializer.Deserialize<ApCardAssignmentState>(JsonSerializer.Serialize(saved))!;
        CardRewardData restored = MirroredRewardAdapter.DecodeSavedCardAssignment(42, roundTrip, 123).Card;
        Assert.True(restored.Configuration.HasBeenRevealed);
        Assert.False(MirroredRewardAdapter.NeedsApplication(restored.Configuration.Effects[0], 4, "7:42"));
        Assert.False(MirroredRewardAdapter.NeedsApplication(restored.Configuration.Effects[0], 6, "7:42"));
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
        var saved = new ApCardAssignmentState { SerializedCards = ["{}"] };
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
        saved.AppliedEffects = null!;
        Assert.Throws<InvalidOperationException>(() => MirroredRewardAdapter.DecodeSavedCardAssignment(42, saved, 123));
    }

    [Fact]
    public void ObservedEffectsKeepTheWireContractAndRejectUnexpectedHookResults()
    {
        var effects = MirroredRewardAdapter.EncodeEffects([
            MirroredRewardAdapter.ObserveSilkenTress(0, 1, "7:42"),
            MirroredRewardAdapter.ObserveSilverCrucible(3, 4, "7:42"),
        ]);
        Assert.Equal("silken_tress_used_v1", effects[0].EffectId);
        Assert.Equal(0, effects[0].BeforeValue);
        Assert.Equal(1, effects[0].AfterValue);
        Assert.Equal("silver_crucible_times_used_v1", effects[1].EffectId);
        Assert.Equal(3, effects[1].BeforeValue);
        Assert.Equal(4, effects[1].AfterValue);
        var wire = Card();
        wire.AppliedEffects = JsonSerializer.Deserialize<List<ApRewardEffectSpec>>(JsonSerializer.Serialize(effects))!;
        Assert.Equal(JsonSerializer.Serialize(effects),
            JsonSerializer.Serialize(MirroredRewardAdapter.EncodeEffects(MirroredRewardAdapter.Decode(wire, 3).Effects)));

        Assert.Contains("7:42", Assert.Throws<InvalidOperationException>(() =>
            MirroredRewardAdapter.ObserveSilkenTress(1, 0, "7:42")).Message);
        Assert.Contains("7:42", Assert.Throws<InvalidOperationException>(() =>
            MirroredRewardAdapter.ObserveSilverCrucible(3, 5, "7:42")).Message);
    }

    [Theory]
    [InlineData("unknown", false, "used an unknown materialization strategy.")]
    [InlineData("ap_rng_owner_final_v1", true, "requested removed replica-native generation.")]
    public void ExistingStrategyDiagnosticsRetainReceiptContext(string strategy, bool replay, string message)
    {
        var spec = Card();
        spec.MaterializationStrategyId = strategy;
        spec.RequiresNativeMaterialization = replay;
        Assert.Equal($"AP reward 7:42 {message}",
            Assert.Throws<InvalidOperationException>(() => MirroredRewardAdapter.Decode(spec, 3)).Message);
    }

    [Theory]
    [InlineData(ApMirroredRewardKind.Potion, 1)]
    [InlineData(ApMirroredRewardKind.Relic, 1)]
    [InlineData(ApMirroredRewardKind.Ancient, 3)]
    [InlineData(ApMirroredRewardKind.Unavailable, 0)]
    public void OtherRewardKindsUseTheirNativePayload(ApMirroredRewardKind kind, int count)
    {
        var spec = Card();
        spec.Kind = kind;
        spec.AppliedEffects.Clear();
        spec.SerializedModels = Enumerable.Repeat("{}", count).ToList();
        spec.UnavailableReason = "No valid Ancient relic choice is available for this receipt.";
        MirroredReward reward = MirroredRewardAdapter.Decode(spec, 3);
        Assert.Equal(kind.ToString(), reward.Match(
            _ => "Card", _ => "Potion", _ => "Relic", _ => "Ancient", _ => "Unavailable"));
    }

    [Fact]
    public void SchemaAndUnknownKindFailBeforeNativeExecution()
    {
        var spec = Card();
        spec.SchemaVersion = 6;
        Assert.Throws<InvalidOperationException>(() => MirroredRewardAdapter.Decode(spec, 3));
        spec.SchemaVersion = 5;
        spec.Kind = (ApMirroredRewardKind)100;
        Assert.Throws<InvalidOperationException>(() => MirroredRewardAdapter.Decode(spec, 3));
    }

    [Theory]
    [InlineData(ApMirroredRewardKind.Card)]
    [InlineData(ApMirroredRewardKind.Potion)]
    [InlineData(ApMirroredRewardKind.Relic)]
    [InlineData(ApMirroredRewardKind.Ancient)]
    [InlineData(ApMirroredRewardKind.Unavailable)]
    public void LegacyReplicaGenerationRequestIsRejectedBeforePayloadDecoding(ApMirroredRewardKind kind)
    {
        // Old fingerprint fields may still arrive, but must never enable generation or silent restoration.
        var spec = JsonSerializer.Deserialize<ApMirroredRewardSpec>("""
            {"SchemaVersion":5,"ApSlotId":7,"ReceivedItemIndex":42,
             "MaterializationStrategyId":"replica_native_v1","RequiresNativeMaterialization":true,
             "StateBeforeMaterialization":"pre","StateAfterMaterialization":"post",
             "SerializedModels":["invalid JSON"]}
            """)!;
        spec.Kind = kind;
        Assert.Equal("AP reward 7:42 requested removed replica-native generation.",
            Assert.Throws<InvalidOperationException>(() => MirroredRewardAdapter.Decode(spec, 3)).Message);
        Assert.Throws<InvalidOperationException>(() => MirroredRewardAdapter.CardConfiguration(spec));
    }
}
