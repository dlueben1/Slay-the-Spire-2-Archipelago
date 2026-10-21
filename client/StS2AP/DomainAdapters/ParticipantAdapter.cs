using Microsoft.FSharp.Core;
using StS2AP.Domain;
using StS2AP.Utils;

namespace StS2AP.DomainAdapters;

/// <summary>
/// Projects transport fields for synchronous validation without copying settings or receipts.
/// ApRunData owns staging, sender authentication, and revalidation against the latest roster.
/// </summary>
internal static class ParticipantAdapter
{
    public static ContributionReadiness Evaluate(ApPlayerRunState? state)
    {
        if (state == null)
            return ContributionReadiness.Evaluate(ApRunData.RunSchemaVersion, null!);

        return ContributionReadiness.Evaluate(ApRunData.RunSchemaVersion, new ParticipantContributionInput
        {
            SchemaVersion = state.SchemaVersion,
            Participation = IdentityInput(state.Participation, state.ApRoomSeed, state.ApTeamId, state.ApSlotId),
            HasSettings = state.SlotSettings != null,
            ReceiptSourceReady = state.ReceiptSourceReady,
            RelicReceipts = state.InitialRelicReceiptIndexesByCharacter?.Select(
                pair => new KeyValuePair<long, IReadOnlyList<int>>(pair.Key, pair.Value))!,
            ProgressiveAncients = state.InitialProgressiveAncientsByCharacter!,
        });
    }

    public static string? Blocker(ContributionReadiness readiness) => readiness.Match<string?>(
        _ => null, waiting => waiting.Code, rejected => $"{rejected.Code}: {rejected.Description}");

    public static FSharpResult<ParticipantIdentity, ParticipantResumeError> MatchReturning(
        ApPlayerRunState saved, ApParticipationKind currentKind, ApSlotIdentity? currentSlot) =>
        ParticipantResume.Match(ApRunData.RunSchemaVersion, saved.SchemaVersion,
            IdentityInput(saved.Participation, saved.ApRoomSeed, saved.ApTeamId, saved.ApSlotId),
            IdentityInput(currentKind, currentSlot?.RoomSeed, currentSlot?.ApTeamId, currentSlot?.ApSlotId));

    private static ParticipationInput IdentityInput(
        ApParticipationKind kind, string? seed, int? team, int? slot) => new()
    {
        Kind = (int)kind, RoomSeed = seed!, ApTeamId = team, ApSlotId = slot,
    };
}
