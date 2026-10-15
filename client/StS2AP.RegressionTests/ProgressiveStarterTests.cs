using System.Text.Json;
using StS2AP.Domain;
using StS2AP.DomainAdapters;
using StS2AP.Persistence;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class ProgressiveStarterTests
{
    private static ApProgressiveStarterKindState Recipe(int tier = 1) => new()
    {
        Initialized = true,
        Supported = true,
        BaseId = "CARD.STARTER",
        UpgradedId = "CARD.ANCIENT",
        SerializedBaseModel = "{\"id\":\"CARD.STARTER\",\"upgrades\":1}",
        SerializedUpgradeRelic = "{\"id\":\"RELIC.ARCHAIC_TOOTH\"}",
        AppliedTier = (ProgressiveStarterTier)tier,
    };

    private static StarterState<CapturedStarterRecipe> Captured(int tier) =>
        ProgressiveStarterAdapter.Decode(Recipe(tier), StarterKind.Card);

    private static StarterState<StarterMapping> Mapping(int tier) =>
        ProgressiveStarterAdapter.DecodeSingleplayer(StarterKind.Card, "CARD.STARTER", "CARD.ANCIENT",
            (ProgressiveStarterTier)tier);

    [Theory]
    [InlineData(0, 0, "")]
    [InlineData(0, 1, "RestoreBase")]
    [InlineData(0, 2, "RestoreBase,GrantUpgrade")]
    [InlineData(1, 0, "RemoveBase")]
    [InlineData(1, 1, "")]
    [InlineData(1, 2, "GrantUpgrade")]
    [InlineData(2, 0, null)]
    [InlineData(2, 1, null)]
    [InlineData(2, 2, "")]
    public void SingleplayerAndMultiplayerShareTheCompleteInitializationTransitionTable(
        int from, int to, string? expected)
    {
        var single = StarterProgression.Plan(Mapping(from), Mapping(from), StarterContext.Initialization, to);
        var multi = StarterProgression.Plan(Captured(from), Captured(from), StarterContext.Initialization, to);
        Assert.Equal(expected == null, single.IsError);
        Assert.Equal(expected == null, multi.IsError);
        if (expected == null) return;
        Assert.Equal(expected, string.Join(",", single.ResultValue.Operations));
        Assert.Equal(expected, string.Join(",", multi.ResultValue.Operations));
        var singleState = single.ResultValue.State;
        var multiState = multi.ResultValue.State;
        foreach (var operation in single.ResultValue.Operations)
            singleState = ProgressiveStarterAdapter.Require(StarterProgression.AfterApplied(singleState, operation));
        foreach (var operation in multi.ResultValue.Operations)
            multiState = ProgressiveStarterAdapter.Require(StarterProgression.AfterApplied(multiState, operation));
        Assert.Equal(to, singleState.AppliedWireValue);
        Assert.Equal(to, multiState.AppliedWireValue);
    }

    [Fact]
    public void DowngradeIsOnlyAllowedAtInitialization()
    {
        foreach (var context in new[] { StarterContext.Reconciliation, StarterContext.LiveReceipt })
        {
            Assert.True(StarterProgression.Plan(Mapping(1), Mapping(1), context, 0).IsError);
            Assert.True(StarterProgression.Plan(Captured(1), Captured(1), context, 0).IsError);
        }
        Assert.True(StarterProgression.Plan(Captured(0), Captured(0), StarterContext.LiveReceipt, 0).IsError);
        Assert.Empty(ProgressiveStarterAdapter.Require(
            StarterProgression.Plan(Mapping(0), Mapping(0), StarterContext.Reconciliation, 0)).Operations);
    }

    [Fact]
    public void NewMultiplayerPlayerAdoptsRecipeThenRemovesVanillaStarter()
    {
        var empty = ProgressiveStarterAdapter.Decode(new(), StarterKind.Card);
        var supplied = Captured(1);
        var plan = ProgressiveStarterAdapter.Require(StarterProgression.Plan(empty, supplied, StarterContext.Initialization, 0));
        Assert.Equal(supplied, plan.State);
        Assert.Equal(new[] { StarterOperation.RemoveBase }, plan.Operations);
        Assert.False(ProgressiveStarterAdapter.Encode(empty).Initialized);
        Assert.Equal(1, supplied.AppliedWireValue); // Planning has no side effects.
    }

    [Fact]
    public void RepeatedReconciliationUsesCurrentTierNotStaleAuthoredTier()
    {
        var stale = Captured(1);
        var current = Captured(0);
        var plan = ProgressiveStarterAdapter.Require(StarterProgression.Plan(current, stale, StarterContext.LiveReceipt, 2));
        Assert.Equal(new[] { StarterOperation.RestoreBase, StarterOperation.GrantUpgrade }, plan.Operations);
        foreach (var operation in plan.Operations)
            current = ProgressiveStarterAdapter.Require(StarterProgression.AfterApplied(current, operation));
        Assert.Empty(ProgressiveStarterAdapter.Require(
            StarterProgression.Plan(current, stale, StarterContext.LiveReceipt, 2)).Operations);
    }

    [Fact]
    public void AFailedSecondCommandLeavesOnlyTheSuccessfulBaseRestorationApplied()
    {
        var original = Captured(0);
        var plan = ProgressiveStarterAdapter.Require(StarterProgression.Plan(original, original, StarterContext.LiveReceipt, 2));
        var current = ProgressiveStarterAdapter.Require(StarterProgression.AfterApplied(original, plan.Operations.Head));
        // The executor does not acknowledge GrantUpgrade if its game command fails.
        Assert.Equal(0, original.AppliedWireValue);
        Assert.Equal(1, current.AppliedWireValue);
        Assert.Equal(ProgressiveStarterTier.Basic, ProgressiveStarterAdapter.Encode(current).AppliedTier);
        Assert.True(StarterProgression.AfterApplied(original, StarterOperation.GrantUpgrade).IsError);
    }

    [Fact]
    public void RecipeIsBoundToThePlayerAndExactCapturedPayload()
    {
        var original = Captured(1);
        var changed = Recipe();
        changed.SerializedBaseModel = "{ \"id\":\"CARD.STARTER\",\"upgrades\":1}";
        var result = StarterProgression.Plan(original, ProgressiveStarterAdapter.Decode(changed, StarterKind.Card),
            StarterContext.LiveReceipt, 2);
        Assert.True(result.IsError);
        Assert.Equal(StarterError.RecipeChanged, result.ErrorValue);
        var relic = ProgressiveStarterAdapter.Decode(Recipe(), StarterKind.Relic);
        Assert.True(StarterProgression.Plan(original, relic, StarterContext.LiveReceipt, 2).IsError);
        Assert.Equal(StarterKind.Relic, relic.Match(() => null!, () => null!, (recipe, _) => recipe.Mapping.Kind));
    }

    [Fact]
    public void UnsupportedAndUninitializedAreDistinctAndCannotCarryRecipes()
    {
        var empty = ProgressiveStarterAdapter.Decode(new(), StarterKind.Card);
        var unsupported = ProgressiveStarterAdapter.Decode(new() { Initialized = true }, StarterKind.Card);
        Assert.NotEqual(empty, unsupported);
        Assert.Throws<InvalidOperationException>(() => ProgressiveStarterAdapter.ReceivedTarget(empty, 2));
        Assert.Equal(ProgressiveStarterTier.Unsupported, ProgressiveStarterAdapter.ReceivedTarget(unsupported, 2));
        Assert.Equal(ProgressiveStarterTier.Upgraded, ProgressiveStarterAdapter.ReceivedTarget(Captured(0), 2));
        Assert.True(StarterProgression.Plan(empty, empty, StarterContext.Initialization, -1).IsError);
        Assert.Empty(ProgressiveStarterAdapter.Require(
            StarterProgression.Plan(empty, unsupported, StarterContext.Initialization, -1)).Operations);
        Assert.Empty(ProgressiveStarterAdapter.Require(
            StarterProgression.Plan(unsupported, unsupported, StarterContext.LiveReceipt, -1)).Operations);
        Assert.True(StarterProgression.Plan(unsupported, unsupported, StarterContext.Initialization, 0).IsError);
        Assert.True(StarterProgression.Plan(unsupported, Captured(1), StarterContext.Initialization, 1).IsError);
        Assert.Throws<InvalidOperationException>(() => ProgressiveStarterAdapter.Decode(
            new() { Initialized = true, BaseId = "unexpected" }, StarterKind.Card));
        Assert.Throws<InvalidOperationException>(() => ProgressiveStarterAdapter.Decode(
            new() { Supported = true }, StarterKind.Card));
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void SupportedStatesAndTargetsRejectInvalidTiers(int tier)
    {
        Assert.Throws<InvalidOperationException>(() => Captured(tier));
        Assert.Throws<InvalidOperationException>(() => Mapping(tier));
        Assert.True(StarterProgression.Plan(Captured(1), Captured(1), StarterContext.Initialization, tier).IsError);
    }

    [Fact]
    public void AllRequiredRecipeFieldsAreValidated()
    {
        foreach (string property in new[] { "BaseId", "UpgradedId", "SerializedBaseModel", "SerializedUpgradeRelic" })
        foreach (string? missing in new[] { null, "", " " })
        {
            var dto = Recipe();
            typeof(ApProgressiveStarterKindState).GetProperty(property)!.SetValue(dto, missing);
            Assert.Throws<InvalidOperationException>(() => ProgressiveStarterAdapter.Decode(dto, StarterKind.Card));
        }
        Assert.Throws<InvalidOperationException>(() => ProgressiveStarterAdapter.Decode(null!, StarterKind.Card));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("1")]
    [InlineData("{")]
    public void CapturedPayloadsMustBeJsonObjects(string invalid)
    {
        var dto = Recipe();
        dto.SerializedBaseModel = invalid;
        Assert.Throws<InvalidOperationException>(() => ProgressiveStarterAdapter.Decode(dto, StarterKind.Card));
        dto = Recipe();
        dto.SerializedUpgradeRelic = invalid;
        Assert.Throws<InvalidOperationException>(() => ProgressiveStarterAdapter.Decode(dto, StarterKind.Card));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(20, 2)]
    public void ReceivedCountIsBoundedIndependentlyOfAppliedState(int count, int expected) =>
        Assert.Equal(expected, (int)ProgressiveStarterAdapter.ReceivedTier(count));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SavedRecipeRoundTripRetainsExactIdentityPayloadAndAppliedTier(int tier)
    {
        var dto = Recipe(tier);
        string saved = JsonSerializer.Serialize(dto);
        var restored = ProgressiveStarterAdapter.Decode(JsonSerializer.Deserialize<ApProgressiveStarterKindState>(saved)!, StarterKind.Card);
        Assert.Equal(saved, JsonSerializer.Serialize(ProgressiveStarterAdapter.Encode(restored)));
        dto.BaseId = "changed after decoding";
        Assert.Equal("CARD.STARTER", ProgressiveStarterAdapter.Encode(restored).BaseId);
        Assert.Empty(ProgressiveStarterAdapter.Require(
            StarterProgression.Plan(restored, restored, StarterContext.Reconciliation, tier)).Operations);
    }

    [Fact]
    public void SingleplayerAbsentMappingRemainsUnsupportedAndPartialMappingIsRejected()
    {
        var unsupported = ProgressiveStarterAdapter.DecodeSingleplayer(StarterKind.Relic, null, null, ProgressiveStarterTier.Unsupported);
        Assert.True(unsupported.Match(() => false, () => true, (_, _) => false));
        Assert.Throws<InvalidOperationException>(() => ProgressiveStarterAdapter.DecodeSingleplayer(
            StarterKind.Relic, "RELIC.STARTER", null, ProgressiveStarterTier.Basic));
    }

    [Fact]
    public void UnknownWireKindOrReasonCannotFallThroughToRelicOrInitialization()
    {
        Assert.Throws<InvalidOperationException>(() => ProgressiveStarterAdapter.Kind((ApProgressiveStarterActionMessage.StarterKind)99));
        Assert.Throws<InvalidOperationException>(() => ProgressiveStarterAdapter.Context((ApProgressiveStarterActionMessage.ActionReason)99));
    }
}
