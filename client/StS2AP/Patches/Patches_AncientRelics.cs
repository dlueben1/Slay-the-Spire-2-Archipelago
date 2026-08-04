using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Extensions;
using StS2AP.Models;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Patches
{
    [HarmonyPatch(typeof(AncientEventModel), "GenerateInitialOptionsWrapper")]
    public static class Patches_AncientRelics
    {
        [HarmonyPostfix]
        public static void ReplaceAncientOptions(AncientEventModel __instance, ref IReadOnlyList<EventOption> __result)
        {
            var player = GameUtility.CurrentPlayer;
            if (player == null)
                return;

            var currentAct = player.RunState.CurrentActIndex + 1;
            var maxAct = ArchipelagoClient.Progress.MaxAncientUnlock(player?.Character.GetCharacterOffset() ?? -1);
            var ancientChaos = ArchipelagoClient.Settings?.AncientChaos ?? AncientChaosMode.Balanced;
            var useProceedOnly = maxAct < currentAct ||
                                 (ancientChaos != AncientChaosMode.Balanced && currentAct is 2 or 3);
            if (useProceedOnly)
            {
                LogUtility.Info($"Replacing Ancient choices with Proceed; AncientChaos {ancientChaos} maxAct {maxAct} current act {currentAct}");
                __result = new List<EventOption> { CreateFakeOption(__instance) };
            }
        }

        [HarmonyPrefix]
        public static void SendAncientUnlockCheck()
        {

            var player = GameUtility.CurrentPlayer;
            if(player != null)
            {
                var currentAct = player.RunState.CurrentActIndex + 1;
                if(currentAct == 1 && !ArchipelagoClient.Settings.NeowSanity)
                {
                    return;
                }
                GameUtility.SendCheck($"{player.Character.APName()} Ancient Act {currentAct}");
            }
        }

        private static EventOption CreateFakeOption(AncientEventModel ancient)
        {
            return new EventOption(ancient,
                NEventRoom.Proceed,
                new MegaCrit.Sts2.Core.Localization.LocString("events", "AP_PROCEED.title"),
                new MegaCrit.Sts2.Core.Localization.LocString("events", "AP_PROCEED.description"),
                "AP_PROCEED", new List<IHoverTip>());
        }
    }
}
