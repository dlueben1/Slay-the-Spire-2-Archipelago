using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace StS2AP.Utils;

/// <summary>
/// Utilities for selecting relics that are compatible with Archipelago rewards.
/// </summary>
public static class RelicUtility
{
    private const int MaxRelicPullAttempts = 15;

    /// <summary>
    /// Relics whose effects do not work correctly with Archipelago's custom reward flow.
    /// Add new incompatible relic model types here.
    /// </summary>
    private static readonly HashSet<Type> BlacklistedRelicTypes =
    [
        typeof(LastingCandy),
    ];

    /// <summary>
    /// Pulls relics from the front of the player's relic queue until a compatible relic is found.
    /// Blacklisted relics remain consumed from the queue so they cannot be selected later in the run.
    /// </summary>
    public static RelicModel PullNextAllowedRelic(Player player)
    {
        for (int attempt = 0; attempt < MaxRelicPullAttempts; attempt++)
        {
            var relic = RelicFactory.PullNextRelicFromFront(player);
            if (!BlacklistedRelicTypes.Contains(relic.GetType()))
            {
                return relic;
            }

            LogUtility.Info(
                $"Skipped blacklisted relic '{relic.Id}' while assigning an Archipelago relic reward"
            );
        }

        throw new InvalidOperationException(
            $"RelicFactory did not return an allowed relic after {MaxRelicPullAttempts} attempts"
        );
    }
}
