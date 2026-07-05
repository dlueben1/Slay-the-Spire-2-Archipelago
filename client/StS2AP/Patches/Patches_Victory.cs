using Godot;
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
    /// <summary>
    /// Collection of Patches that apply when a run is won
    /// </summary>
    public static class Patches_Victory
    {
        /// <summary>
        /// Runs when a run is won. Used to update Goal Progress and release any remaining checks for a given character.
        /// </summary>
        [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded), new Type[] {typeof(bool)})]
        public static class OnEnded
        {
            [HarmonyPostfix]
            public static void Postfix(bool isVictory)
            {
                // Only run if the player cleared their run, not if they died or quit
                if(isVictory)
                {
                    /// This function does three things:
                    /// 1. Marks this character as having cleared a run, which is used for goal tracking
                    /// 2. Releases any checks that are still locked for this character, since the player has now achieved victory with them
                    /// 3. Determines if the player has met their goal, and if so, marks the player as having achieved their goal
                    Callable.From(() => _ = GameUtility.TrySetGoalAchieved()).CallDeferred();
                }
            }
        }
    }
}
