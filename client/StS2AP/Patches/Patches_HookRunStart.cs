using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using StS2AP.Models;
using StS2AP.UI;
using StS2AP.Utils;
using System;
using System.Linq;
using System.Threading.Tasks;
using static Godot.HttpRequest;

namespace StS2AP.Patches
{



    /// <summary>
    /// Patches for managing the current player reference and run lifecycle events for Archipelago.
    /// </summary>
    public static class Patches_HookRunStart
    {
        ///<summary>
        /// Sets up the character config on run start
        /// </summary>
        [HarmonyPatch(typeof(NGame), nameof(NGame.StartNewSingleplayerRun))]
        public static class OnRunPreStart
        {
            [HarmonyPrefix]
            public static void Prefix(CharacterModel character, ref int ascensionLevel, ref string seed)
            {
                var officialName = character.Id.Entry;
                GameUtility.CurrentConfig = ArchipelagoClient.Settings.Characters[officialName];
                if(GameUtility.CurrentConfig.Ascension.Count == 0)
                {
                    ascensionLevel = 0;
                }
                else
                {
                    // Not 100% sure this is correct, but in testing this didn't have a negative impact.
                    ascensionLevel = 10;
                }
                var configuredSeed = GameUtility.CurrentConfig.Seed;
                if(configuredSeed != null && configuredSeed.Length > 0)
                {
                    seed = configuredSeed;
                }
            }
        }
        /// <summary>
        /// Does a bunch of work we need when a run starts, including caching references, resetting game state/progress, and hooking event listeners.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.CreateForNewRun), new Type[] { typeof(CharacterModel), typeof(UnlockState), typeof(ulong) })]
        public static class OnRunStart
        {
            [HarmonyPostfix]
            public static void Postfix(Player __result)
            {
                // Get rid of the tracker UI
                ArchipelagoCharTrackerUI.RemoveUI();
                ArchipelagoGoalTrackerUI.RemoveUI();

                // Grab a reference to the current player
                GameUtility.CurrentPlayer = __result;

                // Reset progress
                ArchipelagoClient.Progress.InitializeTrackers(__result);

                // Relic Coupons is a presentation-only starting relic backed by the saved bank.
                RelicCoupons.EnsureOwnedBy(__result);

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
        /// Reconciles progressive starters after the base game has finalized starting relic effects,
        /// but before it launches the run scene. At this point each player has a real RunState and
        /// relic removal can pair AfterRemoved with the base game's completed AfterObtained call.
        /// </summary>
        [HarmonyPatch(typeof(RunManager), nameof(RunManager.FinalizeStartingRelics))]
        public static class OnStartingRelicsFinalized
        {
            [HarmonyPostfix]
            public static void Postfix(ref Task __result)
            {
                __result = ReconcileProgressiveStarters(__result);
            }

            private static async Task ReconcileProgressiveStarters(Task finalizeTask)
            {
                await finalizeTask;

                var player = GameUtility.CurrentPlayer;
                var runState = RunManager.Instance.DebugOnlyGetState();
                if (player == null || !ReferenceEquals(player.RunState, runState))
                    return;

                await ProgressiveStarterUtility.InitializeForRun(player);
            }
        }

        /// <summary>
        /// Similar to `OnRunStart` but only happens on loading a run. We don't have to initialize anything, but we still need to do some work.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.FromSerializable), new Type[] { typeof(SerializablePlayer) })]
        public static class OnRunLoad
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                // Get rid of the tracker UI
                ArchipelagoCharTrackerUI.RemoveUI();
                ArchipelagoGoalTrackerUI.RemoveUI();
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
                GameUtility.CurrentConfig = null;
                LogUtility.Info("CurrentPlayer cleared (returned to main menu)");
            }
        }
    }
}
