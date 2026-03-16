using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Unlocks;
using System.Collections.Generic;

namespace StS2AP.Patches
{
    /// <summary>
    /// Forces all potions to always be available in the potion pool
    /// </summary>

    [HarmonyPatch(typeof(SharedPotionPool), "GetUnlockedPotions")]
    public static class Patches_PotionsPoolUnlock_Shared
    {
        /// <summary>
        /// Unlock all Shared Potions
        /// </summary>
        [HarmonyPostfix]
        static void UnlockAllPotions(ref IEnumerable<PotionModel> __result, SharedPotionPool __instance)
        {
            __result = __instance.AllPotions;
        }
    }

    [HarmonyPatch(typeof(IroncladPotionPool), "GetUnlockedPotions")]
    public static class Patches_PotionsPoolUnlock_Ironclad
    {
        /// <summary>
        /// Unlock all Potions for Ironchad
        /// </summary>
        [HarmonyPostfix]
        static void UnlockAllPotions(ref IEnumerable<PotionModel> __result, IroncladPotionPool __instance)
        {
            __result = __instance.AllPotions;
        }
    }

    [HarmonyPatch(typeof(SilentPotionPool), "GetUnlockedPotions")]
    public static class Patches_PotionsPoolUnlock_Silent
    {
        /// <summary>
        /// Unlock all Potions for Silent
        /// </summary>
        [HarmonyPostfix]
        static void UnlockAllPotions(ref IEnumerable<PotionModel> __result, SilentPotionPool __instance)
        {
            __result = __instance.AllPotions;
        }
    }

    [HarmonyPatch(typeof(DefectPotionPool), "GetUnlockedPotions")]
    public static class Patches_PotionsPoolUnlock_Defect
    {
        /// <summary>
        /// Unlock all Potions for Defect
        /// </summary>
        [HarmonyPostfix]
        static void UnlockAllPotions(ref IEnumerable<PotionModel> __result, DefectPotionPool __instance)
        {
            __result = __instance.AllPotions;
        }
    }

    [HarmonyPatch(typeof(NecrobinderPotionPool), "GetUnlockedPotions")]
    public static class Patches_PotionsPoolUnlock_Necrobinder
    {
        /// <summary>
        /// Unlock all Potions for Necrobinder
        /// </summary>
        [HarmonyPostfix]
        static void UnlockAllPotions(ref IEnumerable<PotionModel> __result, NecrobinderPotionPool __instance)
        {
            __result = __instance.AllPotions;
        }
    }

    [HarmonyPatch(typeof(RegentPotionPool), "GetUnlockedPotions")]
    public static class Patches_PotionsPoolUnlock_Regent
    {
        /// <summary>
        /// Unlock all Potions for Regent
        /// </summary>
        [HarmonyPostfix]
        static void UnlockAllPotions(ref IEnumerable<PotionModel> __result, RegentPotionPool __instance)
        {
            __result = __instance.AllPotions;
        }
    }
}