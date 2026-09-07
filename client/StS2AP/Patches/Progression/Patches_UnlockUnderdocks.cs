using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Acts;

namespace StS2AP.Patches
{
    /// <summary>
    /// Makes Underdocks available regardless of the local profile's Timeline progress.
    /// Archipelago runs should not depend on the vanilla Underdocks Epoch being revealed.
    /// </summary>
    [HarmonyPatch(typeof(Underdocks), nameof(Underdocks.IsUnlocked))]
    public static class Patches_UnlockUnderdocks
    {
        [HarmonyPostfix]
        static void UnlockUnderdocks(ref bool __result)
        {
            __result = true;
        }
    }
}
