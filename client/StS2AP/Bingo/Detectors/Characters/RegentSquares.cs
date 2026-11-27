using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;

namespace StS2AP.Bingo.Detectors.Characters;

/// <summary>Regent squares. "Win as Regent" is in <see cref="CharacterWinSquares"/>.</summary>
internal static class RegentSquares
{
    private const string HaveEightStars = "C-RE-E1";
    private const string ForgeThreeTimesInOneCombat = "C-RE-E2";
    private const string SpendSixStarsInOneTurn = "C-RE-E3";
    private const string GenerateSixCardsInOneCombat = "C-RE-E4";
    private const string HaveTwentyStars = "C-RE-M1";
    private const string PlayFiveMinionCards = "C-RE-M3";
    private const string GainTenStarsInOneTurn = "C-RE-M4";
    private const string ForgeBladeTo150 = "C-RE-H1";
    private const string SpendFifteenStarsInOneTurn = "C-RE-H2";
    private const string GainFifteenEnergyInOneTurn = "C-RE-H4";
    private const string GainThirtyStarsInOneTurn = "C-RE-H5";
    private const string GenerateTwentyThreeCardsInOneCombat = "C-RE-H6";

    private static readonly PlayerTally ForgesThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally StarsSpentThisTurn = new(TallyScope.Turn);
    private static readonly PlayerTally MinionCardsPlayedThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally StarsGainedThisTurn = new(TallyScope.Turn);
    private static readonly PlayerTally EnergyGainedThisTurn = new(TallyScope.Turn);
    private static readonly PlayerTally CardsGeneratedThisCombat = new(TallyScope.Combat);

