using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using StS2AP.UI;

namespace StS2AP.Patches
{
    /// <summary>
    /// Keeps the AP reward overlay active when the map is also open. The base
    /// game normally gives the map priority over every overlay, which disables
    /// overlay focus and input even if an overlay is still visible.
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
    /// Opening the map is an explicit request to leave AP rewards and view the
    /// map, so close the AP overlay instead of keeping it above the new map.
    /// </summary>
    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
    public static class CloseAPRewardScreenWhenMapOpens
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // If we're not not restoring the map behind rewards then this is a case
            // of you have the AP reward menu open but you hit the map button
            // so you close the menu
            // If we're restoring the map behind the rewards then this means we temporarily closed the map
            // to do the card reward shenanigans. In this case we actually want to return back to the AP menu
            // cus that was the thing before opening the card reward.
            if (ArchipelagoRewardUI.IsOpen &&
                !ArchipelagoRewardUI.IsRestoringMapBehindRewards)
            {
                ArchipelagoRewardUI.Hide();
            }
        }
    }

    /// <summary>
    /// The map button normally treats a map already visible behind AP rewards
    /// as a request to close it. While AP rewards are open, make the button take
    /// its open-map path instead; NMapScreen.Open then closes AP rewards.
    /// </summary>
    [HarmonyPatch(typeof(NTopBarMapButton), nameof(NTopBarMapButton.MethodName.IsOpen))]
    public static class TreatMapAsClosedBehindAPRewardScreen
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (ArchipelagoRewardUI.IsOpen)
            {
                __result = false;
            }
        }
    }
}
