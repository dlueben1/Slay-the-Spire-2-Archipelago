using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Extensions;
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
            List<EventOption> newResult = new List<EventOption>();
            var player = GameUtility.CurrentPlayer;
            var maxAct = ArchipelagoClient.Progress.MaxAncientUnlock(player?.Character.GetCharacterOffset() ?? -1);
            if (maxAct == null || player == null || maxAct < (player.RunState.CurrentActIndex + 1))
            {
                LogUtility.Info($"Not enough Ancient Unlocks for Act; replacing with fake options maxAct {maxAct} current act {(player?.RunState.CurrentActIndex ?? 0) + 1}");
                newResult.Add(CreateFakeOption(__instance));
                __result = newResult;
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
