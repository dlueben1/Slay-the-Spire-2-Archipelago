using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Patches;

/// <summary>
/// Drops a combat that outlived its run before a new run starts.
/// </summary>
/// <remarks>
/// When the host disconnects a client for state divergence while a map move into a combat room is
/// running, the client cleans up its run but the move carries on and sets up that combat afterwards.
/// The leftover combat makes <see cref="CombatManager.IsInProgress"/> true in the client's next run,
/// so the client takes a checksum after every action that the host does not, and the next run fails
/// its first checksum at Neow. Seen in AP multiplayer on beta 0.113.1, 2026-11-15.
/// </remarks>
internal static class Patches_StaleCombatReset
{
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.InitializeShared))]
    private static class ResetLeftoverCombat
    {
        // Postfix: the reset tells the new run's action synchronizer that no combat is running,
        // which it already assumes, instead of the previous run's disposed one.
        [HarmonyPostfix]
        private static void Postfix()
        {
            CombatManager combat = CombatManager.Instance;
            if (combat.DebugOnlyGetState() == null)
                return;

            LogUtility.Warn("A combat from the previous run was still set up at run start; resetting it.");
            try
            {
                // Graceful: main 0.107.1 only clears its combat state on a graceful reset.
                combat.Reset(graceful: true);
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Could not reset the previous run's combat: {ex}");
            }
        }
    }
}
