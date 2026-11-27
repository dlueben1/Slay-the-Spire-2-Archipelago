using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;

namespace StS2AP.Bingo.Detectors.Characters;

/// <summary>Regent squares. "Win as Regent" is in <see cref="CharacterWinSquares"/>.</summary>
internal static class RegentSquares
{
    private const string HaveEightStars = "C-RE-E1";
    private const string ForgeThreeTimesInOneCombat = "C-RE-E2";
    private const string SpendSixStarsInOneTurn = "C-RE-E3";
    private const string HaveTwentyStars = "C-RE-M1";
    private const string PlayFourMinionCards = "C-RE-M3";
    private const string ForgeBladeTo150 = "C-RE-H1";
    private const string SpendFifteenStarsInOneTurn = "C-RE-H2";

    private static readonly PlayerTally ForgesThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally StarsSpentThisTurn = new(TallyScope.Turn);
    private static readonly PlayerTally MinionCardsPlayedThisCombat = new(TallyScope.Combat);

    // C-RE-E1, C-RE-M1: the Stars the player holds after a gain, as in the game's 20-Star
    // achievement.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterStarsGained))]
    private static class StarsGained
    {
        [HarmonyPrefix]
        private static void Prefix(Player gainer)
        {
            if (!BingoSquares.IsOpen(HaveEightStars) && !BingoSquares.IsOpen(HaveTwentyStars))
                return;

            CharacterSquares.Guard($"{nameof(RegentSquares)}.{nameof(StarsGained)}", () =>
            {
                if (!CharacterSquares.Plays<Regent>(gainer))
                    return;

                int stars = gainer.PlayerCombatState?.Stars ?? 0;
                BingoSquares.Reach(HaveEightStars, gainer, stars, 8);
                BingoSquares.Reach(HaveTwentyStars, gainer, stars, 20);
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
            if (!BingoSquares.IsOpen(PlayFourMinionCards))
                return;

            CharacterSquares.Guard($"{nameof(RegentSquares)}.{nameof(CardPlayed)}", () =>
            {
                Player player = cardPlay.Player;
                if (cardPlay.IsFirstInSeries
                    && cardPlay.Card.Tags.Contains(CardTag.Minion)
                    && CharacterSquares.Plays<Regent>(player))
                    BingoSquares.Reach(PlayFourMinionCards, player, MinionCardsPlayedThisCombat.Add(player), 4);
            });
        }
    }
}
