using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;

namespace StS2AP.Bingo.Detectors.Run;

/// <summary>
/// Squares about which map points the player visits. Each room entry re-reads the map point
/// history, which already holds the room being entered, so a square completes as the player
/// walks into the room that finishes it, and a run loaded from a save is judged the same way.
/// </summary>
/// <remarks>
/// "? room" means the map point type Unknown, whatever the room turned out to be (the game's
/// Mystery Machine badge counts the same way). Room types (fight, elite, shop...) are read from
/// the rooms themselves, so a ? that became a shop counts as visiting a shop.
/// </remarks>
internal static class MapRouteDetectors
{
    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeRoomEntered))]
    private static class RoomEntered
    {
        [HarmonyPrefix]
        private static void Prefix(IRunState runState) =>
            RunSquare.Detect("map route", () => Check(runState));
    }

    private static void Check(IRunState runState)
    {
        if (RunHistory.LocalPlayer(runState) is not Player me)
            return;
        IReadOnlyList<MapPointHistoryEntry> act = RunHistory.CurrentAct(runState);

        // E24, M20: ? map points visited in the current act.
        RunSquare.CompleteIf("E24", me, () => CountUnknown(act) >= 4);
        RunSquare.CompleteIf("M20", me, () => CountUnknown(act) >= 6);
        // H14: ? map points over the whole run.
        RunSquare.CompleteIf("H14", me, () => CountUnknown(RunHistory.WholeRun(runState)) >= 12);

        // R-E03: map points with a rest site room in the current act.
        RunSquare.CompleteIf("R-E03", me,
            () => act.Count(entry => entry.HasRoomOfType(RoomType.RestSite)) >= 4);

        // R-E02: a fight, an elite, a ?, a shop, a rest site and a treasure room in one act.
        RunSquare.CompleteIf("R-E02", me, () =>
            act.Any(entry => entry.MapPointType == MapPointType.Unknown)
            && act.Any(entry => entry.HasRoomOfType(RoomType.Monster))
            && act.Any(entry => entry.HasRoomOfType(RoomType.Elite))
            && act.Any(entry => entry.HasRoomOfType(RoomType.Shop))
            && act.Any(entry => entry.HasRoomOfType(RoomType.RestSite))
            && act.Any(entry => entry.HasRoomOfType(RoomType.Treasure)));
    }

    private static int CountUnknown(IEnumerable<MapPointHistoryEntry> entries) =>
        entries.Count(entry => entry.MapPointType == MapPointType.Unknown);
}
