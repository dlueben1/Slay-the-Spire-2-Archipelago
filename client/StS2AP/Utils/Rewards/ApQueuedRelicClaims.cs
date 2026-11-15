using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Models;
using StS2AP.Multiplayer;
using StS2AP.Persistence;

namespace StS2AP.Utils;

/// <summary>
/// Standard AP Relic and Progressive Ancient receipts as queued claims (<see cref="ApQueuedClaims"/>).
/// A relic comes from the player's relic bag, and preparing an Ancient choice can roll the player's
/// reward RNG (Dusty Tome picks its card there), so every peer must draw both at the same point in
/// the run: before the owner's menu opens, one <c>relic_assignment</c> action draws every relic and
/// Ancient choice the menu needs on every peer and records it. Each <c>relic_claim</c> or
/// <c>ancient_claim</c> action then obtains a recorded relic.
/// </summary>
/// <remarks>
/// The assignment is recorded as a menu reservation in <see cref="ApRelicReceiptState"/>, with its
/// relic in <see cref="ApRelicReceiptState.Claim.MenuRelicAssignment"/>. Natural relic rewards
/// (RelicReceiptMultiplayer) skip reserved receipts, and banks, reloads and reconnects read those
/// reservations. Only this action writes them, on every peer in queue order; natural relic
/// decisions are ordered against it too. Ancient choices have no such record, so they go to
/// <see cref="ApPlayerRunState.QueuedAncientChoices"/>.
/// </remarks>
internal static class ApQueuedRelicClaims
{
    internal static readonly ApQueuedClaimKind<ApRelicAssignmentActionMessage> AssignmentKind = new(
        actionKey: "relic_assignment",
        noun: "AP relic assignment",
        apply: ApplyAssignment,
        describe: static message => "relic receipts ["
            + string.Join(", ", message.Receipts.Select(receipt => receipt.ReceivedItemIndex))
            + "], Ancient receipts [" + string.Join(", ", message.Ancients.Select(ancient => ancient.ReceivedItemIndex)) + "]",
        notifiesOwnerOfRefusal: false
    );

    internal static readonly ApQueuedClaimKind<ApReceiptClaimActionMessage> ClaimKind = new(
        actionKey: "relic_claim",
        noun: "AP relic",
        apply: ApplyClaim,
        describe: static message => $"receipt {message.ReceivedItemIndex}"
    );

    internal static readonly ApQueuedClaimKind<ApAncientClaimActionMessage> AncientClaimKind = new(
        actionKey: "ancient_claim",
        noun: "AP Ancient relic",
        apply: ApplyAncientClaim,
        describe: static message => $"receipt {message.ReceivedItemIndex}, option {message.OptionIndex}"
    );

    /// <summary>
    /// Owner only, as the menu opens: assigns relics to the menu's Relic receipts and choices to its
    /// Progressive Ancient receipts that have none yet, and returns every Relic receipt the menu
    /// can offer: the run's anytime relics, and waiting receipts up to the player's banked natural
    /// relic rewards, without those a chest or Elite took.
    /// </summary>
    internal static async Task<IReadOnlySet<int>> PrepareMenu(Player owner)
    {
        if (owner.RunState is not RunState runState)
            throw new InvalidOperationException("AP relic assignment needs a native run state.");

        IReadOnlyList<int> candidates = RelicReceiptMultiplayer.GetMenuCandidates(owner);
        List<ApRelicAssignmentRequest> missing = candidates
            .Where(index => !HasUsableAssignment(runState, owner, index) && !IsTakenElsewhere(runState, owner, index))
            .Order()
            .Select(index => new ApRelicAssignmentRequest { ReceivedItemIndex = index })
            .ToList();

        ApRunData.TryGetPlayerState(runState, owner.NetId, out ApPlayerRunState ownerState);
        List<ApAncientAssignmentRequest> missingAncients = new();
        foreach (IndexedItemInfo receipt in ApMirroredRewardDispatcher.GetClaimableMenuReceipts(
                     owner, ApMirroredRewardKind.Ancient))
        {
            if (ownerState?.QueuedAncientChoices.ContainsKey(receipt.Index) == true)
                continue;
            // A surplus receipt has no recipe and stays an unavailable row, as it does today.
            if (ArchipelagoClient.Progress.TryGetAncientDrawRequest(
                    receipt.Index, owner, AncientChoiceKey(owner, receipt.Index), out var request))
                missingAncients.Add(request);
        }

        if (missing.Count > 0 || missingAncients.Count > 0)
        {
            ApQueuedClaimResult result = await ApQueuedClaims.RequestAndWait(
                AssignmentKind,
                owner,
                () => new ApRelicAssignmentActionMessage { Receipts = missing, Ancients = missingAncients }
            );
            if (!result.Applied)
                throw new InvalidOperationException("The AP relic assignments for this menu did not execute.");
            if (RunManager.Instance.DebugOnlyGetState() != runState)
                throw new OperationCanceledException("Run changed during AP relic assignment.");
        }

        return candidates.Where(index => HasUsableAssignment(runState, owner, index)).ToHashSet();
    }

