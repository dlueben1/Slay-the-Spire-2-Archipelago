using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// The Hidden Map challenge. Rooms show a closed eye until the player can move to them, so a hidden
/// room can't be mistaken for a real unknown room. The boss and Ancient icons stay visible, and so
/// does the row layout: experienced players can still guess the game's fixed rows, such as the
/// rest sites right before the boss.
/// </summary>
public static class Patches_HiddenMap
{
    private const string HiddenRoomIconPath = "res://images/ui/map/ap_hidden_room.png";

    /// <summary>
    /// UpdateIcon runs when a map point is created and each time its state changes, so a room
    /// shows its real icon as soon as it becomes reachable.
    /// </summary>
    [HarmonyPatch(typeof(NNormalMapPoint), nameof(NNormalMapPoint.UpdateIcon))]
    public static class HideRoomIcon
    {
        [HarmonyPostfix]
        public static void Postfix(NNormalMapPoint __instance)
        {
            if (!MapSight.IsHidden(__instance))
                return;
            __instance._icon.Texture = ResourceLoader.Load<Texture2D>(
                HiddenRoomIconPath, null, ResourceLoader.CacheMode.Reuse);
            // The hidden room icon is the same round shape as the unknown icon, so it shares that outline.
            __instance._outline.Texture = ResourceLoader.Load<Texture2D>(
                NNormalMapPoint.OutlinePath(NNormalMapPoint.IconName(MapPointType.Unknown)),
                null,
                ResourceLoader.CacheMode.Reuse);
        }
    }

    /// <summary>
    /// Hovering a map legend entry animates every room of that type, which would point out the
    /// hidden shops and elites. Hidden rooms match no legend entry.
    /// </summary>
    [HarmonyPatch(typeof(NNormalMapPoint), nameof(NNormalMapPoint.OnHighlightPointType))]
    public static class HideLegendHighlight
    {
        [HarmonyPrefix]
        public static bool Prefix(NNormalMapPoint __instance)
        {
            if (!MapSight.IsHidden(__instance))
                return true;
            __instance.AnimUnhover();
            return false;
        }
    }
}
