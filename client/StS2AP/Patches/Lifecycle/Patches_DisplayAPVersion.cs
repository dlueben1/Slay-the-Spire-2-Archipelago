using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Debug;

namespace StS2AP.Patches
{
    /// <summary>
    /// Adds the Archipelago Version to the game's version info overlay
    /// </summary>
    [HarmonyPatch(typeof(NDebugInfoLabelManager), "UpdateText")]
    public static class Patches_DisplayAPVersion
    {
        // Set by the loader when no build exists for this game version's line and it fell
        // back to an older one.
        private const string UnverifiedBuildKey = "StS2AP.UnverifiedBuildTarget";

        [HarmonyPostfix]
        public static void Postfix(NDebugInfoLabelManager __instance)
        {
            if (__instance._releaseInfo != null)
            {
                // Add the Archipelago mod version as a new line
                __instance._releaseInfo.Text += $"\nArchipelago Mod {ArchipelagoClient.Version}";
                if (AppContext.GetData(UnverifiedBuildKey) is string buildTarget)
                    __instance._releaseInfo.Text += $" (v{buildTarget} build, untested on this game version)";
            }
        }
    }
}
