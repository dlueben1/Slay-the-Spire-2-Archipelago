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
        => Require(CardRewardConfiguration.Decode(spec.IsRareCardReward, spec.CardRewardActIndex,
            spec.CardHasBeenRevealed, spec.CardCanReroll, spec.MaterializationStrategyId), spec.GrantId.ToString());

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
            MaterializationStrategyId = assignment.MaterializationStrategyId,
            SerializedModels = assignment.SerializedCards,
        });
        return (input.Origin, Require(MirroredReward.DecodeCard(input), input.Origin.ReceiptIdentity));
    }

    public static MirroredReward Decode(ApMirroredRewardSpec spec, int ancientChoiceCount) =>
        Require(MirroredReward.Decode(ToInput(spec), ancientChoiceCount), spec.GrantId.ToString());

    private static MirroredRewardInput ToInput(ApMirroredRewardSpec spec)
    {
        if (spec == null)
            throw new InvalidOperationException("Invalid AP reward-menu entry.");

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
                ApMirroredRewardKind.Bonus => RewardInputKind.Bonus,
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
            Models = spec.SerializedModels?.ToArray()!,
            UnavailableReason = spec.UnavailableReason,
        };
    }

    private static T Require<T>(FSharpResult<T, RewardDecodeError> result, string identity)
    {
        if (result.IsOk) return result.ResultValue;
        throw result.ErrorValue.Match(
            reason => new InvalidOperationException($"AP reward {identity} {reason}"),
            error => error.Match(
                _ => new InvalidOperationException($"AP reward {identity} used an unknown materialization strategy.")));
    }
}
