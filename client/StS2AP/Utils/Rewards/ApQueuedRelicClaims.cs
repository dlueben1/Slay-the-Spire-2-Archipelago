using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Models;
using StS2AP.Multiplayer;
using StS2AP.Persistence;

namespace StS2AP.Utils;

/// <summary>
/// Standard AP Relic receipts as queued claims (<see cref="ApQueuedClaims"/>). A relic comes from
/// the player's relic bag, so every peer must draw it at the same point in the run: before the
/// owner's menu opens, one <c>relic_assignment</c> action draws every relic the menu needs on every
/// peer and records it, and each <c>relic_claim</c> action then obtains a recorded relic.
/// </summary>
/// <remarks>
/// The assignment is recorded where the host's menu approval records it today: as a menu
/// reservation in <see cref="ApRelicReceiptState"/>, with its relic in
/// <see cref="ApRelicReceiptState.Claim.MenuRelicAssignment"/>. Chest decisions skip reserved
/// receipts, and bank reconciliation, reloads and reconnects read those reservations, so a queued
/// assignment behaves like an approved one there. The difference is who writes it: the action, on
/// every peer in queue order, instead of host replies that arrive whenever they arrive. A chest
/// decision is made during room entry inside the move action, so it is ordered against these
/// actions too.
/// </remarks>
internal static class ApQueuedRelicClaims
{
    internal static readonly ApQueuedClaimKind<ApRelicAssignmentActionMessage> AssignmentKind = new(
        actionKey: "relic_assignment",
        noun: "AP relic assignment",
        apply: ApplyAssignment,
        describe: static message => "receipts " + string.Join(", ", message.Receipts.Select(receipt =>
            receipt.RequiresBank ? $"{receipt.ReceivedItemIndex} (banked)" : $"{receipt.ReceivedItemIndex}")),
        waitsForMenuRow: false
    );

    internal static readonly ApQueuedClaimKind<ApReceiptClaimActionMessage> ClaimKind = new(
        actionKey: "relic_claim",
        noun: "AP relic",
        apply: ApplyClaim,
        describe: static message => $"receipt {message.ReceivedItemIndex}"
    );

    /// <summary>
    /// Owner only, as the menu opens: assigns relics to the menu's Relic receipts that have none
    /// yet and returns every receipt the menu can offer. The same receipts the host's approval
    /// takes today: the run's anytime relics, and waiting receipts up to the owner's banked natural
    /// relic rewards, without those a chest took.
    /// </summary>
    internal static async Task<IReadOnlySet<int>> PrepareMenu(Player owner)
    {
        if (owner.RunState is not RunState runState)
            throw new InvalidOperationException("AP relic assignment needs a native run state.");

        IReadOnlyList<int> candidates = RelicRewardUtility.GetMenuReservationCandidates(owner);
        List<ApRelicAssignmentRequest> missing = candidates
            .Where(index => !HasUsableAssignment(runState, owner, index) && !IsTakenElsewhere(runState, owner, index))
            .Order()
            .Select(index => new ApRelicAssignmentRequest
            {
                ReceivedItemIndex = index,
                RequiresBank = !RelicRewardUtility.IsAnytimeReceiptIndex(owner, index),
            })
            .ToList();

        if (missing.Count > 0)
        {
            ApQueuedClaimResult result = await ApQueuedClaims.RequestAndWait(
                AssignmentKind,
                owner,
                () => new ApRelicAssignmentActionMessage { Receipts = missing }
            );
            if (!result.Applied)
                throw new InvalidOperationException("The AP relic assignments for this menu did not execute.");
            if (RunManager.Instance.DebugOnlyGetState() != runState)
                throw new OperationCanceledException("Run changed during AP relic assignment.");
        }

        return candidates.Where(index => HasUsableAssignment(runState, owner, index)).ToHashSet();
    }

    /// <summary>The relic a queued menu row offers for this receipt, as its assignment recorded it.</summary>
    internal static string GetAssignedRelic(RunState runState, Player owner, int receivedItemIndex) =>
        RelicReceiptMultiplayer.State(runState).Find(owner.NetId, receivedItemIndex)?.MenuRelicAssignment
            ?? throw new InvalidOperationException(
                $"AP relic receipt {owner.NetId}:{receivedItemIndex} has no queued assignment.");

