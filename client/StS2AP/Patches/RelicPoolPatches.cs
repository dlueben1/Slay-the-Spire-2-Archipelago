using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Saves;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Patches
{
    [HarmonyPatch(typeof(SharedRelicPool))]
    public static class SharedRelicPoolPatches
    {
        /// <summary>
        /// Unlock all Shared Relics
        /// </summary>
        [HarmonyPatch("GetUnlockedRelics")]
        [HarmonyPostfix]
        static void UnlockAllRelics(ref IEnumerable<RelicModel> __result, SharedRelicPool __instance)
        {
            __result = __instance.AllRelics.ToList();
        }
    }

    [HarmonyPatch(typeof(IroncladRelicPool))]
    public static class IroncladRelicPoolPatches
    {
        /// <summary>
        /// Unlock all Relics for the Ironclad
        /// </summary>
        [HarmonyPatch("GetUnlockedRelics")]
        [HarmonyPostfix]
        static void UnlockAllRelics(ref IEnumerable<RelicModel> __result, IroncladRelicPool __instance)
        {
            __result = __instance.AllRelics.ToList();
        }
    }

    [HarmonyPatch(typeof(DefectRelicPool))]
    public static class DefectRelicPoolPatches
    {
        /// <summary>
        /// Unlock all Relics for the Defect
        /// </summary>
        [HarmonyPatch("GetUnlockedRelics")]
        [HarmonyPostfix]
        static void UnlockAllRelics(ref IEnumerable<RelicModel> __result, DefectRelicPool __instance)
        {
            __result = __instance.AllRelics.ToList();
        }
    }

    [HarmonyPatch(typeof(NecrobinderRelicPool))]
    public static class NecrobinderRelicPoolPatches
    {
        /// <summary>
        /// Unlock all Relics for the Necrobinder
        /// </summary>
        [HarmonyPatch("GetUnlockedRelics")]
        [HarmonyPostfix]
        static void UnlockAllRelics(ref IEnumerable<RelicModel> __result, NecrobinderRelicPool __instance)
        {
            __result = __instance.AllRelics.ToList();
        }
    }

    [HarmonyPatch(typeof(RegentRelicPool))]
    public static class RegentRelicPoolPatches
    {
        /// <summary>
        /// Unlock all Relics for the Regent
        /// </summary>
        [HarmonyPatch("GetUnlockedRelics")]
        [HarmonyPostfix]
        static void UnlockAllRelics(ref IEnumerable<RelicModel> __result, RegentRelicPool __instance)
        {
            __result = __instance.AllRelics.ToList();
        }
    }

    [HarmonyPatch(typeof(SilentRelicPool))]
    public static class SilentRelicPoolPatches
    {
        /// <summary>
        /// Unlock all Relics for the Silent
        /// </summary>
        [HarmonyPatch("GetUnlockedRelics")]
        [HarmonyPostfix]
        static void UnlockAllRelics(ref IEnumerable<RelicModel> __result, SilentRelicPool __instance)
        {
            __result = __instance.AllRelics.ToList();
        }
    }
}
