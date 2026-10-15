using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class NonCombatActionAdmissionTests
{
    private static readonly NonCombatActionAdmissionState Idle = new(
        RunReady: true, IsLoading: false, CombatInProgress: false,
        CombatStarting: false, CombatEnding: false, IsNonCombatPhase: true,
        ExecutorRunning: false, ExecutorPaused: false, HasCurrentAction: false,
        QueuesEmpty: true);

    [Fact]
    public void ReadyIdleNoncombatStateIsAdmitted() => Assert.Null(Idle.BlockedReason);

    [Fact]
    public void UninitializedStateIsBlocked() =>
        Assert.NotNull(default(NonCombatActionAdmissionState).BlockedReason);

    [Theory]
    [InlineData("RunReady")]
    [InlineData("IsLoading")]
    [InlineData("CombatStarting")]
    [InlineData("CombatEnding")]
    [InlineData("CombatInProgress")]
    [InlineData("IsNonCombatPhase")]
    [InlineData("ExecutorRunning")]
    [InlineData("ExecutorPaused")]
    [InlineData("HasCurrentAction")]
    [InlineData("QueuesEmpty")]
    public void EachUnsafeConditionIndependentlyBlocksAdmission(string condition)
    {
        var state = condition switch
        {
            "RunReady" => Idle with { RunReady = false },
            "IsLoading" => Idle with { IsLoading = true },
            "CombatStarting" => Idle with { CombatStarting = true },
            "CombatEnding" => Idle with { CombatEnding = true },
            "CombatInProgress" => Idle with { CombatInProgress = true },
            "IsNonCombatPhase" => Idle with { IsNonCombatPhase = false },
            "ExecutorRunning" => Idle with { ExecutorRunning = true },
            "ExecutorPaused" => Idle with { ExecutorPaused = true },
            "HasCurrentAction" => Idle with { HasCurrentAction = true },
            "QueuesEmpty" => Idle with { QueuesEmpty = false },
            _ => throw new ArgumentOutOfRangeException(nameof(condition)),
        };
        Assert.NotNull(state.BlockedReason);
    }
}
