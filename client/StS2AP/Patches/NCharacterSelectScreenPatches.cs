using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Saves;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Patches
{
    [HarmonyPatch(typeof(NCharacterSelectScreen))]
    public static class NCharacterSelectScreenPatches
    {
        /// <summary>
        /// Hides the Back Button from the Character Select Screen
        /// </summary>
        [HarmonyPatch("_Ready")]
        [HarmonyPostfix]
        static void HideBackButtonOnCharSelectScreen(NCharacterSelectScreen __instance)
        {
            __instance.GetNode<NBackButton>("BackButton").Visible = false;
        }
    }

    /// <summary>
    /// Disable the "Enable Tutorials?" dialog
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SeenFtue))]
    public static class DisableTutorialDialogPatch
    {
        [HarmonyPostfix]
        public static void Postfix(string ftueKey, ref bool __result)
        {
            // Always return true for the accept_tutorials_ftue check
            if (ftueKey == "accept_tutorials_ftue")
            {
                __result = true;
            }
        }
    }
}