    // The multiplayer Ancient choice key, as the menu used for its owner-drawn choices.
    internal static string AncientChoiceKey(Player owner, int receivedItemIndex) => $"{owner.NetId}:{receivedItemIndex}";

    /// <summary>The three relics a queued Ancient row offers, as the assignment action recorded them.</summary>
    internal static IReadOnlyList<string>? GetAssignedAncientChoices(RunState runState, Player owner, int receivedItemIndex) =>
        ApRunData.TryGetPlayerState(runState, owner.NetId, out ApPlayerRunState state)
        && state.QueuedAncientChoices.TryGetValue(receivedItemIndex, out List<string>? choices)
            ? choices
            : null;

    internal static Task<ApQueuedClaimResult> SelectAncientRow(
        int receivedItemIndex,
        int optionIndex,
        Player player) =>
        ApQueuedClaims.RequestAndWait(AncientClaimKind, player, () => new ApAncientClaimActionMessage
        {
            ReceivedItemIndex = receivedItemIndex,
            OptionIndex = optionIndex,
        });

    /// <summary>The relic a queued menu row offers for this receipt, as its assignment recorded it.</summary>
    internal static string GetAssignedRelic(RunState runState, Player owner, int receivedItemIndex) =>
        RelicReceiptMultiplayer.State(runState).Find(owner.NetId, receivedItemIndex)?.MenuRelicAssignment
            ?? throw new InvalidOperationException(
                $"AP relic receipt {owner.NetId}:{receivedItemIndex} has no queued assignment.");

