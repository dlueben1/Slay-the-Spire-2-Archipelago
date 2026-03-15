using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
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
        /// Patches RunManager.ShouldApplyTutorialModifications to always return false,
        /// ensuring that tutorial discovery order modifications are never applied.
        /// </summary>
        [HarmonyPatch(typeof(RunManager), "ShouldApplyTutorialModifications")]
        public class DisableTutorialModificationsPatch
        {
            /// <summary>
            /// Skip the original function and set the result to false
            /// </summary>
            static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }
    }
}
