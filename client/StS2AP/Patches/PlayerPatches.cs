using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Patches
{
    /// <summary>
    /// Patches for `Player`
    /// </summary>
    public static class PlayerPatches
    {
        /// <summary>
        /// Grabs a reference to the player character that we can access globally.
        /// The Player is the entry point to many things, such as giving gold, damage, rewards, etc. so having a reference to it is important.
        /// 
        /// Also, resets our progress (we need to clean this up when we refactor patches...)
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.CreateForNewRun),
            new Type[] { typeof(CharacterModel), typeof(UnlockState), typeof(ulong) })]
        public class CachePlayerPatch
        {
            static void Postfix(Player __result)
            {
                LogUtility.Debug("Caching Player");
                GameUtility.CurrentPlayer = __result;

                // Reset progress
                ArchipelagoClient.Progress.ResetTrackers();

                // At start of game, listen to Combat Manager
                CombatManager.Instance.CombatWon -= GameUtility.OnCombatWin;
                CombatManager.Instance.CombatWon += GameUtility.OnCombatWin;

                // Send "Press Start" check
                GameUtility.TrySendPressStartCheck();

                // Clear buffers
                ArchipelagoClient.Progress.UsedItems.Clear();
            }

            /// <summary>
            /// Override MaxAscensionWhenRunStarted with the Player's Ascension Level
            /// </summary>
            [HarmonyPatch(typeof(Player), nameof(Player.MaxAscensionWhenRunStarted), MethodType.Getter)]
        [HarmonyPostfix]
        public static void OverrideMaxAscensionWhenRunStarted(ref int __result)
        {
            __result = ArchipelagoClient.Settings?.AscensionLevel ?? __result;
        }
        }
    }
}
