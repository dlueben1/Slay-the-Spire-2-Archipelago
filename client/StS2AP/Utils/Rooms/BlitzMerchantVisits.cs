using MegaCrit.Sts2.Core.Runs;
using StS2AP.Models;
using StS2AP.Multiplayer;
using StS2AP.Persistence;

namespace StS2AP.Utils;

/// <summary>
/// The Blitz Merchant Chance option: some shops of a standard or Dread run are the game's Blitz
/// merchant, with six offers and three free picks in place of a shop's stock. The game picks
/// between the two shops by game mode alone, and the Blitz merchant's own code does not need a
/// Blitz run, so the mod only has to make that choice itself.
/// In multiplayer the host's chance applies to the whole run, and every peer makes the same
/// choice: the host's settings are frozen into the run, and the roll depends only on the run's
/// seed and the shop's place. The game itself keeps the players' Blitz picks in step.
/// Game versions before 0.113 have no Blitz merchant; there the answer is always "no".
/// </summary>
internal static class BlitzMerchantVisits
{
    /// <summary>The free picks the game gives at a Blitz merchant.</summary>
    public const int FreePicks = 3;

    /// <summary>Whether the shop the run is entering is a Blitz merchant.</summary>
    public static bool IsBlitzMerchant(IRunState? runState)
    {
#if STS2_AT_LEAST_0_113_0
        int chance = RunSettings(runState)?.BlitzMerchantChance ?? 0;
        if (chance <= 0
            || runState == null
            // A Blitz run has its own merchants already.
            || runState.GameMode == GameMode.Blitz)
        {
            return false;
        }

        // A shop the game opens away from the map, such as a debug room, has no place to roll for.
        if (runState.CurrentMapCoord is not { } coord)
            return false;
        return BlitzMerchantRoll.IsBlitzMerchant(
            runState.Rng.Seed,
            runState.CurrentActIndex,
            coord.col,
            coord.row,
            chance);
#else
        return false;
#endif
    }

    /// <summary>
    /// The AP settings that decide this run's shops: the slot's own in singleplayer, the host's
    /// frozen settings in multiplayer (the same on every peer), or none outside an AP run.
    /// </summary>
    private static ArchipelagoSettings? RunSettings(IRunState? runState)
    {
        if (MultiplayerSupport.IsRealMultiplayerRun)
        {
            return runState is RunState state && ApRunData.TryGetSharedState(state, out ApRunSharedState shared)
                ? shared.HostSettings
                : null;
        }
        return ApSingleplayerSaves.IsHandlingSingleplayerRun ? ArchipelagoClient.Settings : null;
    }
}
