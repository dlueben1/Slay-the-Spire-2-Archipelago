using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.PotionPools;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace StS2AP.Patches
{
    /// <summary>
    /// Forces all potions to always be available in the potion pool for all characters.
    /// </summary>
    [HarmonyPatch]
    public static class Patches_UnlockAllPotions
    {
        /// <summary>
        /// List of each character's Potion Pool, including the Shared Potion Pool.
        /// </summary>
        private static readonly Type[] PotionPoolTypes =
        [
            typeof(SharedPotionPool),
            typeof(IroncladPotionPool),
            typeof(SilentPotionPool),
            typeof(DefectPotionPool),
            typeof(NecrobinderPotionPool),
            typeof(RegentPotionPool)
        ];

        /// <summary>
        /// Identifies all the `GetUnlockedPotions` methods from each potion pool class that should be patched.
        /// Harmony will apply the postfix patch to each of these methods.
        /// </summary>
        /// <returns>An enumerable of MethodBase objects representing each GetUnlockedPotions method to patch.</returns>
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var type in PotionPoolTypes)
            {
                var method = AccessTools.Method(type, "GetUnlockedPotions");
                if (method != null)
                {
                    yield return method;
                }
            }
        }

        /// <summary>
        /// Postfix patch that replaces the list of unlocked potions with all available potions from the pool.
        /// This is applied to all character potion pools, ensuring every potion is available regardless of unlock status.
        /// </summary>
        /// <param name="__result">The original result from GetUnlockedPotions, which is replaced with all potions.</param>
        /// <param name="__instance">The potion pool instance being patched (can be any of the pool types).</param>
        [HarmonyPostfix]
        static void UnlockAllPotions(ref IEnumerable<PotionModel> __result, object __instance)
        {
            // Use reflection to get AllPotions from whatever pool type this is
            var allPotionsProperty = AccessTools.Property(__instance.GetType(), "AllPotions");
            if (allPotionsProperty != null)
            {
                __result = (IEnumerable<PotionModel>)allPotionsProperty.GetValue(__instance);
            }
        }
    }
}