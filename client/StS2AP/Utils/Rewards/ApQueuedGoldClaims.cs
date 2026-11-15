using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Players;
using StS2AP.Models;
using StS2AP.Multiplayer;
using StS2AP.Utils.RunGraph;

namespace StS2AP.Utils;

/// <summary>
/// AP gold as a queued claim (<see cref="ApQueuedClaims"/>). Its claim is validated against a
/// cursor only this action advances, and the granted amount is computed at execution from the
/// shared Ascensions, so removing Poverty while the menu is open no longer underpays.
/// </summary>
internal static class ApQueuedGoldClaims
{
    internal static readonly ApQueuedClaimKind<ApGoldClaimActionMessage> Kind = new(
        actionKey: "gold_claim",
        noun: "AP gold",
        apply: Apply,
        describe: static message => $"cursor {message.CursorBefore} + {message.SourceAmount} raw gold"
    );

    internal static async Task<bool> SelectRow(Guid claimId, ApGoldClaim claim, Player player)
    {
        ApQueuedClaimResult result = await ApQueuedClaims.SelectRow(
            Kind,
            claimId,
            player,
            () => new ApGoldClaimActionMessage
            {
                CursorBefore = claim.RedeemedRawAfter - claim.SourceAmount,
                SourceAmount = claim.SourceAmount,
            }
        );
        return result.Applied;
    }

    private static async Task<ApQueuedClaimResult> Apply(ApQueuedClaim<ApGoldClaimActionMessage> claim)
    {
        ApGoldClaimActionMessage message = claim.Message;
        bool poverty = claim.Shared.AscensionStateInitialized
            && claim.Shared.CurrentAscensions.Contains((int)AscensionLevel.Poverty);
        QueuedGoldClaimRule.Decision decision = QueuedGoldClaimRule.Evaluate(
            claim.OwnerState.QueuedGoldClaimCursor,
            message.CursorBefore,
            message.SourceAmount,
            poverty
        );
        if (!decision.Accepted)
        {
            return ApQueuedClaims.Refuse(
                Kind,
                message,
                claim.Player,
                decision.RefusalReason ?? "the claim was refused",
                "That AP gold was already claimed."
            );
        }

        // Advance the cursor before awaiting the wallet change, so nothing can apply it twice.
        ApRunData.RecordQueuedGoldClaim(claim.RunState, claim.Player.NetId, decision.CursorAfter);
        if (decision.GrantedAmount > 0)
            await PlayerCmd.GainGold(decision.GrantedAmount, claim.Player);
        ApQueuedClaims.LogApplied(Kind, message, claim.Player,
            $"{decision.GrantedAmount} gold, raw cursor -> {decision.CursorAfter}, poverty={poverty}");

        if (claim.IsLocalOwner)
            CommitOwnerBookkeeping(message, decision, claim.Player);
        return new ApQueuedClaimResult(true);
    }

    /// <summary>
    /// Owner-only: advances the AP source cursor that later menus offer gold from. Every peer
    /// has already applied the same wallet change, so a mismatch here stops further claims
    /// rather than undoing anything.
    /// </summary>
    private static void CommitOwnerBookkeeping(
        ApGoldClaimActionMessage message,
        QueuedGoldClaimRule.Decision decision,
        Player player)
    {
        ArchipelagoProgress progress = ArchipelagoClient.Progress;
        if (progress.GoldRedeemed != message.CursorBefore)
        {
            LogUtility.Error(
                $"Queued AP gold claim {message.ClaimId} started at raw cursor {message.CursorBefore}, "
                    + $"but this client's cursor is {progress.GoldRedeemed}."
            );
            MultiplayerSupport.InvalidateRunClaims("a queued AP gold claim did not match the local gold cursor");
        }
        progress.GoldRedeemed = Math.Max(progress.GoldRedeemed, decision.CursorAfter);
        ApRunGraphRecorder.RecordGoldClaim(decision.GrantedAmount);
        if (!ApRunData.PublishLocalProgress(player))
        {
            MultiplayerSupport.InvalidateRunClaims(
                "the AP gold cursor could not be published after a queued gold claim"
            );
        }
    }
}
