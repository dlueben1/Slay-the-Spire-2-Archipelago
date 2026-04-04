using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using StS2AP.Utils;
using System;
using System.Linq;

namespace StS2AP.Patches
{
    /// <summary>
    /// Patches for managing the current player reference and run lifecycle events for Archipelago.
    /// </summary>
    public static class Patches_HookRunStart
    {
        /// <summary>
        /// Does a bunch of work we need when a run starts, including caching references, resetting game state/progress, and hooking event listeners.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.CreateForNewRun), new Type[] { typeof(CharacterModel), typeof(UnlockState), typeof(ulong) })]
        public static class OnRunStart
        {
            [HarmonyPostfix]
            public static void Postfix(Player __result)
            {
                // Grab a reference to the current player
                GameUtility.CurrentPlayer = __result;

                // Reset progress
                ArchipelagoClient.Progress.InitializeTrackers(__result);

                // At start of game, listen to Combat Manager
                //CombatManager.Instance.CombatWon -= GameUtility.OnCombatWin;
                //CombatManager.Instance.CombatWon += GameUtility.OnCombatWin;

                // Send "Press Start" check
                GameUtility.TrySendPressStartCheck();

                // Clear buffers
                ArchipelagoClient.Progress.UsedItems.Clear();
            }
        }

        /// <summary>
        /// Clears the CurrentPlayer reference when the run ends to avoid stale state.
        /// </summary>
        [HarmonyPatch(typeof(NGame), nameof(NGame.ReturnToMainMenu))]
        public static class ClearPlayerOnReturnToMenu
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                GameUtility.CurrentPlayer = null;
                LogUtility.Info("CurrentPlayer cleared (returned to main menu)");
            }
        }
    }
}
