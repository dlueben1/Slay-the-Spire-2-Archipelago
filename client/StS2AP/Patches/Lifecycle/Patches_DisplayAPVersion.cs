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
        [HarmonyPostfix]
        public static void Postfix(NDebugInfoLabelManager __instance)
        {
            if (__instance._releaseInfo != null)
            {
                // Add the Archipelago mod version as a new line
                __instance._releaseInfo.Text += $"\nArchipelago Mod {ArchipelagoClient.Version}";
            }
        }
    }
}
