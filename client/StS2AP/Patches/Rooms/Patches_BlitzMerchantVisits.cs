#if STS2_AT_LEAST_0_113_0
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Opens the game's Blitz merchant in place of a normal shop for the shops
/// <see cref="BlitzMerchantVisits"/> picks. The replacement follows the Blitz branch of the game's
/// own MerchantRoom.EnterInternal, which the game takes only in a Blitz run.
/// </summary>
internal static class Patches_BlitzMerchantVisits
{
    [HarmonyPatch(typeof(MerchantRoom), nameof(MerchantRoom.EnterInternal))]
    private static class EnterAsBlitzMerchant
    {
        [HarmonyPrefix]
        private static bool Prefix(
            MerchantRoom __instance,
            IRunState? runState,
            bool isRestoringRoomStackBase,
            ref Task __result)
        {
            // The game refuses to rebuild a merchant from a room stack; leave that to its own code.
            if (isRestoringRoomStackBase || runState == null || !BlitzMerchantVisits.IsBlitzMerchant(runState))
                return true;

            __result = Enter(__instance, runState);
            return false;
        }

        private static async Task Enter(MerchantRoom room, IRunState runState)
        {
            // The game builds a Blitz inventory, and gives it its free picks, only in a Blitz run.
            // Every peer builds every player's inventory, in the same order, as the game does.
            var inventories = new List<MerchantInventory>();
            foreach (var player in runState.Players)
            {
                MerchantInventory inventory = MerchantInventory.CreateForBlitzMerchant(player);
                inventory.BlitzChoicesRemaining = BlitzMerchantVisits.FreePicks;
                // Shop Sanity checks are sold here as in any shop, on the same AP page. The
                // stock it rolls for them must be rolled for every player on every peer.
                Patches_ShopSanity.AttachApInventory(player, inventory);
                inventories.Add(inventory);
            }
            RunManager.Instance.MerchantSynchronizer.OnMerchantEntered(inventories);
            // Without Shop Sanity there is no AP page, and one left over from an earlier shop
            // must not take part. With it, the page is registered again when the room is ready.
            ShopPageUtility.Reset();
            LogUtility.Info(
                $"This shop is a Blitz merchant (act {runState.CurrentActIndex + 1}, floor {runState.ActFloor})."
            );

            if (!TestMode.IsOn)
            {
                BackgroundAssets backgroundAssets = runState.Act.GenerateBackgroundAssets(
                    NCombatRoom.GenerateBackgroundRngForCurrentPoint(runState));
                await PreloadManager.LoadRoomBlitzMerchantAssets(
                    backgroundAssets,
                    RunManager.Instance.MerchantSynchronizer.GetLocalInventory().BlitzEntries);
                NRun.Instance?.SetCurrentRoom(NBlitzMerchantRoom.Create(runState.Players, backgroundAssets));
            }
            await Hook.AfterRoomEntered(runState, room);
        }
    }
}
#endif