    internal static async Task<ApQueuedClaimResult> SelectRow(Guid claimId, int receivedItemIndex, Player player) =>
        await ApQueuedClaims.SelectRow(ClaimKind, claimId, player, () => new ApReceiptClaimActionMessage
        {
            ReceivedItemIndex = receivedItemIndex,
        });

    private static bool HasUsableAssignment(RunState runState, Player player, int receivedItemIndex) =>
        RelicReceiptMultiplayer.State(runState).Find(player.NetId, receivedItemIndex) is
        {
            Destination: ApRelicReceiptState.MenuDestination,
            Consumed: false,
            MenuRelicAssignment: not null,
        };

    // A chest reservation, or a receipt already used up.
    private static bool IsTakenElsewhere(RunState runState, Player player, int receivedItemIndex) =>
        RelicReceiptMultiplayer.State(runState).Find(player.NetId, receivedItemIndex) is { } claim
        && (claim.Destination != ApRelicReceiptState.MenuDestination || claim.Consumed);

    /// <summary>
    /// Every peer, in queue order: draws and records a relic for each requested receipt that is
    /// still free, with the same stable draw the owner made alone before
    /// (<see cref="ArchipelagoProgress.GetOrAssignRelicChoices"/>), and takes it out of the relic
    /// bags right away. A receipt that already has a relic keeps it, so reopening the menu never
    /// rerolls one.
    /// </summary>
    private static Task<ApQueuedClaimResult> ApplyAssignment(ApQueuedClaim<ApRelicAssignmentActionMessage> claim)
    {
        Player player = claim.Player;
        ApRelicReceiptState receipts = RelicReceiptMultiplayer.State(claim.RunState);

        // Relics already assigned and not yet claimed are excluded from new draws, as the owner's
        // own unclaimed assignments were.
        var reservedRelicIds = new HashSet<ModelId>();
        foreach ((int index, ApRelicReceiptState.Claim existing) in
                 receipts.Claims.GetValueOrDefault(player.NetId) ?? new Dictionary<int, ApRelicReceiptState.Claim>())
        {
            if (existing.Consumed || existing.MenuRelicAssignment == null)
                continue;
            try
            {
                reservedRelicIds.Add(ApMirroredRewardDispatcher.LoadRelic(existing.MenuRelicAssignment, $"{index}").Id);
            }
            catch (Exception ex)
            {
                // Same data and game on every peer, so every peer skips it alike.
                LogUtility.Warn($"Ignoring unreadable AP relic assignment {player.NetId}:{index}: {ex.Message}");
            }
        }

        var assigned = new List<(ApRelicAssignmentRequest Request, RelicModel Relic)>();
        foreach (ApRelicAssignmentRequest request in claim.Message.Receipts
                     .Where(request => request.ReceivedItemIndex >= 0)
                     .DistinctBy(request => request.ReceivedItemIndex)
                     .OrderBy(request => request.ReceivedItemIndex))
        {
            int index = request.ReceivedItemIndex;
            ApRelicReceiptState.Claim? existing = receipts.Find(player.NetId, index);
            if (ApQueuedClaims.IsReceiptClaimed(claim, index)
                || existing is { MenuRelicAssignment: not null }
                || (existing != null
                    && (existing.Destination != ApRelicReceiptState.MenuDestination || existing.Consumed)))
            {
                continue;
            }

            RelicModel relic = StandardRelicPool.CreateChoices(
                player,
                $"{player.NetId}:{index}",
                choiceCount: 1,
                reservedRelicIds
            )[0];
            StandardRelicPool.ReserveChoice(player, relic);
            string serialized = ApMirroredRewardDispatcher.SerializeRelic(relic);
            ApRunData.ModifyRelicReceipts(claim.RunState, state =>
            {
                state.TryReserve(player.NetId, index, ApRelicReceiptState.MenuDestination,
                    requiresBank: request.RequiresBank);
                state.AssignMenu(player.NetId, index, serialized);
            });
            reservedRelicIds.Add(relic.Id);
            assigned.Add((request, relic));
        }

        LogUtility.Info(
            $"Applied queued AP relic assignment {claim.Message.ClaimId} for player {player.NetId}: "
                + (assigned.Count == 0
                    ? "nothing new"
                    : string.Join(", ", assigned.Select(entry => $"{entry.Request.ReceivedItemIndex}={entry.Relic.Id}")))
        );
        if (claim.IsLocalOwner && assigned.Count > 0)
            CommitOwnerAssignments(player, assigned);
        return Task.FromResult(new ApQueuedClaimResult(true));
    }

