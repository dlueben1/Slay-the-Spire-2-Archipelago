using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using StS2AP.Utils;

namespace StS2AP.Bingo.Detectors.Run;

/// <summary>
/// Elite counts and boss wins. Elites defeated are the elite rooms in the map point history,
/// as the game's own score and Elite Killer badge count them: every elite room before the
/// current one was won, since losing ends the run. A won fight completes its square at once;
/// each room entry re-checks the history too, so a run loaded from a save still counts.
/// </summary>
internal static class EliteDetectors
{
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatVictory))]
    private static class CombatWon
    {
        [HarmonyPrefix]
        private static void Prefix(IRunState runState, CombatRoom room) =>
            RunSquare.Detect("combat victory", () => OnCombatWon(runState, room));
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeRoomEntered))]
    private static class RoomEntered
    {
        [HarmonyPrefix]
        private static void Prefix(IRunState runState) =>
            RunSquare.Detect("elites", () =>
            {
                // The room being entered has not been won (yet).
                if (RunHistory.LocalPlayer(runState) is Player me)
                    CheckElites(runState, me, runState.CurrentMapPointHistoryEntry);
            });
    }

    private static void OnCombatWon(IRunState runState, CombatRoom room)
    {
        if (RunHistory.LocalPlayer(runState) is not Player me)
            return;

        if (room.RoomType == RoomType.Elite)
            CheckElites(runState, me, unfinished: null);

        // AP-E10: the boss fight of the run's first act won in a Dread run. DreadRuns answers
        // "no" on game versions without Dread.
        if (room.RoomType == RoomType.Boss && runState.CurrentActIndex == 0)
            RunSquare.CompleteIf("AP-E10", me, () => DreadRuns.IsDreadRun(runState));
    }

    private static void CheckElites(IRunState runState, Player me, MapPointHistoryEntry? unfinished)
    {
        int DefeatedIn(IEnumerable<MapPointHistoryEntry> entries) =>
            RunHistory.CountRooms(entries.Where(entry => entry != unfinished), RoomType.Elite);

        IReadOnlyList<MapPointHistoryEntry> act = RunHistory.CurrentAct(runState);
        // E20, F-M15: elites defeated in the current act.
        RunSquare.CompleteIf("E20", me, () => DefeatedIn(act) >= 2);
        RunSquare.CompleteIf("F-M15", me, () => DefeatedIn(act) >= 3);
        // M19, H13: elites defeated over the whole run.
        RunSquare.CompleteIf("M19", me, () => DefeatedIn(RunHistory.WholeRun(runState)) >= 4);
        RunSquare.CompleteIf("H13", me, () => DefeatedIn(RunHistory.WholeRun(runState)) >= 7);
    }
}
