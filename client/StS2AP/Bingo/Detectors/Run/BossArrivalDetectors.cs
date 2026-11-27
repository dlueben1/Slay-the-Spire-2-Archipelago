using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;

namespace StS2AP.Bingo.Detectors.Run;

/// <summary>
/// "Reach a boss" squares, judged as the player enters an act's boss room. "That act" or
/// "this act" means the act the boss belongs to; "Reach the Act 2 boss" squares look at the
/// whole run so far. History-based parts read the run's map point history (see
/// <see cref="RunHistory"/>), deck parts the deck as the boss room is entered.
/// </summary>
internal static class BossArrivalDetectors
{
    /// <summary>Act index of Act 2 (the game counts acts from 0).</summary>
    private const int Act2 = 1;

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeRoomEntered))]
    private static class RoomEntered
    {
        [HarmonyPrefix]
        private static void Prefix(IRunState runState, AbstractRoom room)
        {
            if (room.RoomType == RoomType.Boss)
                RunSquare.Detect("boss arrival", runState, OnBossReached);
        }
    }

    private static void OnBossReached(IRunState runState)
    {
        if (RunHistory.LocalPlayer(runState) is not Player me)
            return;
        int actIndex = runState.CurrentActIndex;
        IReadOnlyList<MapPointHistoryEntry> act = RunHistory.CurrentAct(runState);
        IReadOnlyList<CardModel> deck = me.Deck.Cards;

        // E21: at most one ? map point visited this act. None at all needed Winged Boots on most
        // maps, where every path crosses a ? (user ruling).
        RunSquare.CompleteIf("E21", me, () => act.Count(entry => entry.MapPointType == MapPointType.Unknown) <= 1);

        // E22, E23: took the leftmost (rightmost) way at every step of this act's map.
        RunSquare.CompleteIf("E22", me, () => EdgePath.Followed(runState, leftmost: true));
        RunSquare.CompleteIf("E23", me, () => EdgePath.Followed(runState, leftmost: false));

        // E25: no Rest (Heal) chosen this act, and this act's Progressive Rest is held.
        RunSquare.CompleteIf("E25", me, () =>
            RunHistory.CountRestChoices(act, me, RestChoices.Heal) == 0
            && NoRestRule.CouldRestThrough(me, actIndex + 1));

        // R-E07, R-E08: the cards added to the deck this act are all Attacks (Skills), at least
        // three. Curses, Statuses and quest cards are not chosen, so they are left out.
        RunSquare.CompleteIf("R-E07", me, () => AddedOnly(CardsAddedThisAct(act, me), CardType.Attack));
        RunSquare.CompleteIf("R-E08", me, () => AddedOnly(CardsAddedThisAct(act, me), CardType.Skill));

        // R-E09: never below half HP this act (live record plus history, see HalfHpWatch).
        RunSquare.CompleteIf("R-E09", me, () => !HalfHpWatch.DroppedThisAct(runState, me));

        // AP-E08 (Cursed Run): no Curse in the deck on entering the boss room.
        RunSquare.CompleteIf("AP-E08", me, () => deck.All(card => card.Type != CardType.Curse));

        if (actIndex != Act2)
            return;
        IEnumerable<MapPointHistoryEntry> run = RunHistory.WholeRun(runState);

        // M18: 15 or fewer cards in the deck at the Act 2 boss.
        RunSquare.CompleteIf("M18", me, () => deck.Count <= 15);

        // R-M04: no Rest (Heal) in Acts 1 and 2, and both acts' Progressive Rests are held.
        RunSquare.CompleteIf("R-M04", me, () =>
            RunHistory.CountRestChoices(run, me, RestChoices.Heal) == 0
            && NoRestRule.CouldRestThrough(me, actIndex + 1));

        // R-M06: no upgraded card in the deck at the Act 2 boss.
        RunSquare.CompleteIf("R-M06", me, () => deck.All(card => !card.IsUpgraded));

        // R-M07: at most 5 non-Curse, non-Basic cards added to the deck in the run so far.
        RunSquare.CompleteIf("R-M07", me, () =>
            RunHistory.CardsAdded(run, me).Count(card => card.Type != CardType.Curse) <= 5);

        // R-M08: no potion used in the run so far (discarding one is not using it).
        RunSquare.CompleteIf("R-M08", me, () =>
            RunHistory.StatsFor(run, me).All(stats => stats.PotionUsed.Count == 0));
    }

    internal static IEnumerable<CardType> CardsAddedThisAct(IReadOnlyList<MapPointHistoryEntry> act, Player me) =>
        RunHistory.CardsAdded(act, me)
            .Select(card => card.Type)
            .Where(type => type is CardType.Attack or CardType.Skill or CardType.Power);

    private static bool AddedOnly(IEnumerable<CardType> added, CardType wanted)
    {
        List<CardType> types = added.ToList();
        return types.Count >= 3 && types.All(type => type == wanted);
    }

    /// <summary>
    /// The leftmost (rightmost) path: starting from the act's first map point, always moving
    /// to the child with the lowest (highest) column. At every fork that is the leftmost
    /// (rightmost) way on offer, which also makes it the one path that hugs the map's left
    /// (right) side. A move to anything else, including travel that skips the map's links,
    /// breaks it.
    /// </summary>
    internal static class EdgePath
    {
        public static bool Followed(IRunState runState, bool leftmost)
        {
            // VisitedMapCoords is on the run state itself, not the interface, on game 0.111.1.
            if (runState is not RunState state)
                return false;
            ActMap map = state.Map;
            List<MapPoint?> path = state.VisitedMapCoords.Select(coord => map.GetPoint(coord)).ToList();
            // The act's start point (its Ancient) is where every path begins; prepend it when
            // the visits don't include it.
            if (path.Count == 0 || path[0] != map.StartingMapPoint)
                path.Insert(0, map.StartingMapPoint);

            for (int i = 0; i + 1 < path.Count; i++)
            {
                if (path[i] is not MapPoint from || path[i + 1] is not MapPoint to || from.Children.Count == 0)
                    return false;
                MapPoint edgeChild = leftmost
                    ? from.Children.MinBy(child => child.coord.col)!
                    : from.Children.MaxBy(child => child.coord.col)!;
                if (edgeChild != to)
                    return false;
            }
            return true;
        }
    }
}
