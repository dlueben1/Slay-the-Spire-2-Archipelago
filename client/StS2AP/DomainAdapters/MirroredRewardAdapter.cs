using System.Text.Json;
using Microsoft.FSharp.Core;
using StS2AP.Domain;
using StS2AP.Persistence;

namespace StS2AP.DomainAdapters;

/// <summary>Explicit wire/save boundary. Domain snapshots never retain the mutable DTO.</summary>
internal static class MirroredRewardAdapter
{
    public static RewardOrigin Origin(ApMirroredRewardSpec spec) => new(
        spec.ApSlotId, spec.ReceivedItemIndex, spec.OwnerNetId,
        spec.ItemName, spec.SenderName, spec.FoundLocation);

    public static CardRewardConfiguration CardConfiguration(ApMirroredRewardSpec spec)
    {
        RejectReplicaGeneration(spec);
        return Require(CardRewardConfiguration.Decode(spec.IsRareCardReward, spec.CardRewardActIndex,
            spec.CardHasBeenRevealed, spec.CardCanReroll, spec.MaterializationStrategyId,
            Effects(spec.AppliedEffects)), spec.GrantId.ToString());
    }

    public static (RewardOrigin Origin, CardRewardData Card) DecodeSavedCardAssignment(
        int itemIndex, ApCardAssignmentState assignment, ulong ownerNetId)
    {
        MirroredRewardInput input = ToInput(new ApMirroredRewardSpec
        {
            ReceivedItemIndex = itemIndex,
            OwnerNetId = ownerNetId,
            Kind = ApMirroredRewardKind.Card,
            IsRareCardReward = assignment.IsRare,
            CardRewardActIndex = assignment.RewardActIndex,
            CardHasBeenRevealed = assignment.HasBeenRevealed,
            CardCanReroll = assignment.CanReroll,
            // Preserve the existing save loader's default for pre-strategy card assignments.
            MaterializationStrategyId = string.IsNullOrEmpty(assignment.MaterializationStrategyId)
                ? "ap_rng_owner_final_v1" : assignment.MaterializationStrategyId,
            AppliedEffects = assignment.AppliedEffects,
            SerializedModels = assignment.SerializedCards,
        });
        return (input.Origin, Require(MirroredReward.DecodeCard(input), input.Origin.ReceiptIdentity));
    }

    public static MirroredReward Decode(ApMirroredRewardSpec spec, int ancientChoiceCount) =>
        Require(MirroredReward.Decode(ToInput(spec), ancientChoiceCount), spec.GrantId.ToString());

    private static MirroredRewardInput ToInput(ApMirroredRewardSpec spec)
    {
        if (spec == null || spec.SchemaVersion != 5)
            throw new InvalidOperationException("Invalid AP reward-menu entry schema.");
        RejectReplicaGeneration(spec);

        // These are MegaCrit serialized objects. Parsing, including model restoration, stays in C#.
        // Preserve the exact strings for hashing and replica comparisons.
        if (spec.SerializedModels != null)
        {
            foreach (string json in spec.SerializedModels)
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(json);
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                        throw new JsonException("Expected a serialized model object.");
                }
                catch (Exception ex) when (ex is JsonException or ArgumentNullException)
                {
                    throw new InvalidOperationException($"AP reward {spec.GrantId} had invalid model JSON.", ex);
                }
            }
        }

        return new MirroredRewardInput
        {
            Kind = spec.Kind switch
            {
                ApMirroredRewardKind.Card => RewardInputKind.Card,
                ApMirroredRewardKind.Potion => RewardInputKind.Potion,
                ApMirroredRewardKind.Relic => RewardInputKind.Relic,
                ApMirroredRewardKind.Ancient => RewardInputKind.Ancient,
                ApMirroredRewardKind.Unavailable => RewardInputKind.Unavailable,
                _ => throw new InvalidOperationException($"AP reward {spec.GrantId} had an invalid Unknown({(int)spec.Kind}) assignment."),
            },
            Origin = Origin(spec),
            IsRare = spec.IsRareCardReward,
            ActIndex = spec.CardRewardActIndex,
            Revealed = spec.CardHasBeenRevealed,
            CanReroll = spec.CardCanReroll,
            Strategy = spec.MaterializationStrategyId,
            Effects = Effects(spec.AppliedEffects),
            Models = spec.SerializedModels?.ToArray()!,
            UnavailableReason = spec.UnavailableReason,
        };
    }

    public static List<ApRewardEffectSpec> EncodeEffects(IEnumerable<RewardEffect> effects) => effects
        .Select(effect => new ApRewardEffectSpec
        {
            EffectId = effect.EffectId,
            BeforeValue = effect.BeforeValue,
            AfterValue = effect.AfterValue,
        }).ToList();

    public static bool NeedsApplication(RewardEffect effect, int current, string receiptIdentity) =>
        Require(effect.NeedsApplication(current), receiptIdentity);

    public static RewardEffect ObserveSilkenTress(int before, int after, string receiptIdentity) =>
        Require(RewardEffect.ObserveSilkenTress(before, after), receiptIdentity);

    public static RewardEffect ObserveSilverCrucible(int before, int after, string receiptIdentity) =>
        Require(RewardEffect.ObserveSilverCrucible(before, after), receiptIdentity);

    // Keep the old wire flag only to reject unsupported generation rather than silently restore it.
    private static void RejectReplicaGeneration(ApMirroredRewardSpec spec)
    {
        if (spec.RequiresNativeMaterialization)
            throw new InvalidOperationException($"AP reward {spec.GrantId} requested removed replica-native generation.");
    }

    private static RewardEffectInput[] Effects(IEnumerable<ApRewardEffectSpec>? effects) => effects?
        .Select(effect => effect == null ? null! : new RewardEffectInput
        {
            EffectId = effect.EffectId,
            BeforeValue = effect.BeforeValue,
            AfterValue = effect.AfterValue,
        }).ToArray()!;

    private static T Require<T>(FSharpResult<T, RewardDecodeError> result, string identity)
    {
        if (result.IsOk) return result.ResultValue;
        throw result.ErrorValue.Match(
            reason => new InvalidOperationException($"AP reward {identity} {reason}"),
            error => error.Match(
                _ => new InvalidOperationException($"AP reward {identity} used an unknown materialization strategy.")));
    }
}
