using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using StS2AP.Utils;

namespace StS2AP.Bingo.Detectors.Run;

/// <summary>
/// "Win" squares, judged when the game ends the run as a victory (RunManager.OnEnded, the
/// moment the client's own victory handling uses), from the deck and relics the player won
/// with and the run's map point history.
/// </summary>
internal static class RunVictoryDetectors
{
    // OnEnded returns a different type on 0.111.1 and 0.114.0; the postfix does not need it.
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded), [typeof(bool)])]
    private static class RunEnded
    {
        [HarmonyPostfix]
        private static void Postfix(bool isVictory)
        {
            if (isVictory)
                RunSquare.Detect("run victory", OnRunWon);
        }
    }

    private static void OnRunWon()
    {
        if (RunManager.Instance.DebugOnlyGetState() is not RunState runState
            || RunHistory.LocalPlayer(runState) is not Player me)
        {
            return;
        }
        IReadOnlyList<CardModel> deck = me.Deck.Cards;
        IEnumerable<MapPointHistoryEntry> run = RunHistory.WholeRun(runState);

        // H01: any win.
        RunSquare.CompleteIf("H01", me, () => true);

        // H15, H16: deck size at the win.
        RunSquare.CompleteIf("H15", me, () => deck.Count <= 10);
        RunSquare.CompleteIf("H16", me, () => deck.Count >= 45);

        // H17: no Rest (Heal) chosen all run, and every act's Progressive Rest is held.
        RunSquare.CompleteIf("H17", me, () =>
            RunHistory.CountRestChoices(run, me, RestChoices.Heal) == 0
            && NoRestRule.CouldRestThrough(me, runState.MapPointHistory.Count));

        // H18: no shop room entered all run (a ? that became a shop counts as a shop).
        RunSquare.CompleteIf("H18", me, () => RunHistory.CountRooms(run, RoomType.Shop) == 0);

        // H19: no two cards in the deck share a card ID; upgraded copies are the same card, and
        // the starter Strikes and Defends count like any other card.
        RunSquare.CompleteIf("H19", me, () => deck.Select(card => card.Id).Distinct().Count() == deck.Count);

        // H20: three or fewer relics. Relics this mod defines are AP's own indicators that every
        // AP run carries (Relic Coupons, Boons, Sight bonuses) and would make the square
        // impossible, so they are not counted; relics received as AP items are game relics and
        // count.
        RunSquare.CompleteIf("H20", me, () => me.Relics.Count(relic => !IsModIndicator(relic)) <= 3);

        // R-H02: no Rare card in the deck at the win.
        RunSquare.CompleteIf("R-H02", me, () => deck.All(card => card.Rarity != CardRarity.Rare));

        // AP-H03: the win is a Dread run (DreadRuns answers "no" on versions without Dread).
        RunSquare.CompleteIf("AP-H03", me, () => DreadRuns.IsDreadRun(runState));
    }

    private static bool IsModIndicator(RelicModel relic) =>
        relic.GetType().Assembly == typeof(RunVictoryDetectors).Assembly;
}
