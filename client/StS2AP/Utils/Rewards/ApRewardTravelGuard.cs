namespace StS2AP.Utils;

/// <summary>
/// Transient UI lifetime only: a menu opening started before travel/reset cannot resume afterward.
/// This is not replicated gameplay state and must not be saved with AP progress.
/// </summary>
internal sealed class ApRewardTravelGuard
{
    // Incremented at map-travel start and UI reset to reject stale async menu work.
    // Local only: this is not a multiplayer synchronization version.
    public int ApLifecycleVersion { get; private set; }
    public bool IsTraveling { get; private set; }

    public int BeginTravel()
    {
        IsTraveling = true;
        return ++ApLifecycleVersion;
    }

    public void EndTravel(int apLifecycleVersion)
    {
        if (apLifecycleVersion == ApLifecycleVersion)
            IsTraveling = false;
    }

    public bool CanOpen(int apLifecycleVersion, bool isLoading) =>
        apLifecycleVersion == ApLifecycleVersion && !IsTraveling && !isLoading;

    public void Reset()
    {
        ++ApLifecycleVersion;
        IsTraveling = false;
    }
}
