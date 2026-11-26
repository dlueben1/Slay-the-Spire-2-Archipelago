using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Lets Winged Boots share free moves with AP's free-travel relics (<see cref="FreeTravelUses"/>):
/// when the player also holds one of them, Winged Boots spends a use only on the moves it pays for.
/// Without an AP free-travel relic, Winged Boots behaves as in the base game.
/// </summary>
[HarmonyPatch(typeof(WingedBoots), nameof(WingedBoots.AfterRoomEntered))]
public static class Patches_SharedFreeTravel
{
    [HarmonyPrefix]
    public static bool Prefix(WingedBoots __instance, ref Task __result)
    {
        if (!FreeTravelUses.HasApFreeTravelRelic(__instance.Owner) || FreeTravelUses.ShouldSpend(__instance))
            return true;
        __result = Task.CompletedTask;
        return false;
    }
}
