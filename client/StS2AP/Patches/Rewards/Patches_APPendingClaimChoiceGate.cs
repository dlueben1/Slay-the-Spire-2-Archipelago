using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using StS2AP.UI;

namespace StS2AP.Patches;

/// <summary>
/// Native choices outside the action queue that reserve player-choice ids, held back while this
/// player has a queued AP claim in flight (<see cref="ArchipelagoRewardUI.TryBlockNativeChoiceWhileClaimPending"/>).
/// Vanilla reward rows go through the existing reward-button gate. Each prefix ignores the click
/// the way vanilla ignores one on a locked or unclickable button.
/// </summary>
public static class Patches_APPendingClaimChoiceGate
{
    // Rest-site options: Smith picks a card, Mend reserves a choice itself.
    [HarmonyPatch(typeof(NRestSiteButton), nameof(NRestSiteButton.OnRelease))]
    private static class RestSiteOption
    {
        [HarmonyPrefix]
        private static bool Prefix() => !ArchipelagoRewardUI.TryBlockNativeChoiceWhileClaimPending();
    }

    // Event options can open card or relic selections.
    [HarmonyPatch(typeof(NEventOptionButton), nameof(NEventOptionButton.OnRelease))]
    private static class EventOption
    {
        [HarmonyPrefix]
        private static bool Prefix() => !ArchipelagoRewardUI.TryBlockNativeChoiceWhileClaimPending();
    }

    // Shop purchases: card removal picks a card, and a bought relic can ask for one.
    [HarmonyPatch(typeof(NMerchantSlot), nameof(NMerchantSlot.OnSelected))]
    private static class MerchantPurchase
    {
        [HarmonyPrefix]
        private static bool Prefix(ref Task __result)
        {
            if (!ArchipelagoRewardUI.TryBlockNativeChoiceWhileClaimPending())
                return true;
            __result = Task.CompletedTask;
            return false;
        }
    }
}
