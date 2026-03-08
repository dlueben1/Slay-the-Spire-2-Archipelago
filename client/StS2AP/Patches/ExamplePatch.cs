using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Patches
{
    [HarmonyPatch(typeof(Player), nameof(Player.CreateForNewRun),
        new Type[] { typeof(CharacterModel), typeof(UnlockState), typeof(ulong) })]
    public class ExamplePatch
    {
        static void Postfix(Player __result)
        {
            // Give 999 gold at the start of the run
            LogUtility.Info("Test Log");
            __result.Gold = 999;
        }
    }
}
