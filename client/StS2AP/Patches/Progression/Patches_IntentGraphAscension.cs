using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Patches;

public static class Patches_IntentGraphAscension
{
    private static FieldInfo? previewLevel;
    private static bool attempted;

    // Localization initializes after mod initializers on both supported game versions.
    // Waiting until this point also covers IntentGraph loading after Archipelago.
    [HarmonyPatch(typeof(LocManager), nameof(LocManager.Initialize))]
    public static class InstallAfterModsLoad
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (attempted)
                return;
            attempted = true;
            var type = AccessTools.TypeByName("IntentGraph2.Utils.Variable.VariableContext");
            if (type is null)
                return;

            try
            {
                // IntentGraph is optional, so resolve its API without requiring its DLL.
                var target = AccessTools.Method(type, "GetObjectVariable", [typeof(string)])
                    ?? throw new MissingMethodException(type.FullName, "GetObjectVariable");
                previewLevel = type.Assembly.GetType("IntentGraph2.Patches.HasAscensionLevelPatches")
                    ?.GetField("OverwriteAsensionLevel")
                    ?? throw new MissingFieldException("IntentGraph's Ascension preview field is missing.");
                var harmony = new Harmony("archipelago.patch.intentgraph");
                harmony.Patch(target,
                    postfix: new HarmonyMethod(typeof(Patches_IntentGraphAscension), nameof(ReadAscension)));
                // If IntentGraph patches this caller before AP patches HasLevel, the JIT can
                // inline the original A10 check. Override the caller as well as the graph value.
                harmony.Patch(AccessTools.Method(typeof(RunManager), nameof(RunManager.HasAscension)),
                    postfix: new HarmonyMethod(typeof(Patches_IntentGraphAscension), nameof(ReadCombatAscension)));
                LogUtility.Info("IntentGraph compatibility enabled for AP Ascension flags and graph conditions.");
            }
            catch (Exception ex)
            {
                previewLevel = null;
                LogUtility.Warn($"Could not enable optional IntentGraph Ascension compatibility: {ex.Message}");
            }
        }
    }

    private static void ReadAscension(string variableName, ref object? __result)
    {
        if (variableName != "ascension" || !ShouldUseApAscensions())
            return;

        // IntentGraph's current definitions only distinguish below A9 from A9+.
        // Query the live flag so A10 alone is not mistaken for Deadly Enemies.
        if (AscensionMultiplayer.TryHasLevel(AscensionLevel.DeadlyEnemies, out bool enabled))
            __result = enabled ? 9 : 0;
    }

    private static void ReadCombatAscension(AscensionLevel level, ref bool __result)
    {
        if (ShouldUseApAscensions())
            Patches_AscensionOverride.InGameAscensionOverride.Postfix(level, ref __result);
    }

    private static bool ShouldUseApAscensions() =>
        previewLevel is not null && previewLevel.GetValue(null) is null
        && RunManager.Instance.IsInProgress
        && (MultiplayerSupport.IsRealMultiplayerRun || GameUtility.CurrentConfig is not null);
}
