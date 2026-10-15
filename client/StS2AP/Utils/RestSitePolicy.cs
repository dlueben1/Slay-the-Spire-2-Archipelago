namespace StS2AP.Utils;

/// <summary>
/// AP rest-site decisions after the caller has resolved one player's settings and progress.
/// Native option availability and location IDs are supplied by the game adapter.
/// </summary>
internal sealed class RestSitePolicy(int currentAct, int restLevel, int smithLevel)
{
    public int CurrentAct { get; } = Math.Min(currentAct, 3);

    /// <summary>Applies progression locks, then provides a safe exit before AP checks are added.</summary>
    public bool ApplyLocks<TOption>(
        ICollection<TOption> options,
        Func<TOption, string> getOptionId,
        Func<TOption, bool> isEnabled,
        Func<TOption> createFallback)
    {
        bool canRest = restLevel >= CurrentAct;
        bool canSmith = smithLevel >= CurrentAct;
        foreach (TOption option in options.ToArray())
        {
            string id = getOptionId(option);
            if ((!canRest && id is "HEAL" or "MEND") || (!canSmith && id == "SMITH"))
                options.Remove(option);
        }

        // Unlocked Smith can still be disabled, and relic actions must remain usable.
        // Preserve the both-locked fallback even if a relic provides another action.
        bool needsFallback = (!canRest && !canSmith) || !options.Any(isEnabled);
        if (needsFallback)
        {
            TOption fallback = createFallback();
            if (options is List<TOption> list)
                list.Insert(0, fallback);
            else
                options.Add(fallback);
        }
        return needsFallback;
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
