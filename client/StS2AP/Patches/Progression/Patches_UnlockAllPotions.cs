using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.PotionPools;

namespace StS2AP.Patches;

/// <summary>Removes unlock filtering from the playable and shared potion pools.</summary>
public static class Patches_UnlockAllPotions
{
    private static void Unlock(PotionPoolModel pool, ref IEnumerable<PotionModel> result) =>
        result = pool.AllPotions;

    [HarmonyPatch(typeof(SharedPotionPool), nameof(PotionPoolModel.GetUnlockedPotions))]
    private static class Shared
    {
        [HarmonyPostfix]
        private static void Postfix(
            SharedPotionPool __instance,
            ref IEnumerable<PotionModel> __result) => Unlock(__instance, ref __result);
    }

    [HarmonyPatch(typeof(IroncladPotionPool), nameof(PotionPoolModel.GetUnlockedPotions))]
    private static class Ironclad
    {
        [HarmonyPostfix]
        private static void Postfix(
            IroncladPotionPool __instance,
            ref IEnumerable<PotionModel> __result) => Unlock(__instance, ref __result);
    }

    [HarmonyPatch(typeof(SilentPotionPool), nameof(PotionPoolModel.GetUnlockedPotions))]
    private static class Silent
    {
        [HarmonyPostfix]
        private static void Postfix(
            SilentPotionPool __instance,
            ref IEnumerable<PotionModel> __result) => Unlock(__instance, ref __result);
    }

    [HarmonyPatch(typeof(DefectPotionPool), nameof(PotionPoolModel.GetUnlockedPotions))]
    private static class Defect
    {
        [HarmonyPostfix]
        private static void Postfix(
            DefectPotionPool __instance,
            ref IEnumerable<PotionModel> __result) => Unlock(__instance, ref __result);
    }

    [HarmonyPatch(typeof(NecrobinderPotionPool), nameof(PotionPoolModel.GetUnlockedPotions))]
    private static class Necrobinder
    {
        [HarmonyPostfix]
        private static void Postfix(
            NecrobinderPotionPool __instance,
            ref IEnumerable<PotionModel> __result) => Unlock(__instance, ref __result);
    }

    [HarmonyPatch(typeof(RegentPotionPool), nameof(PotionPoolModel.GetUnlockedPotions))]
    private static class Regent
    {
        [HarmonyPostfix]
        private static void Postfix(
            RegentPotionPool __instance,
            ref IEnumerable<PotionModel> __result) => Unlock(__instance, ref __result);
    }
}
