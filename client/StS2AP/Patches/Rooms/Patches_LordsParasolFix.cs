using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Models;
using StS2AP.Multiplayer;
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

#if STS2_AT_LEAST_0_113_0
    /// <summary>
    /// From 0.113 every machine replays a player's Lord's Parasol against its own copy of that
    /// player's shop. Shop Sanity locks slots only on the owner's machine, so the other machines
    /// would buy the locked slots as well. A locked card removal, or a locked relic that asks for
    /// a pick when obtained, then leaves them waiting for a choice the owner never makes, and the
    /// run diverges. Until shop locks are shared between players, keep the relic out of AP
    /// multiplayer runs where any player has Shop Sanity. Ancients and the mod's own Ancient relic
    /// pool both ask IsAllowed, and every machine reads the same frozen slot settings.
    /// </summary>
    [HarmonyPatch(typeof(RelicModel), nameof(RelicModel.IsAllowed))]
    public static class KeepLordsParasolOutOfLockedMultiplayerShops
    {
        [HarmonyPostfix]
        public static void Postfix(RelicModel __instance, IRunState runState, ref bool __result)
        {
            if (!__result
                || __instance is not LordsParasol
                || !MultiplayerSupport.IsRealMultiplayerRun
                || !MultiplayerSupport.ShouldRunReplicatedConstruction(MultiplayerFeature.Shops))
            {
                return;
            }

            if (runState.Players.Any(player =>
                    ApPlayerContextResolver.TryGetRewardSettings(player, out ArchipelagoSettings settings)
                    && settings.ShopSanity))
            {
                __result = false;
            }
        }
    }
#endif

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
