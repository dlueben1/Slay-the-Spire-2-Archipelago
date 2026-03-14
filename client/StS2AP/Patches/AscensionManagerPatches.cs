using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ascension;
using StS2AP;

namespace StS2AP.Patches
{
    [HarmonyPatch(typeof(AscensionManager))]
    public static class AscensionManagerPatches
    {
        private static readonly FieldInfo s_levelField =
            typeof(AscensionManager).GetField("_level", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Overrides the Ascension Level of an AscensionManager instance.
        /// Overrides the constructor that takes an AscensionLevel enum, which is used in the int copy constructor.
        /// </summary>
        [HarmonyPatch(MethodType.Constructor, new[] { typeof(int) })]
        [HarmonyPostfix]
        private static void Ascension_Int_Postfix(AscensionManager __instance, int level)
        {
            if (s_levelField == null || __instance == null) return;

            // Prefer the value from Archipelago settings if available; otherwise use original value
            var desired = ArchipelagoClient.Settings?.AscensionLevel ?? level;
            LogUtility.Debug($"Patching Ascension Level to {desired}");
            s_levelField.SetValue(__instance, desired);
        }

        /// <summary>
        /// Overrides the Ascension Level of an AscensionManager instance.
        /// Overrides the constructor that takes an AscensionLevel enum, which is used in the AscensionManager copy constructor.
        /// </summary>
        [HarmonyPatch(MethodType.Constructor, new[] { typeof(AscensionLevel) })]
        [HarmonyPostfix]
        private static void Ascension_Enum_Postfix(AscensionManager __instance, AscensionLevel level)
        {
            if (s_levelField == null || __instance == null) return;

            // Prefer the value from Archipelago settings if available; otherwise use original value
            var desired = ArchipelagoClient.Settings?.AscensionLevel ?? (int)level;
            LogUtility.Debug($"Patching Ascension Level to {desired}");
            s_levelField.SetValue(__instance, desired);
        }
    }
}