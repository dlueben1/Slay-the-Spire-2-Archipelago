using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Patches
{
    /// <summary>
    /// Neow offers its normal blessings only when a run has no modifiers; otherwise it offers
    /// just the modifiers' own Neow options. AP's Run Modifiers have none, so without this patch
    /// they would leave Neow with nothing to offer. While Neow builds its options and opening
    /// text, the run's modifier list leaves out AP modifiers.
    /// </summary>
    public static class Patches_NeowIgnoresApModifiers
    {
        [HarmonyPatch(typeof(Neow), "GenerateInitialOptions")]
        public static class InitialOptions
        {
            [HarmonyPrefix]
            public static void Prefix(Neow __instance, out IReadOnlyList<ModifierModel>? __state) =>
                __state = HideApModifiers(__instance);

            [HarmonyFinalizer]
            public static Exception? Finalizer(
                Exception? __exception,
                Neow __instance,
                IReadOnlyList<ModifierModel>? __state)
            {
                RestoreModifiers(__instance, __state);
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Neow), nameof(Neow.InitialDescription), MethodType.Getter)]
        public static class InitialDescription
        {
            [HarmonyPrefix]
            public static void Prefix(Neow __instance, out IReadOnlyList<ModifierModel>? __state) =>
                __state = HideApModifiers(__instance);

            [HarmonyFinalizer]
            public static Exception? Finalizer(
                Exception? __exception,
                Neow __instance,
                IReadOnlyList<ModifierModel>? __state)
            {
                RestoreModifiers(__instance, __state);
                return __exception;
            }
        }

        /// <summary>
        /// Removes AP modifiers from the run's list and returns the full list to restore, or
        /// null when there is nothing to hide.
        /// </summary>
        private static IReadOnlyList<ModifierModel>? HideApModifiers(Neow neow)
        {
            if (neow.Owner?.RunState is not RunState runState)
                return null;
            IReadOnlyList<ModifierModel> all = runState.Modifiers;
            if (!all.Any(ApRunModifiers.IsArchipelagoModifier))
                return null;
            runState.Modifiers = all
                .Where(modifier => !ApRunModifiers.IsArchipelagoModifier(modifier))
                .ToList();
            return all;
        }

        private static void RestoreModifiers(Neow neow, IReadOnlyList<ModifierModel>? all)
        {
            if (all != null && neow.Owner?.RunState is RunState runState)
                runState.Modifiers = all;
        }
    }
}
