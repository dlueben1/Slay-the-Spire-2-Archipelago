using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Utils;

/// <summary>
/// Keeps noncombat managed actions out of native queues until local execution is idle.
/// </summary>
internal static class NonCombatActionAdmission
{
    public static NonCombatActionAdmissionState CaptureState()
    {
        RunManager manager = RunManager.Instance;
        CombatManager combat = CombatManager.Instance;
        var executor = manager.ActionExecutor;

        // NotInCombat also covers travel into a combat room. A NonCombat action queued behind
        // that move can reach another peer after combat starts and block later player actions.
        return new NonCombatActionAdmissionState(
            RunReady: manager.IsInProgress
                && !manager.IsCleaningUp
                && !manager.IsGameOver
                && manager.DebugOnlyGetState()?.CurrentRoom != null,
            IsLoading: manager.NetService.IsGameLoading,
            CombatInProgress: combat.IsInProgress,
            CombatStarting: combat.IsStarting,
            CombatEnding: combat.IsEnding,
            IsNonCombatPhase: BetaMainCompatibility.IsActionSynchronizerCombatState(
                manager.ActionQueueSynchronizer.CombatState,
                ActionSynchronizerCombatState.NotInCombat),
            ExecutorRunning: executor.IsRunning,
            ExecutorPaused: executor.IsPaused,
            HasCurrentAction: executor.CurrentlyRunningAction != null,
            QueuesEmpty: manager.ActionQueueSet.IsEmpty
        );
    }

    public static Func<bool> CreateGate(string description)
    {
        string? lastBlockReason = null;
        return () =>
        {
            string? reason = CaptureState().BlockedReason;
            if (reason == null)
            {
                lastBlockReason = null;
                return true;
            }

            if (!string.Equals(lastBlockReason, reason, StringComparison.Ordinal))
            {
                lastBlockReason = reason;
                RunManager manager = RunManager.Instance;
                LogUtility.Info(
                    $"Deferred {description}: {reason}; "
                        + $"synchronizer={manager.ActionQueueSynchronizer.CombatState}, "
                        + $"currentAction={manager.ActionExecutor.CurrentlyRunningAction?.GetType().Name ?? "none"}."
                );
            }
            return false;
        };
    }
}
