using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace StS2AP.Patches;

/// <summary>AP snapshots load directly; native reload counters and cleanup must not touch current_run.save.</summary>
internal static class Patches_SingleplayerSaveIsolation
{
    [HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.SaveRun),
        new[] { typeof(SerializableRun), typeof(bool) })]
    private static class NativeWrite
    {
        [HarmonyPrefix]
        private static bool Prefix(bool isMultiplayer, ref Task __result)
        {
            if (isMultiplayer || !ApSingleplayerSaves.IsHandlingSingleplayerRun) return true;
            __result = Task.CompletedTask;
            return false;
        }
    }

    [HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.DeleteCurrentRun))]
    private static class NativeDelete
    {
        [HarmonyPrefix]
        private static bool Prefix() => !ApSingleplayerSaves.IsHandlingSingleplayerRun;
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    private static class ClearAfterCleanup
    {
        [HarmonyPostfix]
        private static void Postfix() => ApSingleplayerSaves.ClearSelection();
    }
}
