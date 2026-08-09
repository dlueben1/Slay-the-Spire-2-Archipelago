using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using StS2AP.UI;

namespace StS2AP.Patches
{
    /// <summary>
    /// Keeps the AP reward overlay chain active when the map is also open. The
    /// base game normally gives the map priority over every overlay, which
    /// disables overlay focus and input even if an overlay is still visible.
    /// </summary>
    [HarmonyPatch(typeof(ActiveScreenContext), nameof(ActiveScreenContext.GetCurrentScreen))]
    public static class PreferAPRewardScreenOverMap
    {
        [HarmonyPostfix]
        public static void Postfix(ref IScreenContext? __result)
        {
            if (__result is NMapScreen &&
                ArchipelagoRewardUI.IsOpen &&
                NOverlayStack.Instance?.Peek() is IScreenContext overlayScreen)
            {
                __result = overlayScreen;
            }
        }
    }

    /// <summary>
    /// Reapplies the AP reward screen's visual and mouse-input priority when
    /// the player opens the map after the reward screen is already present.
    /// </summary>
    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
    public static class KeepNewlyOpenedMapBehindAPRewardScreen
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (ArchipelagoRewardUI.IsOpen)
            {
                ArchipelagoRewardUI.RaiseOverlayAboveMap();
            }
        }
    }
}
