using System;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using StS2AP.Extensions;
using StS2AP.Models;
using StS2AP.UI;
using StS2AP.Utils;

namespace StS2AP.Patches
{
    /// <summary>
    /// Apparently, Wax Relics don't melt automatically - the melting and tracking of Wax Relics is handled by the Toy Box Relic!
    /// So, this is a collection of patches to do the following:
    /// - Prevent the Toy Box relic from controlling the melting of wax relics
    /// - Control the melting of Wax Relics manually from our mod
    /// </summary>
    public static class Patches_WaxRelics
    {
        #region Combat Tracking

        /// <summary>
        /// This matches how the Toy Box handles Wax Relics, and is (I presume) what users expect,
        /// but maybe one day we want to make this configurable...
        /// </summary>
        private const int CombatsBetweenMelts = 3;

        #endregion

        #region Helper Functions

        /// <summary>Returns whether a relic callback belongs to the active run.</summary>
        private static bool IsCurrentPlayer(Player player)
        {
            Player? currentPlayer = GameUtility.CurrentPlayer;
            return currentPlayer != null
                && ReferenceEquals(currentPlayer, player)
                && ReferenceEquals(player.RunState, RunManager.Instance.DebugOnlyGetState());
        }

        /// <summary>
        /// Processes wax relics after combat ends, melting them when appropriate.
        /// </summary>
        /// <param name="nativeTask">The original task to await.</param>
        /// <param name="runState">The current run state.</param>
        private static async Task ProcessWaxRelicsAfterCombatEnd(
            Task nativeTask,
            IRunState runState
        )
        {
            await nativeTask;

            Player? player = GameUtility.CurrentPlayer;
            if (player == null || !ReferenceEquals(player.RunState, runState))
            {
                return;
            }

            RelicModel? waxRelic = player.Relics.FirstOrDefault(relic =>
                relic != null && relic.IsWax && !relic.IsMelted
            );
            if (waxRelic == null)
            {
                ArchipelagoClient.Progress.CombatsSinceLastWaxMelt = 0;
                return;
            }

            ArchipelagoClient.Progress.CombatsSinceLastWaxMelt++;
            if (ArchipelagoClient.Progress.CombatsSinceLastWaxMelt < CombatsBetweenMelts)
            {
                return;
            }

            try
            {
                await RelicCmd.Melt(waxRelic);
                ArchipelagoClient.Progress.CombatsSinceLastWaxMelt = 0;
                await Cmd.CustomScaledWait(0.5f, 0.75f);
            }
            catch (Exception ex)
            {
                LogUtility.Warn($"Failed to melt a wax relic after combat: {ex.Message}");
            }
        }

        #endregion

        #region Harmony Patches - ToyBox

        /// <summary>
        /// Prevents the Toy Box relic from controlling the melting of Wax Relics.
        /// </summary>
        [HarmonyPatch(typeof(ToyBox), nameof(ToyBox.AfterCombatEnd))]
        public static class Patch_DisableToyBoxControlOfWaxRelics
        {
            [HarmonyPrefix]
            private static bool Prefix(ToyBox __instance, ref Task __result)
            {
                if (!IsCurrentPlayer(__instance.Owner))
                {
                    return true;
                }

                __result = Task.CompletedTask;
                return false;
            }
        }

        /// <summary>
        /// Hides the counter on the Toy Box relic, since we are handling the melting of Wax Relics ourselves.
        /// </summary>
        [HarmonyPatch(typeof(ToyBox), nameof(ToyBox.ShowCounter), MethodType.Getter)]
        public static class Patch_HideToyBoxCounter
        {
            [HarmonyPostfix]
            private static void Postfix(ref bool __result)
            {
                __result = false;
            }
        }

        #endregion

        #region Harmony Patches - Melting

        /// <summary>
        /// Counts completed combats once at the game's canonical combat-end boundary and melts
        /// the leftmost unmelted wax relic after the defined number of combats between melts.
        /// </summary>
        /// <seealso cref="CombatsBetweenMelts"/>
        [HarmonyPatch(
            typeof(Hook),
            nameof(Hook.AfterCombatEnd),
            new[] { typeof(IRunState), typeof(ICombatState), typeof(CombatRoom) }
        )]
        public static class Patch_ProcessWaxRelicsAfterCombatEnd
        {
            [HarmonyPostfix]
            private static void Postfix(IRunState runState, ref Task __result)
            {
                __result = ProcessWaxRelicsAfterCombatEnd(__result, runState);
            }
        }

        #endregion
    }
}
