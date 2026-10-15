using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using StS2AP.Utils;

namespace StS2AP.Patches
{
    /// <summary>
    /// A collection of Patches related to Death Link
    /// </summary>
    public static class Patches_DeathLink
    {
        /// <summary>
        /// How many seconds after receiving a Death Link that we'll suppress sending one back out.
        /// This prevents the infinite feedback loop where receiving a Death Link kill triggers us to send
        /// another one back, which kills the sender again, and so on.
        /// </summary>
        private const double DeathLinkSuppressionWindowSeconds = 6.0;

        /// <summary>
        /// Fires when the player sees the "Game Over" screen.
        /// If Death Link is enabled, this is how we'll send our Death Link Event.
        /// </summary>
        [HarmonyPatch(typeof(NRun), nameof(NRun.ShowGameOverScreen))]
        public static class OnRunFailed
        {
            static bool Prefix(NRun __instance, SerializableRun serializableRun)
            {
                // AP_MP: multiplayer uses a replicated individual-death boundary instead.
                if (!MultiplayerSupport.IsFeatureEnabled(MultiplayerFeature.DeathLink))
                    return true;

                // Multiplayer sends at the individual player's actual death boundary instead of
                // waiting for the shared game-over screen.
                if (MultiplayerSupport.IsRealMultiplayerRun)
                    return true;

                // If Death Link isn't enabled, there's nothing to do
                if (!DeathLinkUtility.IsDeathLinkEnabled) return true;

                // Grab the state of the Run
                RunState? runState = __instance._state;

                // Don't send death link if the player won (died to the Architect)
                if (runState?.CurrentRoom?.IsVictoryRoom ?? false)
                {
                    return true;
                }

                /// If the timestamp is within the suppression window, this death was caused by an
                /// incoming Death Link — don't echo it back out or we'll create an infinite loop.
                /// 
                /// We only care about this if the Death Link Type is Kill or Damage. If it's Curse,
                /// `LastDeathLinkReceivedAt` will never have a value, so this suppression check won't
                /// fire.
                if (ArchipelagoClient.LastDeathLinkReceivedAt.HasValue)
                {
                    double secondsSinceReceived = (DateTime.UtcNow - ArchipelagoClient.LastDeathLinkReceivedAt.Value).TotalSeconds;
                    if (secondsSinceReceived <= DeathLinkSuppressionWindowSeconds)
                    {
                        LogUtility.Info($"Suppressing outgoing Death Link — this death was caused by a received Death Link ({secondsSinceReceived:F2}s ago, within {DeathLinkSuppressionWindowSeconds}s window).");

                        // Clear the timestamp so future deaths are not incorrectly suppressed
                        ArchipelagoClient.LastDeathLinkReceivedAt = null;

                        // Still return true so the Game Over screen shows normally
                        return true;
                    }

                    // Outside the window — clear the stale timestamp and fall through to send
                    ArchipelagoClient.LastDeathLinkReceivedAt = null;
                }

                // Prepare a "Cause of Death" message, because we want to be cool
                string floorCause = runState != null ? $"Act {runState.CurrentActIndex + 1} Floor {runState.ActFloor}" : "an unknown floor";
                string causeMsg = $"{ArchipelagoClient.PlayerName} was Slain on {floorCause}";

                // Send a Death Link Trigger
                DeathLinkService? deathLinkController = ArchipelagoClient.DeathLinkController;
                if (deathLinkController == null)
                {
                    LogUtility.Error("Could not send Death Link because the service is unavailable.");
                    return true;
                }
                deathLinkController.SendDeathLink(new DeathLink(ArchipelagoClient.PlayerName, causeMsg));

                // Finally, return control to the function
                return true;
            }
        }
    }
}
