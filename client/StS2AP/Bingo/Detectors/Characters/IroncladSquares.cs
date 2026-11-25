using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;

namespace StS2AP.Bingo.Detectors.Characters;

/// <summary>Ironclad squares. "Win as Ironclad" is in <see cref="CharacterWinSquares"/>.</summary>
internal static class IroncladSquares
{
    private const string ExhaustSixInOneTurn = "C-IR-E1";
    private const string HurtYourselfThreeTimes = "C-IR-E2";
    private const string ExhaustTwentyInOneCombat = "C-IR-M1";
    private const string ExhaustThreeStatusOrCurse = "C-IR-M2";
    private const string LoseTwentyHpToOwnCardsAndWin = "C-IR-M3";
    private const string PlaySixPowers = "C-IR-H1";

    private static readonly PlayerTally ExhaustedThisTurn = new(TallyScope.Turn);
    private static readonly PlayerTally ExhaustedThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally StatusOrCurseExhaustedThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally SelfHurtsThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally HpLostToOwnCardsThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally PowersPlayedThisCombat = new(TallyScope.Combat);

    // C-IR-E1, C-IR-M1, C-IR-M2: every exhausted card counts for its owner, Ethereal ones included
    // (like the game's own 20-exhaust achievement). M2 counts Status and Curse cards only.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardExhausted))]
    private static class CardExhausted
    {
        [HarmonyPrefix]
        private static void Prefix(CardModel card)
        {
            if (!BingoSquares.IsOpen(ExhaustSixInOneTurn)
                && !BingoSquares.IsOpen(ExhaustTwentyInOneCombat)
                && !BingoSquares.IsOpen(ExhaustThreeStatusOrCurse))
                return;

            CharacterSquares.Guard($"{nameof(IroncladSquares)}.{nameof(CardExhausted)}", () =>
            {
                Player owner = card.Owner;
                if (!CharacterSquares.Plays<Ironclad>(owner))
                    return;

                if (ExhaustedThisTurn.Add(owner) >= 6)
                    BingoSquares.Complete(ExhaustSixInOneTurn, owner);
                if (ExhaustedThisCombat.Add(owner) >= 20)
                    BingoSquares.Complete(ExhaustTwentyInOneCombat, owner);
                if (card.Type is CardType.Status or CardType.Curse
                    && StatusOrCurseExhaustedThisCombat.Add(owner) >= 3)
                    BingoSquares.Complete(ExhaustThreeStatusOrCurse, owner);
            });
        }
    }

    // C-IR-E2: a hit you deal to yourself that costs HP. HP-cost cards (Bloodletting, Offering,
    // Hemokinesis...) damage their owner with the owner as dealer.
    // C-IR-M3: HP lost to damage whose source is one of your own cards (HP-cost cards, Burns and
    // the like), summed over the combat and judged when the combat is won.
    // AfterDamageGiven fires for every hit, including one that kills, unlike AfterDamageReceived.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageGiven))]
    private static class DamageGiven
    {
        [HarmonyPrefix]
        private static void Prefix(Creature? dealer, DamageResult results, Creature target, CardModel? cardSource)
        {
            if (!BingoSquares.IsOpen(HurtYourselfThreeTimes) && !BingoSquares.IsOpen(LoseTwentyHpToOwnCardsAndWin))
                return;

            CharacterSquares.Guard($"{nameof(IroncladSquares)}.{nameof(DamageGiven)}", () =>
            {
                Player? victim = target.Player;
                if (results.UnblockedDamage <= 0 || !CharacterSquares.Plays<Ironclad>(victim))
                    return;

                if (dealer == target && SelfHurtsThisCombat.Add(victim!) >= 3)
                    BingoSquares.Complete(HurtYourselfThreeTimes, victim);
                if (cardSource != null && cardSource.Owner == victim)
                    HpLostToOwnCardsThisCombat.Add(victim!, results.UnblockedDamage);
            });
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatVictory))]
    private static class CombatVictory
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState? combatState)
        {
            if (combatState == null || !BingoSquares.IsOpen(LoseTwentyHpToOwnCardsAndWin))
                return;

            CharacterSquares.Guard($"{nameof(IroncladSquares)}.{nameof(CombatVictory)}", () =>
            {
                foreach (Player player in combatState.Players)
                {
                    if (HpLostToOwnCardsThisCombat.Get(player) >= 20)
                        CharacterSquares.Complete<Ironclad>(LoseTwentyHpToOwnCardsAndWin, player);
                }
            });
        }
    }

    // C-IR-H1: Power cards played, auto-plays included. A replayed card counts once.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
    private static class CardPlayed
    {
        [HarmonyPrefix]
        private static void Prefix(CardPlay cardPlay)
        {
            if (!BingoSquares.IsOpen(PlaySixPowers))
                return;

            CharacterSquares.Guard($"{nameof(IroncladSquares)}.{nameof(CardPlayed)}", () =>
            {
                Player player = cardPlay.Player;
                if (cardPlay.IsFirstInSeries
                    && cardPlay.Card.Type == CardType.Power
                    && CharacterSquares.Plays<Ironclad>(player)
                    && PowersPlayedThisCombat.Add(player) >= 6)
                    BingoSquares.Complete(PlaySixPowers, player);
            });
        }
    }
}
