using System.Text.Json;
using Microsoft.FSharp.Core;
using StS2AP.Domain;
using StS2AP.Persistence;

namespace StS2AP.DomainAdapters;

/// <summary>Converts mutable save/message data into immutable starter decisions.</summary>
internal static class ProgressiveStarterAdapter
{
    public static StarterKind Kind(ApProgressiveStarterActionMessage.StarterKind kind) => kind switch
    {
        ApProgressiveStarterActionMessage.StarterKind.Card => StarterKind.Card,
        ApProgressiveStarterActionMessage.StarterKind.Relic => StarterKind.Relic,
        _ => throw new InvalidOperationException($"Unknown progressive starter kind {(int)kind}."),
    };

    public static StarterContext Context(ApProgressiveStarterActionMessage.ActionReason reason) => reason switch
    {
        ApProgressiveStarterActionMessage.ActionReason.Initialization => StarterContext.Initialization,
        ApProgressiveStarterActionMessage.ActionReason.LiveReceipt => StarterContext.LiveReceipt,
        _ => throw new InvalidOperationException($"Unknown progressive starter reason {(int)reason}."),
    };

    public static ProgressiveStarterTier ReceivedTier(int count) =>
        (ProgressiveStarterTier)StarterTier.FromReceivedCount(count).WireValue;

    public static ProgressiveStarterTier ReceivedTarget(StarterState<CapturedStarterRecipe> state, int count) =>
        state.Match(
            () => throw new InvalidOperationException("A starter target requires a captured specification."),
            () => ProgressiveStarterTier.Unsupported,
            (_, _) => ReceivedTier(count));

    public static StarterState<StarterMapping> DecodeSingleplayer(
        StarterKind kind, string? baseId, string? upgradedId, ProgressiveStarterTier tier) =>
        Require(StarterProgression.DecodeSingleplayer(kind, baseId!, upgradedId!, (int)tier));

    public static StarterState<CapturedStarterRecipe> Decode(
        ApProgressiveStarterKindState dto, StarterKind kind)
    {
        if (dto == null)
            throw new InvalidOperationException("Missing progressive starter state.");
        var state = Require(StarterProgression.DecodeMultiplayer(kind, dto.Initialized, dto.Supported,
            dto.BaseId!, dto.UpgradedId!, dto.SerializedBaseModel!, dto.SerializedUpgradeRelic!, (int)dto.AppliedTier));
        return state.Match(() => state, () => state, (recipe, _) =>
        {
            // Schema interpretation and real model validation remain in the engine adapter.
            ValidateJsonObject(recipe.SerializedBaseModel);
            ValidateJsonObject(recipe.SerializedUpgradeRelic);
            return state;
        });
    }

    public static ApProgressiveStarterKindState Encode(StarterState<CapturedStarterRecipe> state) =>
        state.Match(
            () => new ApProgressiveStarterKindState(),
            () => new ApProgressiveStarterKindState { Initialized = true },
            (recipe, tier) => new ApProgressiveStarterKindState
            {
                Initialized = true,
                Supported = true,
                BaseId = recipe.Mapping.BaseId,
                UpgradedId = recipe.Mapping.UpgradedId,
                SerializedBaseModel = recipe.SerializedBaseModel,
                SerializedUpgradeRelic = recipe.SerializedUpgradeRelic,
                AppliedTier = (ProgressiveStarterTier)tier.WireValue,
            });

    public static T Require<T>(FSharpResult<T, StarterError> result) => result.IsOk
        ? result.ResultValue
        : throw new InvalidOperationException($"Invalid progressive starter: {result.ErrorValue.Description}");

    private static void ValidateJsonObject(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("Expected a serialized model object.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Invalid progressive starter model JSON.", ex);
        }
    }
}
