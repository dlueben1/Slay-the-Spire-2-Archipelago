using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace StS2AP.Patches
{
    [HarmonyPatch(typeof(NAscensionPanel))]
    public static class NAscensionPanelPatches
    {
        /// <summary>
        /// Hides the Ascension Arrows from the UI during Character Select
        /// </summary>
        [HarmonyPatch("RefreshArrowVisibility")]
        [HarmonyPostfix]
        public static void HideAscensionArrows(NAscensionPanel __instance)
        {
            // Access Left/Right Ascension Modifying Arrows
            var leftField = AccessTools.Field(typeof(NAscensionPanel), "_leftArrow");
            var rightField = AccessTools.Field(typeof(NAscensionPanel), "_rightArrow");
            var leftObj = leftField?.GetValue(__instance) as Control;
            var rightObj = rightField?.GetValue(__instance) as Control;

            if (leftObj != null)
            {
                leftObj.Visible = false;
            }

            if (rightObj != null)
            {
                rightObj.Visible = false;
            }
        }

        /// <summary>
        /// Overrides the Max Ascension for the Character Select Screen UI
        /// </summary>
        [HarmonyPatch(nameof(NAscensionPanel.SetMaxAscension))]
        [HarmonyPrefix]
        public static void OverrideMaxAscension(ref int maxAscension)
        {
            maxAscension = ArchipelagoClient.Settings.AscensionLevel;
        }

        /// <summary>
        /// Overrides the Ascension for the Character Select Screen UI
        /// </summary>
        [HarmonyPatch(nameof(NAscensionPanel.SetAscensionLevel))]
        [HarmonyPrefix]
        public static void OverrideAscension(ref int ascension)
        {
            ascension = ArchipelagoClient.Settings.AscensionLevel;
        }
    }
}