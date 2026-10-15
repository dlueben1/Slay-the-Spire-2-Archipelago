using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;

namespace StS2AP.Patches;

/// <summary>
/// Blacklists Lasting Candy from every relic source while the Archipelago mod is loaded.
/// Native grab bags and AP's stable relic selector both honor RelicModel.IsAllowed.
/// </summary>
[HarmonyPatch(typeof(LastingCandy), nameof(LastingCandy.IsAllowed))]
public static class Patches_LastingCandy
{
    [HarmonyPrefix]
    private static bool Prefix(ref bool __result)
    {
        __result = false;
        return false;
    }
}
