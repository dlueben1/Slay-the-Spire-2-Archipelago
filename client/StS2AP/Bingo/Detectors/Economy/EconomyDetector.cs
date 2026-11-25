using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Bingo.Detectors.Economy;

/// <summary>
/// Shared plumbing for the economy detectors (gold, shops, deck makeup, relics, potions, Max HP).
/// They only read game state; every patch body goes through <see cref="Run"/> so a detector bug
/// is logged instead of thrown into the game's own code.
/// </summary>
internal static class EconomyDetector
{
    /// <summary>
    /// Runs a detector body when bingo is active, logging any exception. Callers pass a static
    /// lambda and its arguments as <paramref name="state"/>, so hooks that fire constantly (every
    /// card moved, every upgrade) allocate nothing, whether or not bingo is active.
    /// </summary>
    public static void Run<TState>(string detector, TState state, Action<TState> body)
    {
        if (!BingoSquares.IsActive)
            return;
        try
        {
            body(state);
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Bingo detector '{detector}' failed: {ex}");
        }
    }

    /// <summary>Completes the square when it is still open and the condition holds.</summary>
    public static void CompleteIf(string key, bool condition, Player player)
    {
        if (condition && BingoSquares.IsOpen(key))
            BingoSquares.Complete(key, player);
    }

    public static bool AnyOpen(IEnumerable<string> keys) => keys.Any(BingoSquares.IsOpen);

    /// <summary>The player this client plays in the run, if any.</summary>
    public static Player? LocalPlayer(IRunState? runState) =>
        runState == null ? null : LocalContext.GetMe(runState);

    /// <summary>
    /// The player's entries in the run record, one per map point visited so far, including the
    /// current one. The record is saved with the run, so totals read from it survive save and quit.
    /// </summary>
    public static IEnumerable<PlayerMapPointHistoryEntry> RunRecord(Player player) =>
        player.RunState.MapPointHistory
            .SelectMany(act => act)
            .SelectMany(point => point.PlayerStats)
            .Where(stats => stats.PlayerId == player.NetId);

    /// <summary>The player's entry in the run record for the map point they are on.</summary>
    public static PlayerMapPointHistoryEntry? CurrentPointRecord(Player player) =>
        player.RunState.CurrentMapPointHistoryEntry?.PlayerStats
            .FirstOrDefault(stats => stats.PlayerId == player.NetId);

    /// <summary>
    /// Checks every "have X" and run-total square for the local player. Called at the
    /// checkpoints in <see cref="EconomyCheckpoints"/> so a loaded run, or a change no specific
    /// hook saw, is still judged.
    /// </summary>
    public static void CheckAll(Player player)
    {
        if (!LocalContext.IsMe(player))
            return;
        GoldDetectors.CheckGoldHeld(player);
        GoldDetectors.CheckGoldSpentInRun(player);
        ShopDetectors.CheckShopCardRemovals(player);
        DeckDetectors.CheckDeck(player);
        DeckDetectors.CheckTransforms(player);
        RelicAndPotionDetectors.CheckRelics(player);
        RelicAndPotionDetectors.CheckPotionSlots(player);
        MaxHpDetectors.CheckMaxHp(player);
    }
}
