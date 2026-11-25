using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using static StS2AP.Bingo.Detectors.Combat.CombatDetectorSupport;

namespace StS2AP.Bingo.Detectors.Combat;

/// <summary>Squares about the cards played, exhausted and held during a combat.</summary>
internal static class CardPlayDetectors
{
    /// <summary>
    /// <see cref="Hook.BeforeCardPlayed"/> fires once per play of a card, so a card played twice
    /// (Burst, replays) fires it twice; only the first play in the series counts as a card
    /// played. It is used instead of <see cref="Hook.AfterCardPlayed"/>, which the game skips
    /// for the card that ends the combat.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardPlayed))]
    private static class WatchCardPlays
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState combatState, CardPlay cardPlay) =>
            Guard("card played", () => OnCardPlayed(combatState, cardPlay));
    }

    private static void OnCardPlayed(ICombatState combatState, CardPlay cardPlay)
    {
        if (!BingoSquares.IsActive || !cardPlay.IsFirstInSeries)
            return;
        Player player = cardPlay.Player;

        // E04 / M01 / R-M01: cards played this player turn. Auto-plays count like any play;
        // plays during the enemies' turn are not "in one turn" of yours and are ignored.
        bool wantsTurnCount = BingoSquares.IsOpen("E04") || BingoSquares.IsOpen("M01") || BingoSquares.IsOpen("R-M01");
        if (wantsTurnCount && combatState.CurrentSide == CombatSide.Player)
        {
            int playedThisTurn = CardsPlayedEarlierThisTurn(combatState, player) + 1;
            CompleteIf("E04", player, () => playedThisTurn >= 8);
            CompleteIf("M01", player, () => playedThisTurn >= 15);
            if (playedThisTurn > 2)
                CombatNotes.MarkPlayedMoreThanTwoInATurn(player);
        }

        // W-M01: X is what the card will use, including Chemical X style bonuses. Auto-plays are
        // skipped: they pay nothing, and the game doesn't capture an X for them.
        CompleteIf("W-M01", player, () => !cardPlay.IsAutoPlay && XValue(combatState, cardPlay) >= 5);
    }

    /// <summary>
    /// First plays of this player's cards earlier in the current turn, read from the combat
    /// history. The play being started isn't recorded yet when <see cref="Hook.BeforeCardPlayed"/>
    /// runs. <see cref="CombatHistoryEntry.HappenedThisTurn"/> also tells extra turns apart.
    /// </summary>
    private static int CardsPlayedEarlierThisTurn(ICombatState combatState, Player player) =>
        CombatManager.Instance.History.CardPlaysStarted.Count(e =>
            e.CardPlay.Player == player && e.CardPlay.IsFirstInSeries && e.HappenedThisTurn(combatState));

    /// <summary>
    /// The X an X-cost card is played for, or 0 when it isn't X-cost. Energy X (every
    /// character) and Star X (Regent) both count. <see cref="Hook.ModifyXValue"/> only reads.
    /// </summary>
    private static int XValue(ICombatState combatState, CardPlay cardPlay)
    {
        CardModel card = cardPlay.Card;
        if (card.EnergyCost.CostsX)
            return Hook.ModifyXValue(combatState, card, cardPlay.Resources.EnergyValue);
        if (card.HasStarCostX)
            return Hook.ModifyXValue(combatState, card, cardPlay.Resources.StarValue);
        return 0;
    }

    /// <summary>
    /// E11 / M09: cards of this player exhausted this combat, counted from the combat history
    /// (the game records the exhaust just before this hook). Ethereal cards fading count.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardExhausted))]
    private static class WatchExhausts
    {
        [HarmonyPrefix]
        private static void Prefix(CardModel card) =>
            Guard("card exhausted", () =>
            {
                if (!BingoSquares.IsOpen("E11") && !BingoSquares.IsOpen("M09"))
                    return;
                Player owner = card.Owner;
                int exhausted = CombatManager.Instance.History.Entries
                    .OfType<CardExhaustedEntry>()
                    .Count(e => e.Actor == owner.Creature);
                CompleteIf("E11", owner, () => exhausted >= 5);
                CompleteIf("M09", owner, () => exhausted >= 12);
            });
    }

    /// <summary>
    /// E12: every way a card reaches the hand (draws, generated cards, returns from other piles)
    /// goes through <see cref="Hook.AfterCardChangedPiles"/>. The game caps the hand at 10, so
    /// 10 is a full hand.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardChangedPiles))]
    private static class WatchHandSize
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState? combatState, CardModel card) =>
            Guard("hand size", () =>
            {
                if (combatState == null || !BingoSquares.IsOpen("E12"))
                    return;
                Player owner = card.Owner;
                int handSize = owner.PlayerCombatState?.Hand.Cards.Count ?? 0;
                CompleteIf("E12", owner, () => handSize >= 10);
            });
    }
}
