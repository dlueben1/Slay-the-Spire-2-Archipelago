using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Patches
{
    public static class NGamePatches
    {
        private static bool _runStartedHooked = false;

        /// <summary>
        /// Subscribes to RunManager.RunStarted the first time a run begins
        /// This is the way I did it since RunManager.State is private :p
        /// </summary>
        public static void HookRunEvents()
        {
            if (_runStartedHooked) return;
            _runStartedHooked = true;

            RunManager.Instance.RunStarted += OnRunStarted;
            LogUtility.Info("Hooked RunManager.RunStarted for CurrentPlayer capture");
        }

        /// <summary>
        /// Fires when a run starts it captures the local player into GameUtility.CurrentPlayer
        /// so all grant methods (gold, relics, cards, potions) have a valid player reference
        /// </summary>
        private static void OnRunStarted(RunState runState)
        {
            try
            {
                var player = runState?.Players?.FirstOrDefault();
                if (player != null)
                {
                    GameUtility.CurrentPlayer = player;
                    LogUtility.Success($"CurrentPlayer captured: {player.Character?.Id}");
                }
                else
                {
                    LogUtility.Warn("OnRunStarted: could not find a player in RunState");
                }
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to capture CurrentPlayer: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears the CurrentPlayer reference when the run ends to avoid stale state.
        /// </summary>
        [HarmonyPatch(typeof(NGame), nameof(NGame.ReturnToMainMenu))]
        [HarmonyPostfix]
        static void ReturnToMainMenu_Postfix()
        {
            GameUtility.CurrentPlayer = null;
            LogUtility.Info("CurrentPlayer cleared (returned to main menu)");
        }
    }
}
