using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Patches
{
    public static class Patches_DisableTutorial
    {
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
}
