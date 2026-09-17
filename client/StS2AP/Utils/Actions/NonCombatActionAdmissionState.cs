namespace StS2AP.Utils;

/// <summary>
/// Local execution state required before submitting a noncombat managed action.
/// Feature-specific ownership and receipt validation remain with the caller.
/// </summary>
internal readonly record struct NonCombatActionAdmissionState(
    bool RunReady,
    bool IsLoading,
    bool CombatInProgress,
    bool CombatStarting,
    bool CombatEnding,
    bool IsNonCombatPhase,
    bool ExecutorRunning,
    bool ExecutorPaused,
    bool HasCurrentAction,
    bool QueuesEmpty)
{
    public string? BlockedReason => this switch
    {
        { RunReady: false } => "the run is not ready",
        { IsLoading: true } => "the game is loading or changing rooms",
        { CombatStarting: true } => "combat is starting",
        { CombatEnding: true } => "combat is ending",
        { CombatInProgress: true } => "combat is in progress",
        { IsNonCombatPhase: false } => "the native synchronizer is not outside combat",
        { HasCurrentAction: true } => "a native action is still executing",
        { ExecutorRunning: true } => "the native action executor is still running",
        { ExecutorPaused: true } => "the native action executor is paused",
        { QueuesEmpty: false } => "native action queues are not empty",
        _ => null,
    };
}
