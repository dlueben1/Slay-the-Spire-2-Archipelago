using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
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
}
