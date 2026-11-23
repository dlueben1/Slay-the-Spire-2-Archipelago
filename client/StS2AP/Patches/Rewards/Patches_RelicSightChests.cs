using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Puts back the chest relics a Relic Sight choice left behind. The extras are added where the
/// chest's AP Relic is paired, in <see cref="Patches_InjectAPRewards"/>.
/// </summary>
public static class Patches_RelicSightChests
{
    /// <summary>
    /// A pick arrives here through the action queue, before the chest awards it. Prefix, so the
    /// relic list still holds the picked index.
    /// </summary>
    [HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.OnPicked))]
    public static class ReturnUnpicked
    {
        [HarmonyPrefix]
        public static void Prefix(TreasureRoomRelicSynchronizer __instance, Player player, int? index) =>
            RelicSightChoices.OnChestPicked(__instance, index);
    }

    /// <summary>Leaving the room ends a skipped singleplayer chest without awarding anything.</summary>
    [HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.OnRoomExited))]
    public static class ReturnSkipped
    {
        [HarmonyPrefix]
        public static void Prefix(TreasureRoomRelicSynchronizer __instance) =>
            RelicSightChoices.OnChestRoomExited(__instance);
    }
}
