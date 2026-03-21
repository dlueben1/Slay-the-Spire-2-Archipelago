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
            // Use Traverse to access private fields more easily
            var releaseInfo = Traverse.Create(__instance).Field("_releaseInfo").GetValue();

            if (releaseInfo != null)
            {
                var textTraverse = Traverse.Create(releaseInfo).Property("Text");
                string currentText = textTraverse.GetValue<string>();

                // Add the Archipelago mod version as a new line
                textTraverse.SetValue(currentText + $"\nArchipelago Mod {ArchipelagoClient.Version}");
            }
        }
    }
}