using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Patches;

/// <summary>
/// Attaches AP's native message handler to each run's net service, the way the game's own
/// synchronizers register theirs when the run is set up, and detaches it when the run is cleaned up.
/// </summary>
internal static class Patches_ApNetMessages
{
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.InitializeShared))]
    private static class AttachToRun
    {
        [HarmonyPostfix]
        private static void Postfix(RunManager __instance) => ApNetMessages.Attach(__instance.NetService);
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    private static class DetachFromRun
    {
        [HarmonyPostfix]
        private static void Postfix() => ApNetMessages.Detach();
    }
}
