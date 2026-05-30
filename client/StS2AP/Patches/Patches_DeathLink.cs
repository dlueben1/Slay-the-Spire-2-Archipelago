using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using StS2AP.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Patches
{
    /// <summary>
    /// A collection of Patches related to Death Link
    /// </summary>
    public static class Patches_DeathLink
    {
        /// <summary>
        /// Fires when the player sees the "Game Over" screen.
        /// If Death Link is enabled, this is how we'll send our Death Link Event.
        /// </summary>
        [HarmonyPatch(typeof(NRun), nameof(NRun.ShowGameOverScreen))]
        public static class OnRunFailed
        {
            static bool Prefix(NRun __instance, SerializableRun serializableRun)
            {
                // If Death Link isn't enabled, there's nothing to do
                if (!ArchipelagoClient.Settings.IsDeathLinkEnabled) return true;

                // Grab the state of the Run
                RunState? runState = Traverse.Create(__instance).Field<RunState>("_state").Value;

                // Prepare a "Cause of Death" message, because we want to be cool
                string floorCause = runState != null ? $"Act {runState.CurrentActIndex + 1} Floor {runState.ActFloor}" : "an unknown floor";
                string causeMsg = $"{ArchipelagoClient.PlayerName} was Slain on {floorCause}";

                // Send a Death Link Trigger
                ArchipelagoClient.DeathLinkController.SendDeathLink(new DeathLink(ArchipelagoClient.PlayerName, causeMsg));

                // Finally, return control to the function
                return true;
            }
        }
    }
}
