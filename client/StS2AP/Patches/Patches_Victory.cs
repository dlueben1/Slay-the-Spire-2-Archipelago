using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Patches
{
    public static class Patches_Victory
    {

        [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded), new Type[] {typeof(bool)})]
        public static class OnEnded
        {
            [HarmonyPostfix]
            public static void Postfix(bool isVictory)
            {
                if(isVictory)
                {
                    _ = GameUtility.TrySetGoalAchieved();
                }
            }
        }
    }
}
