using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
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
    [HarmonyPatch(typeof(IroncladCardPool))]
    public static class IroncladCardPoolPatches
    {
        /// <summary>
        /// Unlock all Cards for the Ironclad
        /// </summary>
        [HarmonyPatch("FilterThroughEpochs")]
        [HarmonyPostfix]
        static void UnlockAllCards(ref IEnumerable<CardModel> __result, IroncladCardPool __instance)
        {
            __result = __instance.AllCards.ToList();
        }
    }

    [HarmonyPatch(typeof(SilentCardPool))]
    public static class SilentCardPoolPatches
    {
        /// <summary>
        /// Unlock all Cards for the Silent
        /// </summary>
        [HarmonyPatch("FilterThroughEpochs")]
        [HarmonyPostfix]
        static void UnlockAllCards(ref IEnumerable<CardModel> __result, SilentCardPool __instance)
        {
            __result = __instance.AllCards.ToList();
        }
    }

    [HarmonyPatch(typeof(DefectCardPool))]
    public static class DefectCardPoolPatches
    {
        /// <summary>
        /// Unlock all Cards for the Defect
        /// </summary>
        [HarmonyPatch("FilterThroughEpochs")]
        [HarmonyPostfix]
        static void UnlockAllCards(ref IEnumerable<CardModel> __result, DefectCardPool __instance)
        {
            __result = __instance.AllCards.ToList();
        }
    }

    [HarmonyPatch(typeof(RegentCardPool))]
    public static class RegentCardPoolPatches
    {
        /// <summary>
        /// Unlock all Cards for the Regent
        /// </summary>
        [HarmonyPatch("FilterThroughEpochs")]
        [HarmonyPostfix]
        static void UnlockAllCards(ref IEnumerable<CardModel> __result, RegentCardPool __instance)
        {
            __result = __instance.AllCards.ToList();
        }
    }

    [HarmonyPatch(typeof(NecrobinderCardPool))]
    public static class NecrobinderCardPoolPatches
    {
        /// <summary>
        /// Unlock all Cards for the Necrobinder
        /// </summary>
        [HarmonyPatch("FilterThroughEpochs")]
        [HarmonyPostfix]
        static void UnlockAllCards(ref IEnumerable<CardModel> __result, NecrobinderCardPool __instance)
        {
            __result = __instance.AllCards.ToList();
        }
    }
}
