using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;
using StS2AP.Models;

namespace StS2AP.Utils;

/// <summary>
/// AP receipts whose reward needs no choice and no draw from a shared pool, as queued claims
/// (<see cref="ApQueuedClaims"/>): potions, rolled with AP RNG when the owner built the menu, and
/// bonus wax relics, assigned from the slot's configuration. The payload carries the exact model
/// the row offered, so every peer obtains the same one.
/// </summary>
internal static class ApQueuedReceiptClaims
{
    internal static readonly ApQueuedClaimKind<ApReceiptClaimActionMessage> PotionKind = new(
        actionKey: "potion_claim",
        noun: "AP potion",
        apply: ApplyPotion,
        describe: Describe
    );

    internal static readonly ApQueuedClaimKind<ApReceiptClaimActionMessage> BonusRelicKind = new(
        actionKey: "bonus_relic_claim",
        noun: "AP bonus wax relic",
        apply: ApplyBonusRelic,
        describe: Describe
    );

    internal static Task<ApQueuedClaimResult> SelectPotionRow(
        Guid claimId,
        int receivedItemIndex,
        string serializedPotion,
        Player player) =>
        ApQueuedClaims.SelectRow(PotionKind, claimId, player, () => new ApReceiptClaimActionMessage
        {
            ReceivedItemIndex = receivedItemIndex,
            SerializedModel = serializedPotion,
        });

    internal static Task<ApQueuedClaimResult> SelectBonusRelicRow(
        Guid claimId,
        int receivedItemIndex,
        string serializedRelic,
        Player player) =>
        ApQueuedClaims.SelectRow(BonusRelicKind, claimId, player, () => new ApReceiptClaimActionMessage
        {
            ReceivedItemIndex = receivedItemIndex,
            SerializedModel = serializedRelic,
        });

    private static async Task<ApQueuedClaimResult> ApplyPotion(ApQueuedClaim<ApReceiptClaimActionMessage> claim)
    {
        if (!TryLoad(PotionKind, claim, ApMirroredRewardDispatcher.LoadPotion, out PotionModel potion, out ApQueuedClaimResult refused))
            return refused;

        ApReceiptClaimActionMessage message = claim.Message;
        PotionProcureResult procured = await PotionCmd.TryToProcure(potion, claim.Player);
        if (!procured.success && procured.failureReason == PotionProcureFailureReason.TooFull)
        {
            // As with a vanilla potion reward, a full belt leaves the reward on offer; the belt
            // flashes on the owner's screen. Every peer has the same belt, so all agree.
            LogUtility.Info(
                $"Queued AP potion claim {message.ClaimId} for player {claim.Player.NetId} found the "
                    + $"potion belt full; receipt {message.ReceivedItemIndex} stays unclaimed."
            );
            return ApQueuedClaimResult.NotApplied;
        }

        // Any other refusal (a relic that forbids potions) uses the reward up, as vanilla does.
        ApQueuedClaims.MarkReceiptClaimed(claim, message.ReceivedItemIndex);
        LogUtility.Info(
            $"Applied queued AP potion claim {message.ClaimId} for player {claim.Player.NetId}: "
                + $"receipt {message.ReceivedItemIndex}, {potion.Id}, procured={procured.success}"
        );
        if (claim.IsLocalOwner)
            ApMirroredRewardDispatcher.CommitDiscreteReward(message.ReceivedItemIndex, ApMirroredRewardKind.Potion);
        return new ApQueuedClaimResult(true, potion);
    }

    private static async Task<ApQueuedClaimResult> ApplyBonusRelic(ApQueuedClaim<ApReceiptClaimActionMessage> claim)
    {
        if (!TryLoad(BonusRelicKind, claim, ApMirroredRewardDispatcher.LoadRelic, out RelicModel relic, out ApQueuedClaimResult refused))
            return refused;

        // The wax cadence needs nothing here: every peer counts combats from the unmelted wax
        // relics a player holds at combat end, and this action decides when that relic arrives.
        ApReceiptClaimActionMessage message = claim.Message;
        ApQueuedClaims.MarkReceiptClaimed(claim, message.ReceivedItemIndex);
        RelicModel obtained = await RelicCmd.Obtain(relic, claim.Player);
        LogUtility.Info(
            $"Applied queued AP bonus wax relic claim {message.ClaimId} for player {claim.Player.NetId}: "
                + $"receipt {message.ReceivedItemIndex}, {obtained.Id}"
        );
        if (claim.IsLocalOwner)
            ApMirroredRewardDispatcher.CommitDiscreteReward(message.ReceivedItemIndex, ApMirroredRewardKind.Bonus);
        return new ApQueuedClaimResult(true, obtained);
    }

    /// <summary>
    /// The receipt checks potion and bonus relic claims share: the receipt is still unclaimed and
    /// its model loads to the same model on this game. Every peer runs the same game build, so a
    /// model that fails to load is refused everywhere.
    /// </summary>
    private static bool TryLoad<TModel>(
        ApQueuedClaimKind<ApReceiptClaimActionMessage> kind,
        ApQueuedClaim<ApReceiptClaimActionMessage> claim,
        Func<string, string, TModel> load,
        out TModel model,
        out ApQueuedClaimResult refused) where TModel : class
    {
        ApReceiptClaimActionMessage message = claim.Message;
        model = null!;
        refused = ApQueuedClaimResult.NotApplied;

        if (message.ReceivedItemIndex < 0)
        {
            refused = ApQueuedClaims.Refuse(kind, message, claim.Player,
                "it names no receipt", $"Could not claim that {kind.Noun}.");
            return false;
        }
        if (ApQueuedClaims.IsReceiptClaimed(claim, message.ReceivedItemIndex))
        {
            refused = ApQueuedClaims.Refuse(kind, message, claim.Player,
                "its receipt was already claimed", $"That {kind.Noun} was already claimed.");
            return false;
        }

        try
        {
            model = load(message.SerializedModel, $"{message.ApSlotId}:{message.ReceivedItemIndex}");
            return true;
        }
        catch (Exception ex)
        {
            refused = ApQueuedClaims.Refuse(kind, message, claim.Player,
                $"its model did not load ({ex.Message})", $"Could not claim that {kind.Noun}.");
            return false;
        }
    }

    private static string Describe(ApReceiptClaimActionMessage message) =>
        $"receipt {message.ReceivedItemIndex}";
}
