using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models.Relics;
using StS2AP.Utils;

namespace StS2AP.Patches
{
    [HarmonyPatch(typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper))]
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
    [HarmonyPatch(typeof(LordsParasol), "PurchaseEverything")]
    public static class BlockShopPageNavigationForLordsParasol
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            Patches_ShopPages.BeginNavigationBlock();
        }

        [HarmonyPostfix]
        public static void Postfix(ref Task __result)
        {
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
