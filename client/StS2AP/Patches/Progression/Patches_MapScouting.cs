using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.UI;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// The Map Sight bonus: the Scouting list under the map legend, and a tip naming what is inside
/// each room the player can move to next.
/// </summary>
public static class Patches_MapScouting
{
    /// <summary>The map opens after every room, so the list is rebuilt each time it opens.</summary>
    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
    public static class RefreshScoutingPanel
    {
        [HarmonyPostfix]
        public static void Postfix(NMapScreen __instance) => MapScoutingPanel.Refresh(__instance);
    }

    /// <summary>
    /// Rooms the player can move to next have no tip of their own, only visited rooms do, so this
    /// one never covers a game tip. Unfocusing the room already removes every tip it owns.
    /// </summary>
    [HarmonyPatch(typeof(NNormalMapPoint), nameof(NNormalMapPoint.OnFocus))]
    public static class ShowNextRoomTip
    {
        [HarmonyPostfix]
        public static void Postfix(NNormalMapPoint __instance)
        {
            if (__instance.State != MapPointState.Travelable
                || !MapScouting.IsActive
                || RunManager.Instance.State is not { } runState
                || MapScouting.DescribeNextRoom(runState, __instance.Point) is not { } contents)
                return;
            var tip = new HoverTip(new LocString("gameplay_ui", "AP_SCOUTING.tipTitle"), contents);
            NHoverTipSet? tipSet = NHoverTipSet.CreateAndShow(__instance, tip);
            if (tipSet == null)
                return;
            // As the game places a visited room's tip, once the tip has its size.
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(tipSet) && GodotObject.IsInstanceValid(__instance))
                    tipSet.SetAlignment(__instance, HoverTip.GetHoverTipAlignment(__instance));
            }).CallDeferred();
        }
    }
}
