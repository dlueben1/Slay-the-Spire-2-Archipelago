using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using System.Collections.Generic;

namespace StS2AP.Patches
{
    /// <summary>
    /// Forces all ancients to always appear at the start of a run
    /// Two patches are required(because the game has 2 checks):
    /// 1. SetStartedWithNeowFlag (RunManager) the real gate.
    ///    Normally sets StartedWithNeow = IsEpochRevealed(NeowEpoch), which is always false(because of Patches_PreventEpochTriggers.cs)
    ///    This controls whether the starting map point is Ancient or Monster, and whether the game navigates to the ancient room on act start
    ///    We force it to be always true.
    /// 2. GetUnlockedAncients (each act) the pool gate
    ///    Normally removes Neow from the pool if NeowEpoch is not revealed
    ///    causingRoomSet.Ancient to be null (empty pool passed to rng.NextItem)
    ///    We return AllAncients directly which bypasses the epoch check, note:requires more testing but I don't have the time sorry
    /// </summary>

    [HarmonyPatch(typeof(RunManager), "SetStartedWithNeowFlag")]
    public static class Patches_AncientsUnlock_Flag
    {
        [HarmonyPrefix]
        static bool AlwaysStartWithNeow(RunManager __instance)
        {
            var stateProperty = typeof(RunManager).GetProperty("State",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (stateProperty?.GetValue(__instance) is RunState runState)
            {
                runState.ExtraFields.StartedWithNeow = true;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Overgrowth), "GetUnlockedAncients")]
    public static class Patches_AncientsUnlock_Overgrowth
    {
        [HarmonyPostfix]
        static void UnlockAllAncients(ref IEnumerable<AncientEventModel> __result, Overgrowth __instance)
        {
            __result = __instance.AllAncients;
        }
    }

    [HarmonyPatch(typeof(Hive), "GetUnlockedAncients")]
    public static class Patches_AncientsUnlock_Hive
    {
        [HarmonyPostfix]
        static void UnlockAllAncients(ref IEnumerable<AncientEventModel> __result, Hive __instance)
        {
            __result = __instance.AllAncients;
        }
    }

    [HarmonyPatch(typeof(Glory), "GetUnlockedAncients")]
    public static class Patches_AncientsUnlock_Glory
    {
        [HarmonyPostfix]
        static void UnlockAllAncients(ref IEnumerable<AncientEventModel> __result, Glory __instance)
        {
            __result = __instance.AllAncients;
        }
    }

    [HarmonyPatch(typeof(Underdocks), "GetUnlockedAncients")]
    public static class Patches_AncientsUnlock_Underdocks
    {
        [HarmonyPostfix]
        static void UnlockAllAncients(ref IEnumerable<AncientEventModel> __result, Underdocks __instance)
        {
            __result = __instance.AllAncients;
        }
    }
}
