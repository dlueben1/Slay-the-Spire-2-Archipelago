using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models.Relics;
using StS2AP.Utils;

namespace StS2AP.Patches
{
    [HarmonyPatch(typeof(MerchantEntry), GameApiCompat.MerchantEntryPurchaseMethod)]
    public static class SkipUnstockedPurchaseAttempts
    {
        [HarmonyPrefix]
        public static bool Prefix(MerchantEntry __instance, ref Task<bool> __result)
        {
            if (!__instance.IsStocked)
            {
                LogUtility.Info($"ShopSanity: skipped force-purchase attempt on an empty/unstocked slot ({__instance.GetType().Name}), avoiding the FailureOutOfStock dialogue crash.");
                __result = Task.FromResult(false);
                return false; // Skip the original call.
            }
            return true;
        }
    }

    /// <summary>
    /// Lord's Parasol opens the vanilla inventory directly while it performs its
    /// asynchronous purchases. Keep the AP page navigation unavailable until that
    /// entire operation, including card removal, has completed.
    /// </summary>
    [HarmonyPatch(typeof(LordsParasol), nameof(LordsParasol.PurchaseEverything))]
    public static class BlockShopPageNavigationForLordsParasol
    {
        [HarmonyPrefix]
        public static void Prefix(LordsParasol __instance, out bool __state)
        {
            // From 0.113 every machine replays every player's Parasol; only the owner's own
            // shop pages are involved.
            __state = LocalContext.IsMe(__instance.Owner);
            if (__state)
                Patches_ShopPages.BeginNavigationBlock();
        }

        [HarmonyPostfix]
        public static void Postfix(ref Task __result, bool __state)
        {
            if (__state)
                __result = AwaitPurchaseCompletion(__result);
        }

        private static async Task AwaitPurchaseCompletion(Task purchaseTask)
        {
            try
            {
                await purchaseTask;
            }
            finally
            {
                Patches_ShopPages.EndNavigationBlock();
            }
        }
    }
}
