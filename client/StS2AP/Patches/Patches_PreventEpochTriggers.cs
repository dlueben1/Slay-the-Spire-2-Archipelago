using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Timeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Patches
{
    public static class Patches_PreventEpochTriggers
    {
        [HarmonyPatch(typeof(ProgressSaveManager), "TryObtainEpochInternal")]
        public class BlockEpochUnlocksPatch
        {
            static bool Prefix(EpochModel epoch)
            {
                // Block all epoch unlocks
                return false;
            }
        }

        [HarmonyPatch(typeof(NMainMenu), "Create")]
        public class NeverOpenTimelinePatch
        {
            static void Prefix(ref bool openTimeline)
            {
                // Force openTimeline to always be false
                openTimeline = false;
            }
        }

        [HarmonyPatch(typeof(NGameOverScreen), "DiscoveredAnyEpochs")]
        public class SkipTimelineAfterRunPatch
        {
            static bool Prefix(ref bool __result)
            {
                // Always return false, preventing timeline from opening
                __result = false;

                // Skip the original method
                return false;
            }
        }
    }
}
