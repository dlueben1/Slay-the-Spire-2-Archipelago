namespace StS2AP.Utils;

/// <summary>
/// AP rest-site decisions after the caller has resolved one player's settings and progress.
/// Native option availability and location IDs are supplied by the game adapter.
/// </summary>
internal sealed class RestSitePolicy(int currentAct, int restLevel, int smithLevel)
{
    public int CurrentAct { get; } = Math.Min(currentAct, 3);

    /// <summary>Whether this act's Progressive Rest has been received.</summary>
    public bool CanRest => restLevel >= CurrentAct;

    /// <summary>Whether this act's Progressive Smith has been received.</summary>
    public bool CanSmith => smithLevel >= CurrentAct;

    /// <summary>
    /// Applies progression locks, then adds a Do Nothing option after the remaining native and
    /// relic actions, before AP checks are added. Do Nothing is always offered so a player can
    /// leave without resting or smithing; it is also the safe exit when no other action is usable.
    /// Returns whether Do Nothing is the only way out.
    /// </summary>
    public bool ApplyLocks<TOption>(
        ICollection<TOption> options,
        Func<TOption, string> getOptionId,
        Func<TOption, bool> isEnabled,
        Func<TOption> createSkip)
    {
        bool canRest = CanRest;
        bool canSmith = CanSmith;
        foreach (TOption option in options.ToArray())
        {
            string id = getOptionId(option);
            if ((!canRest && id is "HEAL" or "MEND") || (!canSmith && id == "SMITH"))
                options.Remove(option);
        }

        // Unlocked Smith can still be disabled, and relic actions must remain usable.
        // With Rest and Smith both locked, relic actions do not count as a way out.
        bool skipIsOnlyExit = (!canRest && !canSmith) || !options.Any(isEnabled);
        options.Add(createSkip());
        return skipIsOnlyExit;
    }

    public IEnumerable<(int Act, int Campfire, long LocationId)> GetAvailableChecks(
        IReadOnlySet<long> checkedLocations,
        Func<int, int, long> getLocationId)
    {
        for (int act = 1; act <= CurrentAct; act++)
        {
            for (int campfire = 1; campfire <= 2; campfire++)
            {
                long locationId = getLocationId(act, campfire);
                if (!checkedLocations.Contains(locationId))
                    yield return (act, campfire, locationId);
            }
        }
    }
}
