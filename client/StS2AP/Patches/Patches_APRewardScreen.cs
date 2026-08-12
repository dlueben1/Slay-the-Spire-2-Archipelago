using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using StS2AP.UI;

namespace StS2AP.Patches
{
    /// <summary>
    /// AP's map hotkey is handled by APRewardScreenNode. A direct top-bar click
    /// calls Open with isOpenedFromTopBar=true, so defer only that user-driven path
    /// until AP has left the overlay stack.
    /// </summary>
    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
    public static class OpenMapAfterClosingAPRewards
    {
        [HarmonyPrefix]
        public static bool Prefix(
            NMapScreen __instance,
            bool isOpenedFromTopBar,
            ref NMapScreen __result)
        {
            if (!isOpenedFromTopBar || !ArchipelagoRewardUI.IsActive)
            {
                return true;
            }

            ArchipelagoRewardUI.CloseToMap();
            __result = __instance;
            return false;
        }
    }

    /// <summary>
    /// Apply the symmetric behaviour to deck requests. AP's blocker handles the
    /// equivalent hotkey while AP owns input; this catches the direct button path.
    /// </summary>
    [HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen.ShowScreen))]
    public static class OpenDeckAfterClosingAPRewards
    {
        [HarmonyPrefix]
        public static bool Prefix(ref NDeckViewScreen? __result)
        {
            if (!ArchipelagoRewardUI.IsActive)
            {
                return true;
            }

            ArchipelagoRewardUI.CloseToDeck();
            __result = null;
            return false;
        }
    }
}
