using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Data;

namespace StS2AP.Utils;

/// <summary>
/// The Map Sight bonus's look ahead. Each act draws its fights and elites from queues built when
/// the act starts, and ? rooms roll their type on a generator of their own, so the next fights,
/// elites and ? room results are the same whichever path the player takes. The one exception is a
/// ? room's shop rule, which depends on the rooms beside it, so only a room the player can move to
/// next is described exactly.
/// </summary>
internal static class MapScouting
{
    // How far ahead the Scouting panel looks.
    internal const int FightsShown = 2;
    internal const int ElitesShown = 1;
    internal const int UnknownRoomsShown = 2;

    internal static bool IsActive =>
        ChallengeSightItems.HasBonus(
            ArchipelagoClient.Settings?.HiddenMap == true,
            ItemTable.APItem.MapSight,
            "MapSight");

    internal static IReadOnlyList<EncounterModel> UpcomingFights(RunState runState, int count) =>
        Upcoming(runState.Act._rooms.normalEncounters, runState.Act._rooms.normalEncountersVisited, count);

    internal static IReadOnlyList<EncounterModel> UpcomingElites(RunState runState, int count) =>
        Upcoming(runState.Act._rooms.eliteEncounters, runState.Act._rooms.eliteEncountersVisited, count);

    // The game wraps around a queue it runs out of, as RoomSet.NextNormalEncounter does.
    private static List<EncounterModel> Upcoming(List<EncounterModel> queue, int visited, int count) =>
        queue.Count == 0
            ? []
            : Enumerable.Range(0, count).Select(offset => queue[(visited + offset) % queue.Count]).ToList();

    /// <summary>
    /// What the next ? rooms will turn into. This runs the game's own roll on a copy of the ? room
    /// odds and generator, so run modifiers such as Dangerous Unknowns and relics such as Juzu
    /// Bracelet shape the answer as they will the real roll. <paramref name="firstRoom"/> is the ?
    /// room the player would enter first, when known, for its shop rule; later rooms skip the rule.
    /// </summary>
    internal static IReadOnlyList<RoomType> UpcomingUnknownRooms(RunState runState, int count, MapPoint? firstRoom = null)
    {
        UnknownMapPointOdds odds = CopyOdds(runState.Odds.UnknownMapPoint);
        var results = new List<RoomType>(count);
        for (int index = 0; index < count; index++)
        {
            IEnumerable<RoomType> blacklist = index == 0
                ? RunManager.BuildRoomTypeBlacklist(
                    runState.CurrentMapPointHistoryEntry,
                    firstRoom?.Children ?? new HashSet<MapPoint>())
                : [];
            results.Add(odds.Roll(blacklist, runState));
        }
        return results;
    }

    private static UnknownMapPointOdds CopyOdds(UnknownMapPointOdds odds)
    {
        var copy = new UnknownMapPointOdds(GameApiCompat.CopyRng(odds._rng));
        foreach ((RoomType roomType, float chance) in odds._baseOdds)
            copy._baseOdds[roomType] = chance;
        foreach ((RoomType roomType, float chance) in odds._nonEventOdds)
            copy._nonEventOdds[roomType] = chance;
        return copy;
    }

    /// <summary>
    /// What a room the player can move to next holds, or null for a room scouting says nothing
    /// about. Fights and elites name their enemies; a ? room names what it will turn into.
    /// </summary>
    internal static string? DescribeNextRoom(RunState runState, MapPoint point) => point.PointType switch
    {
        MapPointType.Monster => FirstTitle(UpcomingFights(runState, 1)),
        MapPointType.Elite => FirstTitle(UpcomingElites(runState, 1)),
        MapPointType.Unknown => DescribeUnknownResult(runState, UpcomingUnknownRooms(runState, 1, point)[0]),
        _ => null,
    };

    private static string? DescribeUnknownResult(RunState runState, RoomType roomType) => roomType switch
    {
        // A ? room fight takes the next fight or elite from the same queues as other rooms.
        RoomType.Monster => $"{Text("fight")}: {FirstTitle(UpcomingFights(runState, 1))}",
        RoomType.Elite => $"{Text("elite")}: {FirstTitle(UpcomingElites(runState, 1))}",
        _ => RoomName(roomType),
    };

    /// <summary>The short name the Scouting panel and tips use for what a ? room turns into.</summary>
    internal static string RoomName(RoomType roomType) => roomType switch
    {
        RoomType.Monster => Text("fight"),
        RoomType.Elite => Text("elite"),
        RoomType.Shop => Text("merchant"),
        RoomType.Treasure => Text("treasure"),
        _ => Text("event"),
    };

    internal static string Text(string key) =>
        new LocString("gameplay_ui", $"AP_SCOUTING.{key}").GetFormattedText();

    private static string? FirstTitle(IReadOnlyList<EncounterModel> encounters) =>
        encounters.Count == 0 ? null : encounters[0].Title.GetFormattedText();
}
