using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using StS2AP.Models;
using StS2AP.Multiplayer;
using StS2AP.Persistence;
using StS2AP.Utils;

namespace StS2AP.Bingo.Detectors.Run;

/// <summary>
/// Reads the run's map point history: one entry per map point visited, grouped by act, with
/// each player's stats for it (rest site choices, cards gained, potions used, HP when they
/// left). The game saves it with the run and rewinds it with a reload, so squares judged from
/// it survive save-and-quit and agree with checkpoint reloads.
/// </summary>
/// <remarks>
/// The game appends a map point's entry before the room's BeforeRoomEntered hook runs, and
/// fills in the player stats (HP, gold) when the players leave it.
/// </remarks>
internal static class RunHistory
{
    /// <summary>The player this client plays, or null outside a run.</summary>
    public static Player? LocalPlayer(IRunState runState) => LocalContext.GetMe(runState.Players);

    public static IReadOnlyList<MapPointHistoryEntry> CurrentAct(IRunState runState) =>
        Act(runState, runState.CurrentActIndex);

    public static IReadOnlyList<MapPointHistoryEntry> Act(IRunState runState, int actIndex) =>
        actIndex >= 0 && actIndex < runState.MapPointHistory.Count
            ? runState.MapPointHistory[actIndex]
            : [];

    public static IEnumerable<MapPointHistoryEntry> WholeRun(IRunState runState) =>
        runState.MapPointHistory.SelectMany(act => act);

    /// <summary>The player's own stats for each of these map points.</summary>
    public static IEnumerable<PlayerMapPointHistoryEntry> StatsFor(
        IEnumerable<MapPointHistoryEntry> entries,
        Player player) =>
        entries
            .Select(entry => entry.PlayerStats.FirstOrDefault(stats => stats.PlayerId == player.NetId))
            .OfType<PlayerMapPointHistoryEntry>();

    public static int CountRooms(IEnumerable<MapPointHistoryEntry> entries, RoomType roomType) =>
        entries.Sum(entry => entry.Rooms.Count(room => room.RoomType == roomType));

    public static int CountRestChoices(IEnumerable<MapPointHistoryEntry> entries, Player player, string optionId) =>
        StatsFor(entries, player).Sum(stats => stats.RestSiteChoices.Count(choice => choice == optionId));

    /// <summary>
    /// Cards the player chose to add to the deck at these map points, as their canonical
    /// models (the history saves cards by ID). Basic cards are left out: none is ever a pick,
    /// and the history also records the starter card the mod re-adds when a Progressive
    /// Starter Card item arrives.
    /// </summary>
    public static IEnumerable<CardModel> CardsAdded(IEnumerable<MapPointHistoryEntry> entries, Player player) =>
        StatsFor(entries, player)
            .SelectMany(stats => stats.CardsGained)
            .Where(card => card.Id != null)
            .Select(card => SaveUtil.CardOrDeprecated(card.Id!))
            .Where(card => card.Rarity != CardRarity.Basic);
}

/// <summary>Rest site option IDs as the history records them.</summary>
internal static class RestChoices
{
    public const string Heal = "HEAL";
    public const string Smith = "SMITH";
    public const string Mend = "MEND";

    /// <summary>
    /// Options the mod adds to campfires: the AP check options (named after the item's
    /// classification, see ApRestSiteOption), the fallback when Rest and Smith are both locked
    /// (FakeRestSiteOption) and the Boon options (AP_BREW, AP_EMBER_ENCHANT). None of them is a
    /// rest site option in the squares' sense; the Boon ones would make the squares free at
    /// every campfire.
    /// </summary>
    public static bool IsArchipelagoOption(string optionId) =>
        optionId is "NOTHING" or "FILLER" or "TRAP" or "PROGRESSION" or "USEFUL"
        || optionId.StartsWith("AP_", StringComparison.Ordinal);
}

/// <summary>
/// The Campfire Sanity ruling for "no resting" squares: with Campfire Sanity on, Rest is locked
/// in an act until that act's Progressive Rest arrives, so an act only counts as "not resting"
/// once the player could have rested there.
/// </summary>
internal static class NoRestRule
{
    /// <summary>
    /// Whether not resting in acts 1 to <paramref name="lastActNumber"/> (one-based) was the
    /// player's choice. Progressive Rest levels are cumulative, so the last act's level covers
    /// the earlier acts. Judged when the square is checked, so a Progressive Rest that arrives
    /// late in the act still lets that act count.
    /// </summary>
    public static bool CouldRestThrough(Player player, int lastActNumber)
    {
        if (!ApPlayerContextResolver.TryGetRewardSettings(player, out ArchipelagoSettings settings))
            return false;
        if (!settings.CampfireSanity)
            return true;
        if (!ApPlayerContextResolver.TryGetCharacterConfig(player, out CharacterConfig config)
            || !ApPlayerContextResolver.TryGetRewardProgress(player, out ApRunProgressState progress, out _))
        {
            return false;
        }

        progress.ProgressiveRests.TryGetValue(config.CharOffset, out int restLevel);
        // Campfire Sanity has three Progressive Rests; any act after the third uses the third
        // (RestSitePolicy caps the act the same way).
        return restLevel >= Math.Min(lastActNumber, 3);
    }
}

/// <summary>Small helpers every run detector uses.</summary>
internal static class RunSquare
{
    /// <summary>
    /// Completes the square for the player when it is still open and the condition holds. The
    /// condition is only evaluated for open squares.
    /// </summary>
    public static void CompleteIf(string key, Player player, Func<bool> condition)
    {
        if (BingoSquares.IsOpen(key) && condition())
            BingoSquares.Complete(key, player);
    }

    /// <summary>
    /// Runs a detector from a game hook. Detectors only read game state; an exception in one
    /// must never break the hook it watches, so it is logged and swallowed. The hook's
    /// arguments come in as <paramref name="state"/> so callers pass a static lambda: a hook
    /// that fires often allocates nothing while bingo is off.
    /// </summary>
    public static void Detect<TState>(string detector, TState state, Action<TState> detect)
    {
        if (!BingoSquares.IsActive)
            return;
        try
        {
            detect(state);
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Bingo detector '{detector}' failed: {ex}");
        }
    }
}