    // C-RE-E1, C-RE-M1: the Stars the player holds after a gain, as in the game's 20-Star
    // achievement.
    // C-RE-M4, C-RE-H5: Stars gained during the player's turn, from any source. Regent has no
    // automatic per-turn Star gain; the starter relics give theirs once per combat (Divine Right
    // on entering the room, before the first turn; Divine Destiny at the start of turn 1, which
    // counts toward that turn like Genesis does each turn).
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterStarsGained))]
    private static class StarsGained
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState combatState, int amount, Player gainer)
        {
            if (!BingoSquares.IsOpen(HaveEightStars) && !BingoSquares.IsOpen(HaveTwentyStars)
                && !BingoSquares.IsOpen(GainTenStarsInOneTurn) && !BingoSquares.IsOpen(GainThirtyStarsInOneTurn))
                return;

            CharacterSquares.Guard($"{nameof(RegentSquares)}.{nameof(StarsGained)}", () =>
            {
                if (!CharacterSquares.Plays<Regent>(gainer))
                    return;

                int stars = gainer.PlayerCombatState?.Stars ?? 0;
                BingoSquares.Reach(HaveEightStars, gainer, stars, 8);
                BingoSquares.Reach(HaveTwentyStars, gainer, stars, 20);

                if (amount > 0 && combatState.CurrentSide == CombatSide.Player)
                {
                    int gainedThisTurn = StarsGainedThisTurn.Add(gainer, amount);
                    BingoSquares.Reach(GainTenStarsInOneTurn, gainer, gainedThisTurn, 10);
                    BingoSquares.Reach(GainThirtyStarsInOneTurn, gainer, gainedThisTurn, 30);
                }
            });
        }
    }

    // C-RE-E3, C-RE-H2: Stars spent, summed over the turn.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterStarsSpent))]
    private static class StarsSpent
    {
        [HarmonyPrefix]
        private static void Prefix(int amount, Player spender)
        {
            if (amount <= 0
                || (!BingoSquares.IsOpen(SpendSixStarsInOneTurn) && !BingoSquares.IsOpen(SpendFifteenStarsInOneTurn)))
                return;

            CharacterSquares.Guard($"{nameof(RegentSquares)}.{nameof(StarsSpent)}", () =>
            {
                if (!CharacterSquares.Plays<Regent>(spender))
                    return;

                int spentThisTurn = StarsSpentThisTurn.Add(spender, amount);
                BingoSquares.Reach(SpendSixStarsInOneTurn, spender, spentThisTurn, 6);
                BingoSquares.Reach(SpendFifteenStarsInOneTurn, spender, spentThisTurn, 15);
            });
        }
    }

    // C-RE-E2: every Forge counts. C-RE-H1: after a Forge, the highest base damage among the
    // player's Sovereign Blades reaches 150 (ForgeCmd raises the blades' base damage before this
    // hook), the same check as the game's 999-damage achievement.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterForge))]
    private static class Forged
    {
        [HarmonyPrefix]
        private static void Prefix(Player forger)
        {
            if (!BingoSquares.IsOpen(ForgeThreeTimesInOneCombat) && !BingoSquares.IsOpen(ForgeBladeTo150))
                return;

            CharacterSquares.Guard($"{nameof(RegentSquares)}.{nameof(Forged)}", () =>
            {
                if (!CharacterSquares.Plays<Regent>(forger))
                    return;

                BingoSquares.Reach(ForgeThreeTimesInOneCombat, forger, ForgesThisCombat.Add(forger), 3);

                // Base damage is a decimal; truncating it keeps "150 or more" exactly as before.
                decimal strongestBlade = forger.PlayerCombatState?.AllCards
                    .OfType<SovereignBlade>()
                    .Select(blade => blade.DynamicVars.Damage.BaseValue)
                    .DefaultIfEmpty(0m)
                    .Max() ?? 0m;
                BingoSquares.Reach(ForgeBladeTo150, forger, (int)strongestBlade, 150);
            });
        }
    }

    // C-RE-M3: plays of cards tagged Minion (Minion Strike, Minion Dive Bomb, Minion Sacrifice,
    // the same tag Vitruvian Minion checks). A replayed card counts once.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
    private static class CardPlayed
    {
        [HarmonyPrefix]
        private static void Prefix(CardPlay cardPlay)
        {
            if (!BingoSquares.IsOpen(PlayFiveMinionCards))
                return;

            CharacterSquares.Guard($"{nameof(RegentSquares)}.{nameof(CardPlayed)}", () =>
            {
                Player player = cardPlay.Player;
                if (cardPlay.IsFirstInSeries
                    && cardPlay.Card.Tags.Contains(CardTag.Minion)
                    && CharacterSquares.Plays<Regent>(player))
                    BingoSquares.Reach(PlayFiveMinionCards, player, MinionCardsPlayedThisCombat.Add(player), 5);
            });
        }
    }

    // C-RE-E4, C-RE-H6: cards generated into the player's combat piles from any source (their own
    // cards, relics and potions, enemies' Statuses, cards transformed in combat), counted for
    // the player whose piles receive them.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardGeneratedForCombat))]
    private static class CardGenerated
    {
        [HarmonyPrefix]
        private static void Prefix(CardModel card)
        {
            if (!BingoSquares.IsOpen(GenerateSixCardsInOneCombat)
                && !BingoSquares.IsOpen(GenerateTwentyThreeCardsInOneCombat))
                return;

            CharacterSquares.Guard($"{nameof(RegentSquares)}.{nameof(CardGenerated)}", () =>
            {
                Player owner = card.Owner;
                if (!CharacterSquares.Plays<Regent>(owner))
                    return;

                int generated = CardsGeneratedThisCombat.Add(owner);
                BingoSquares.Reach(GenerateSixCardsInOneCombat, owner, generated, 6);
                BingoSquares.Reach(GenerateTwentyThreeCardsInOneCombat, owner, generated, 23);
            });
        }
    }

    // C-RE-H4: energy gained during the player's turn. The game has no hook after an energy gain,
    // so this patches the state change itself; PlayerCmd.GainEnergy is its only caller and passes
    // the amount after modifiers. The turn's energy reset sets Energy directly (ResetEnergy or
    // AddMaxEnergyToCurrent), so it never passes through here; energy that relics or powers give
    // at the start of the turn, after the reset, does count.
    [HarmonyPatch(typeof(PlayerCombatState), nameof(PlayerCombatState.GainEnergy))]
    private static class EnergyGained
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerCombatState __instance, decimal amount)
        {
            if (amount <= 0m || !BingoSquares.IsOpen(GainFifteenEnergyInOneTurn))
                return;

            CharacterSquares.Guard($"{nameof(RegentSquares)}.{nameof(EnergyGained)}", () =>
            {
                Player player = __instance._player;
                if (player.Creature.CombatState?.CurrentSide != CombatSide.Player
                    || !CharacterSquares.Plays<Regent>(player))
                    return;

                BingoSquares.Reach(GainFifteenEnergyInOneTurn, player,
                    EnergyGainedThisTurn.Add(player, (int)amount), 15);
            });
        }
    }
}
