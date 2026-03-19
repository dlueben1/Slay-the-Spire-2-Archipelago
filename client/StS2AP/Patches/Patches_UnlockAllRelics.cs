using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using System.Reflection;

namespace StS2AP.Patches
{
    /// <summary>
    /// Forces all relics to always be available in the relic pool for all characters.
    /// </summary>
    [HarmonyPatch]
    public static class Patches_UnlockAllRelics
    {
        /// <summary>
        /// List of each character's Relic Pool, including the Shared Relic Pool.
        /// </summary>
        private static readonly Type[] RelicPoolTypes =
        [
            typeof(SharedRelicPool),
            typeof(IroncladRelicPool),
            typeof(SilentRelicPool),
            typeof(DefectRelicPool),
            typeof(NecrobinderRelicPool),
            typeof(RegentRelicPool)
        ];

        /// <summary>
        /// Identifies all the `GetUnlockedRelics` methods from each relic pool class that should be patched.
        /// Harmony will apply the postfix patch to each of these methods.
        /// </summary>
        /// <returns>An enumerable of MethodBase objects representing each GetUnlockedRelics method to patch.</returns>
        [HarmonyTargetMethods]
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var type in RelicPoolTypes)
            {
                var method = AccessTools.Method(type, "GetUnlockedRelics");
                if (method != null)
                {
                    yield return method;
                }
            }
        }

        /// <summary>
        /// Postfix patch that replaces the list of unlocked relics with all available relics from the pool.
        /// This is applied to all character relic pools, ensuring every relic is available regardless of unlock status.
        /// </summary>
        /// <param name="__result">The original result from GetUnlockedRelics, which is replaced with all relics.</param>
        /// <param name="__instance">The relic pool instance being patched (can be any of the pool types).</param>
        [HarmonyPostfix]
        static void UnlockAllRelics(ref IEnumerable<RelicModel> __result, object __instance)
        {
            // Use reflection to get AllRelics from whatever pool type this is
            var allRelicsProperty = AccessTools.Property(__instance.GetType(), "AllRelics");
            if (allRelicsProperty != null)
            {
                __result = (IEnumerable<RelicModel>)allRelicsProperty.GetValue(__instance);
            }
        }
    }
}