    /// <summary>
    /// Owner only: what <see cref="RelicRewardUtility.ReconcileBankedRewards"/> does after it
    /// assigns a relic, so the owner's menu, badge, coupons and published progress see it. Each
    /// receipt past the anytime relics spends one banked natural relic reward.
    /// </summary>
    private static void CommitOwnerAssignments(
        Player player,
        IReadOnlyList<(ApRelicAssignmentRequest Request, RelicModel Relic)> assigned)
    {
        ArchipelagoProgress progress = ArchipelagoClient.Progress;
        foreach ((ApRelicAssignmentRequest request, RelicModel relic) in assigned)
        {
            // Like ReconcileBankedRewards, spend a bank only on a receipt not paired before.
            bool alreadyPaired = progress.RelicChoiceAssignments.ContainsKey(request.ReceivedItemIndex);
            progress.RelicChoiceAssignments[request.ReceivedItemIndex] = [relic];
            if (!request.RequiresBank || alreadyPaired)
                continue;
            if (progress.BankedRelicRewards > 0)
                progress.BankedRelicRewards--;
            else
                LogUtility.Warn(
                    $"Queued AP relic assignment for receipt {request.ReceivedItemIndex} found no banked relic reward to spend."
                );
            RelicCoupons.Activate(player);
        }
        if (!ApRunData.PublishLocalProgress(player))
            MultiplayerSupport.InvalidateRunClaims("queued AP relic assignments could not be published");
    }

    private static async Task<ApQueuedClaimResult> ApplyClaim(ApQueuedClaim<ApReceiptClaimActionMessage> claim)
    {
        ApReceiptClaimActionMessage message = claim.Message;
        Player player = claim.Player;
        int index = message.ReceivedItemIndex;
        ApRelicReceiptState.Claim? reservation = index < 0
            ? null
            : RelicReceiptMultiplayer.State(claim.RunState).Find(player.NetId, index);

        if (index >= 0 && (ApQueuedClaims.IsReceiptClaimed(claim, index) || reservation is { Consumed: true }))
        {
            return ApQueuedClaims.Refuse(ClaimKind, message, player,
                "its receipt was already claimed", "That AP relic was already claimed.");
        }
        string? refusal = index < 0 ? "it names no receipt"
            : reservation is { Destination: not ApRelicReceiptState.MenuDestination }
                ? $"a chest decision reserved its receipt ({reservation.Destination})"
            : reservation?.MenuRelicAssignment == null ? "its receipt has no assigned relic"
            : null;
        if (refusal != null)
            return ApQueuedClaims.Refuse(ClaimKind, message, player, refusal, "Could not claim that AP relic.");

        RelicModel relic;
        try
        {
            relic = ApMirroredRewardDispatcher.LoadRelic(reservation!.MenuRelicAssignment!, $"{message.ApSlotId}:{index}");
        }
        catch (Exception ex)
        {
            return ApQueuedClaims.Refuse(ClaimKind, message, player,
                $"its relic did not load ({ex.Message})", "Could not claim that AP relic.");
        }

        ApRunData.ModifyRelicReceipts(claim.RunState, state =>
            state.Consume(player.NetId, index, ApRelicReceiptState.MenuDestination));
        ApQueuedClaims.MarkReceiptClaimed(claim, index);
        RelicModel obtained = await RelicCmd.Obtain(relic, player);
        LogUtility.Info(
            $"Applied queued AP relic claim {message.ClaimId} for player {player.NetId}: "
                + $"receipt {index}, {obtained.Id}"
        );
        if (claim.IsLocalOwner)
            ApMirroredRewardDispatcher.CommitDiscreteReward(index, ApMirroredRewardKind.Relic);
        return new ApQueuedClaimResult(true, obtained);
    }
}
