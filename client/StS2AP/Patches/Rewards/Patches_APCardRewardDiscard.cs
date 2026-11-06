using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Rewards;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Adds Discard to AP menu card pickers when Strict Card Rewards is on. Skip still only backs
/// out of the picker, because the AP menu presses Skip itself when it closes for travel or the
/// hotkey; Discard is the one way to give up a revealed reward without taking a card.
/// </summary>
[HarmonyPatch(typeof(CardRewardAlternative), nameof(CardRewardAlternative.Generate))]
public static class Patches_APCardRewardDiscard
{
    internal const string DiscardOptionId = "AP_DISCARD";

    [HarmonyPostfix]
    private static void Postfix(CardReward cardReward, ref IReadOnlyList<CardRewardAlternative> __result)
    {
        // The native picker lays out at most two alternatives (Generate throws above two).
        // Caveat, Pael's Wing: its Sacrifice fills the second slot, so the picker shows Skip and
        // Sacrifice but no Discard. Sacrifice also completes the reward without a card, so it is
        // the way out of a revealed reward, but every escape now advances Pael's Wing, which a
        // plain Discard would not. A modded relic that fills the slot with an alternative which
        // does not complete the reward would leave taking a card as the only way out.
        if (cardReward is not ApMirroredRewardDispatcher.ApNativeCardReward { OffersDiscard: true }
            || __result.Count >= 2)
            return;

        // Completing the reward with no card lets the AP reward commit its receipt as used.
        __result = __result
            .Append(new CardRewardAlternative(
                DiscardOptionId,
                PostAlternateCardRewardAction.EndSelectionAndCompleteReward))
            .ToList();
    }
}
