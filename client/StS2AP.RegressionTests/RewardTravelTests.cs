using StS2AP.Utils;
using Xunit;

namespace StS2AP.RegressionTests;

public sealed class RewardTravelTests
{
    [Fact(DisplayName = "AP menu remains available until actual travel starts")]
    public void APMenuRemainsAvailableUntilActualTravelStarts()
    {
        var guard = new ApRewardTravelGuard();
        // Votes are deliberately not inputs to the guard.
        Assert.True(guard.CanOpen(guard.ApLifecycleVersion, isLoading: false));
        guard.BeginTravel();
        Assert.False(guard.CanOpen(guard.ApLifecycleVersion, isLoading: false));
    }

    [Fact(DisplayName = "AP travel gate covers animation before loading starts")]
    public void APTravelGateCoversAnimationBeforeLoadingStarts()
    {
        var guard = new ApRewardTravelGuard();
        int travel = guard.BeginTravel();
        Assert.False(guard.CanOpen(travel, isLoading: false));
        Assert.False(guard.CanOpen(travel, isLoading: true));
        guard.EndTravel(travel);
        Assert.True(guard.CanOpen(travel, isLoading: false));
    }

    [Fact(DisplayName = "An AP menu awaiting preparation cannot open in the next room")]
    public void AnAPMenuAwaitingPreparationCannotOpenInTheNextRoom()
    {
        var guard = new ApRewardTravelGuard();
        int opening = guard.ApLifecycleVersion;
        int travel = guard.BeginTravel();
        guard.EndTravel(travel);
        Assert.False(guard.CanOpen(opening, isLoading: false));
        Assert.True(guard.CanOpen(guard.ApLifecycleVersion, isLoading: false));
    }

    [Fact(DisplayName = "AP loading guard also covers non-map room changes")]
    public void APLoadingGuardAlsoCoversNonMapRoomChanges()
    {
        var guard = new ApRewardTravelGuard();
        Assert.False(guard.CanOpen(guard.ApLifecycleVersion, isLoading: true));
    }

    [Fact(DisplayName = "AP cleanup invalidates pending UI work without leaving travel locked")]
    public void APCleanupInvalidatesPendingUIWorkWithoutLeavingTravelLocked()
    {
        var guard = new ApRewardTravelGuard();
        int oldTravel = guard.BeginTravel();
        guard.Reset();
        Assert.False(guard.CanOpen(oldTravel, isLoading: false));
        Assert.True(guard.CanOpen(guard.ApLifecycleVersion, isLoading: false));
    }

    [Fact(DisplayName = "Old travel completion cannot unlock a later travel or a new run")]
    public void OldTravelCompletionCannotUnlockALaterTravelOrANewRun()
    {
        var guard = new ApRewardTravelGuard();
        int oldTravel = guard.BeginTravel();
        guard.Reset();
        int currentTravel = guard.BeginTravel();
        guard.EndTravel(oldTravel);
        Assert.False(guard.CanOpen(currentTravel, isLoading: false));
        guard.EndTravel(currentTravel);
        Assert.True(guard.CanOpen(currentTravel, isLoading: false));
    }
}