    internal static Task<ApQueuedClaimResult> SelectRow(int receivedItemIndex, Player player) =>
        ApQueuedClaims.RequestAndWait(ClaimKind, player, () => new ApReceiptClaimActionMessage
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
    /// rerolls one. The receipt must be one of the player's Relic receipts in the ledger; one past
    /// the anytime relics spends a banked natural relic reward, and is skipped when none is left.
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

        var assigned = new List<(int Index, bool SpentBank, RelicModel Relic)>();
        var skipped = new List<string>();
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
            if (!RelicReceiptMultiplayer.IsRelicReceipt(player, claim.OwnerState, index))
            {
                skipped.Add($"{index} (not a Relic receipt in the ledger)");
                continue;
            }
            bool requiresBank = !RelicReceiptMultiplayer.IsAnytimeReceipt(player, claim.OwnerState, index);
            if (requiresBank && RelicReceiptMultiplayer.GetAvailableBanks(claim.RunState, player, claim.OwnerState) <= 0)
            {
                skipped.Add($"{index} (no banked natural relic reward)");
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
                    requiresBank: requiresBank);
                state.AssignMenu(player.NetId, index, serialized);
            });
            reservedRelicIds.Add(relic.Id);
            assigned.Add((index, requiresBank, relic));
        }

        List<(int Index, IReadOnlyList<RelicModel> Choices)> ancients = AssignAncients(claim);

        IEnumerable<string> outcome = assigned
            .Select(entry => $"{entry.Index}={entry.Relic.Id}{(entry.SpentBank ? " (banked)" : "")}")
            .Concat(ancients.Select(entry => $"{entry.Index}=[{string.Join(", ", entry.Choices.Select(relic => relic.Id))}]"))
            .Concat(skipped.Select(entry => $"skipped {entry}"));
        ApQueuedClaims.LogApplied(AssignmentKind, claim.Message, player,
            outcome.Any() ? string.Join(", ", outcome) : "nothing new");
        if (claim.IsLocalOwner && assigned.Count + ancients.Count > 0)
            CommitOwnerAssignments(claim.RunState, player, assigned, ancients);
        return Task.FromResult(new ApQueuedClaimResult(true));
    }

    /// <summary>
    /// Every peer: draws each requested Progressive Ancient's three choices from the owner's
    /// recipe, with the owner's slot settings rather than this peer's own. A receipt that already
    /// has choices keeps them; one with no valid set gets none and stays unavailable in the menu.
    /// </summary>
    private static List<(int Index, IReadOnlyList<RelicModel> Choices)> AssignAncients(
        ApQueuedClaim<ApRelicAssignmentActionMessage> claim)
    {
        Player player = claim.Player;
        var assigned = new List<(int Index, IReadOnlyList<RelicModel> Choices)>();
        if (claim.Message.Ancients.Count == 0)
            return assigned;

        // As the owner's own draws did, exclude every relic already offered by an Ancient receipt.
        var reservedRelicIds = new HashSet<ModelId>();
        foreach ((int index, List<string> choices) in claim.OwnerState.QueuedAncientChoices)
        {
            foreach (string choice in choices)
            {
                try
                {
                    reservedRelicIds.Add(ApMirroredRewardDispatcher.LoadRelic(choice, $"{index}").Id);
                }
                catch (Exception ex)
                {
                    LogUtility.Warn($"Ignoring unreadable AP Ancient choice {player.NetId}:{index}: {ex.Message}");
                }
            }
        }
        ArchipelagoSettings? settings = ApPlayerContextResolver.TryGetRewardSettings(player, out var resolved)
            ? resolved
            : null;

        foreach (ApAncientAssignmentRequest request in claim.Message.Ancients
                     .Where(request => request.ReceivedItemIndex >= 0)
                     .DistinctBy(request => request.ReceivedItemIndex)
                     .OrderBy(request => request.ReceivedItemIndex))
        {
            int index = request.ReceivedItemIndex;
            if (ApQueuedClaims.IsReceiptClaimed(claim, index) || claim.OwnerState.QueuedAncientChoices.ContainsKey(index))
                continue;

            IReadOnlyList<RelicModel> choices =
                ArchipelagoProgress.DrawAncientChoices(player, request, reservedRelicIds, settings);
            if (choices.Count != AncientRelicPool.ChoiceCount)
            {
                LogUtility.Warn($"No valid AP Ancient choices for receipt {player.NetId}:{index}.");
                continue;
            }
            ApRunData.RecordQueuedAncientChoices(claim.RunState, player.NetId, index,
                choices.Select(ApMirroredRewardDispatcher.SerializeRelic).ToList());
            reservedRelicIds.UnionWith(choices.Select(relic => relic.Id));
            assigned.Add((index, choices));
        }
        return assigned;
    }

    /// <summary>
    /// Owner only: shows the assignments in the owner's own AP progress, which its menu badge,
    /// Relic Coupons counter and checkpoint read. The banks come from the action-owned count.
    /// </summary>
    private static void CommitOwnerAssignments(
        RunState runState,
        Player player,
        IReadOnlyList<(int Index, bool SpentBank, RelicModel Relic)> assigned,
        IReadOnlyList<(int Index, IReadOnlyList<RelicModel> Choices)> ancients)
    {
        ArchipelagoProgress progress = ArchipelagoClient.Progress;
        foreach ((int index, _, RelicModel relic) in assigned)
            progress.RelicChoiceAssignments[index] = [relic];
        foreach ((int index, IReadOnlyList<RelicModel> choices) in ancients)
            progress.AncientRelicChoiceAssignments[index] = choices.ToList();
        RelicReceiptMultiplayer.MirrorOwner(runState, spentBank: assigned.Any(entry => entry.SpentBank));
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
        ApQueuedClaims.LogApplied(ClaimKind, message, player, $"{obtained.Id}");
        if (claim.IsLocalOwner)
            ApMirroredRewardDispatcher.CommitDiscreteReward(index, ApMirroredRewardKind.Relic);
        return new ApQueuedClaimResult(true, obtained);
    }

    private static async Task<ApQueuedClaimResult> ApplyAncientClaim(ApQueuedClaim<ApAncientClaimActionMessage> claim)
    {
        ApAncientClaimActionMessage message = claim.Message;
        Player player = claim.Player;
        int index = message.ReceivedItemIndex;
        if (index >= 0 && ApQueuedClaims.IsReceiptClaimed(claim, index))
        {
            return ApQueuedClaims.Refuse(AncientClaimKind, message, player,
                "its receipt was already claimed", "That AP Ancient relic was already claimed.");
        }
        List<string>? choices = index < 0 ? null : claim.OwnerState.QueuedAncientChoices.GetValueOrDefault(index);
        string? refusal = index < 0 ? "it names no receipt"
            : choices == null ? "its receipt has no assigned choices"
            : message.OptionIndex < 0 || message.OptionIndex >= choices.Count ? $"it picks option {message.OptionIndex} of {choices.Count}"
            : null;
        if (refusal != null)
            return ApQueuedClaims.Refuse(AncientClaimKind, message, player, refusal, "Could not claim that AP Ancient relic.");

        RelicModel relic;
        try
        {
            relic = ApMirroredRewardDispatcher.LoadRelic(choices![message.OptionIndex], $"{message.ApSlotId}:{index}");
        }
        catch (Exception ex)
        {
            return ApQueuedClaims.Refuse(AncientClaimKind, message, player,
                $"its relic did not load ({ex.Message})", "Could not claim that AP Ancient relic.");
        }
        // As the menu's Ancient rows do: some saved-property setters mark a fresh model Disabled.
        relic.Status = RelicStatus.Normal;

        ApQueuedClaims.MarkReceiptClaimed(claim, index);
        RelicModel obtained = await RelicCmd.Obtain(relic, player);
        ApQueuedClaims.LogApplied(AncientClaimKind, message, player, $"{obtained.Id}");
        if (claim.IsLocalOwner)
            ApMirroredRewardDispatcher.CommitDiscreteReward(index, ApMirroredRewardKind.Ancient);
        return new ApQueuedClaimResult(true, obtained);
    }
}
