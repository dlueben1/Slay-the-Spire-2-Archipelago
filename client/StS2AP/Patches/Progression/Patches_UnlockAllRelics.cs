using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace StS2AP.Patches;

/// <summary>Removes unlock filtering from the playable and shared relic pools.</summary>
public static class Patches_UnlockAllRelics
{
    private static void Unlock(RelicPoolModel pool, ref IEnumerable<RelicModel> result) =>
        result = pool.AllRelics;

    [HarmonyPatch(typeof(SharedRelicPool), nameof(RelicPoolModel.GetUnlockedRelics))]
    private static class Shared
    {
        [HarmonyPostfix]
        private static void Postfix(
            SharedRelicPool __instance,
            ref IEnumerable<RelicModel> __result) => Unlock(__instance, ref __result);
    }

    [HarmonyPatch(typeof(IroncladRelicPool), nameof(RelicPoolModel.GetUnlockedRelics))]
    private static class Ironclad
    {
        [HarmonyPostfix]
        private static void Postfix(
            IroncladRelicPool __instance,
            ref IEnumerable<RelicModel> __result) => Unlock(__instance, ref __result);
    }

    [HarmonyPatch(typeof(SilentRelicPool), nameof(RelicPoolModel.GetUnlockedRelics))]
    private static class Silent
    {
        [HarmonyPostfix]
        private static void Postfix(
            SilentRelicPool __instance,
            ref IEnumerable<RelicModel> __result) => Unlock(__instance, ref __result);
    }

    [HarmonyPatch(typeof(DefectRelicPool), nameof(RelicPoolModel.GetUnlockedRelics))]
    private static class Defect
    {
        [HarmonyPostfix]
        private static void Postfix(
            DefectRelicPool __instance,
            ref IEnumerable<RelicModel> __result) => Unlock(__instance, ref __result);
    }

    [HarmonyPatch(typeof(NecrobinderRelicPool), nameof(RelicPoolModel.GetUnlockedRelics))]
    private static class Necrobinder
    {
        [HarmonyPostfix]
        private static void Postfix(
            NecrobinderRelicPool __instance,
            ref IEnumerable<RelicModel> __result) => Unlock(__instance, ref __result);
    }

    [HarmonyPatch(typeof(RegentRelicPool), nameof(RelicPoolModel.GetUnlockedRelics))]
    private static class Regent
    {
        [HarmonyPostfix]
        private static void Postfix(
            RegentRelicPool __instance,
            ref IEnumerable<RelicModel> __result) => Unlock(__instance, ref __result);
    }
}
