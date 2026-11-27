using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using StS2AP.Bingo.Detectors.Characters;

namespace StS2AP.Bingo.Detectors.Combat;

/// <summary>Squares about cards drawn in a turn, the cards left in the piles and long fights.</summary>
internal static class TurnAndPileDetectors
{
    // H22 "Draw 15 extra cards in one turn" (any character) and C-SI-M5 "Draw 8 extra cards in
    // one turn" (Silent) share the draw count.
    private const string FifteenExtraDraws = "H22";
    private const string SilentEightExtraDraws = "C-SI-M5";

    // H23 "Have 6 or fewer cards in your hand, draw and discard piles".
    private const string SixOrFewerCards = "H23";
    private const int MostCardsForFewCards = 6;

    // H24 "Reach turn 20 in a fight".
    private const string TurnTwenty = "H24";

    private static readonly PlayerTally ExtraDrawsThisTurn = new(TallyScope.Turn);

    /// <summary>
    /// "Extra" draws leave out the start-of-turn draw, which the game marks with
    /// <c>fromHandDraw</c> (Ring of the Snake and the like enlarge that draw, so they don't count
    /// either). Only draws during the player side's turn count, the same test the game's
    /// Speedster power uses. The tally starts over with every turn, extra turns included.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardDrawn))]
    private static class CardDrawn
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState combatState, CardModel card, bool fromHandDraw)
        {
            bool wantsDraws = BingoSquares.IsOpen(FifteenExtraDraws) || BingoSquares.IsOpen(SilentEightExtraDraws);
            if (!wantsDraws && !BingoSquares.IsOpen(SixOrFewerCards))
                return;

            CharacterSquares.Guard($"{nameof(TurnAndPileDetectors)}.{nameof(CardDrawn)}", () =>
            {
                Player owner = card.Owner;
                if (wantsDraws && !fromHandDraw && combatState.CurrentSide == CombatSide.Player)
                {
                    int extraDraws = ExtraDrawsThisTurn.Add(owner);
                    BingoSquares.Reach(FifteenExtraDraws, owner, extraDraws, 15);
                    CharacterSquares.Reach<Silent>(SilentEightExtraDraws, owner, extraDraws, 8);
                }
                CheckFewCards(owner);
            });
        }
    }

    // H23 is checked whenever the count can have dropped or a turn begins: when a card is played
    // (it has left the hand for the Play pile by BeforeCardPlayed, so it isn't counted), after an
    // exhaust, after a draw and at the start of the player's turn. Cards generated mid-play only
    // raise the count, so these moments catch the lowest it gets.

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardPlayed))]
    private static class CardPlayed
    {
        [HarmonyPrefix]
        private static void Prefix(CardPlay cardPlay)
        {
            if (!BingoSquares.IsOpen(SixOrFewerCards))
                return;
            CharacterSquares.Guard($"{nameof(TurnAndPileDetectors)}.{nameof(CardPlayed)}",
                () => CheckFewCards(cardPlay.Player));
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardExhausted))]
    private static class CardExhausted
    {
        [HarmonyPrefix]
        private static void Prefix(CardModel card)
        {
            if (!BingoSquares.IsOpen(SixOrFewerCards))
                return;
            CharacterSquares.Guard($"{nameof(TurnAndPileDetectors)}.{nameof(CardExhausted)}",
                () => CheckFewCards(card.Owner));
        }
    }

    /// <summary>
    /// H23 and H24 at the start of each player's turn, after its draw. TurnNumber is the player's
    /// own turn count the game's turn banner shows; extra turns raise it too.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
    private static class PlayerTurnStart
    {
        [HarmonyPrefix]
        private static void Prefix(Player player)
        {
            if (!BingoSquares.IsOpen(TurnTwenty) && !BingoSquares.IsOpen(SixOrFewerCards))
                return;

            CharacterSquares.Guard($"{nameof(TurnAndPileDetectors)}.{nameof(PlayerTurnStart)}", () =>
            {
                if (player.PlayerCombatState is { } combatState)
                    BingoSquares.Reach(TurnTwenty, player, combatState.TurnNumber, 20);
                CheckFewCards(player);
            });
        }
    }

    /// <summary>
    /// H23: cards in the hand, draw and discard piles. The Play pile (the card being played) and
    /// the Exhaust pile are left out.
    /// </summary>
    private static void CheckFewCards(Player? player)
    {
        if (player?.PlayerCombatState is not { } state || !BingoSquares.IsOpen(SixOrFewerCards)
            || CombatManager.Instance.IsOverOrEnding)
            return;

        int cards = state.Hand.Cards.Count + state.DrawPile.Cards.Count + state.DiscardPile.Cards.Count;
        if (cards <= MostCardsForFewCards)
            BingoSquares.Complete(SixOrFewerCards, player);
    }
}
