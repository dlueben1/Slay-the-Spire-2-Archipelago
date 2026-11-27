using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;

namespace StS2AP.Bingo.Detectors.Characters;

/// <summary>Ironclad squares. "Win as Ironclad" is in <see cref="CharacterWinSquares"/>.</summary>
internal static class IroncladSquares
{
    private const string ExhaustThreeInOneTurn = "C-IR-E1";
    private const string ExhaustSevenInOneTurn = "C-IR-M6";
    private const string HurtYourselfThreeTimes = "C-IR-E2";
    private const string HurtYourselfSixTimes = "C-IR-M5";
    private const string ExhaustTwentyInOneCombat = "C-IR-M1";
    private const string ExhaustThreeStatusOrCurse = "C-IR-M2";
    private const string LoseTwentyHpToOwnCardsAndWin = "C-IR-M3";
    private const string PlaySixPowers = "C-IR-H1";
    private const string FourVulnerableOnOneEnemy = "C-IR-E3";
    private const string EightVulnerableOnOneEnemy = "C-IR-M4";
    private const string TwentyVulnerableOnOneEnemy = "C-IR-H3";
    private const string Heal32InOneCombat = "C-IR-H4";
    private const string Have250Block = "C-IR-H5";

    private static readonly PlayerTally ExhaustedThisTurn = new(TallyScope.Turn);
    private static readonly PlayerTally ExhaustedThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally StatusOrCurseExhaustedThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally SelfHurtsThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally HpLostToOwnCardsThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally PowersPlayedThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally HpHealedThisCombat = new(TallyScope.Combat);

    // C-IR-E1, C-IR-M6, C-IR-M1, C-IR-M2: every exhausted card counts for its owner, Ethereal ones
    // included (like the game's own 20-exhaust achievement). M2 counts Status and Curse cards only.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardExhausted))]
    private static class CardExhausted
    {
        [HarmonyPrefix]
        private static void Prefix(CardModel card)
        {
            if (!BingoSquares.IsOpen(ExhaustThreeInOneTurn)
                && !BingoSquares.IsOpen(ExhaustSevenInOneTurn)
                && !BingoSquares.IsOpen(ExhaustTwentyInOneCombat)
                && !BingoSquares.IsOpen(ExhaustThreeStatusOrCurse))
                return;

            CharacterSquares.Guard($"{nameof(IroncladSquares)}.{nameof(CardExhausted)}", () =>
            {
                Player owner = card.Owner;
                if (!CharacterSquares.Plays<Ironclad>(owner))
                    return;

                int exhaustedThisTurn = ExhaustedThisTurn.Add(owner);
                BingoSquares.Reach(ExhaustThreeInOneTurn, owner, exhaustedThisTurn, 3);
                BingoSquares.Reach(ExhaustSevenInOneTurn, owner, exhaustedThisTurn, 7);
                BingoSquares.Reach(ExhaustTwentyInOneCombat, owner, ExhaustedThisCombat.Add(owner), 20);
                if (card.Type is CardType.Status or CardType.Curse)
                    BingoSquares.Reach(ExhaustThreeStatusOrCurse, owner, StatusOrCurseExhaustedThisCombat.Add(owner), 3);
            });
        }
    }

    // C-IR-E2, C-IR-M5: a hit you deal to yourself that costs HP. HP-cost cards (Bloodletting, Offering,
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
            if (!BingoSquares.IsOpen(HurtYourselfThreeTimes)
                && !BingoSquares.IsOpen(HurtYourselfSixTimes)
                && !BingoSquares.IsOpen(LoseTwentyHpToOwnCardsAndWin))
                return;

            CharacterSquares.Guard($"{nameof(IroncladSquares)}.{nameof(DamageGiven)}", () =>
            {
                Player? victim = target.Player;
                if (results.UnblockedDamage <= 0 || !CharacterSquares.Plays<Ironclad>(victim))
                    return;

                if (dealer == target)
                {
                    int selfHurts = SelfHurtsThisCombat.Add(victim!);
                    BingoSquares.Reach(HurtYourselfThreeTimes, victim, selfHurts, 3);
                    BingoSquares.Reach(HurtYourselfSixTimes, victim, selfHurts, 6);
                }
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
                    CharacterSquares.Reach<Ironclad>(
                        LoseTwentyHpToOwnCardsAndWin, player, HpLostToOwnCardsThisCombat.Get(player), 20);
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
                    && CharacterSquares.Plays<Ironclad>(player))
                    BingoSquares.Reach(PlaySixPowers, player, PowersPlayedThisCombat.Add(player), 6);
            });
        }
    }

    // C-IR-E3, C-IR-M4, C-IR-H3: the Vulnerable an enemy holds, checked each time the player adds
    // to it. The game stacks everyone's Vulnerable on an enemy into one power, so in multiplayer
    // the total can include other players' Vulnerable; it counts for the player whose application
    // brought it there. Vulnerable with no applier (some relic effects) is credited only in
    // single-player, where it can only be the one player's doing, as for the core E09.
    // AfterPowerAmountChanged sees the power's new total, whether the power was just applied or
    // an existing stack grew.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPowerAmountChanged))]
    private static class PowerAmountChanged
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState combatState, PowerModel power, decimal amount, Creature? applier)
        {
            if (power is not VulnerablePower || amount <= 0)
                return;
            if (!BingoSquares.IsOpen(FourVulnerableOnOneEnemy)
                && !BingoSquares.IsOpen(EightVulnerableOnOneEnemy)
                && !BingoSquares.IsOpen(TwentyVulnerableOnOneEnemy))
                return;

            CharacterSquares.Guard($"{nameof(IroncladSquares)}.{nameof(PowerAmountChanged)}", () =>
            {
                if (power.Owner.Side != CombatSide.Enemy)
                    return;

                Player? applying = CharacterSquares.PlayerBehind(applier)
                    ?? (applier == null && combatState.Players.Count == 1 ? combatState.Players[0] : null);
                if (!CharacterSquares.Plays<Ironclad>(applying))
                    return;

                BingoSquares.Reach(FourVulnerableOnOneEnemy, applying, power.Amount, 4);
                BingoSquares.Reach(EightVulnerableOnOneEnemy, applying, power.Amount, 8);
                BingoSquares.Reach(TwentyVulnerableOnOneEnemy, applying, power.Amount, 20);
            });
        }
    }

    // C-IR-H4: HP actually restored to the player during one combat, from any source. Every heal
    // goes through CreatureCmd.Heal, which only restores up to Max HP; the prefix runs before the
    // heal and works out the same capped amount the game records in the run history. Max HP
    // gains heal by the amount gained (Feed and the like), so they count too.
    // The combat runs until its room is marked finished, after AfterCombatVictory, so Burning
    // Blood's end-of-combat heal counts toward the combat it ends. Potions drunk on the reward
    // screen afterwards and heals outside combat (rest sites, events) do not.
    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Heal))]
    private static class Healed
    {
        [HarmonyPrefix]
        private static void Prefix(Creature creature, decimal amount)
        {
            if (amount <= 0 || !BingoSquares.IsOpen(Heal32InOneCombat))
                return;

            CharacterSquares.Guard($"{nameof(IroncladSquares)}.{nameof(Healed)}", () =>
            {
                Player? player = creature.Player;
                if (!CharacterSquares.Plays<Ironclad>(player)
                    || creature.CombatState == null
                    || player!.RunState.CurrentRoom is not CombatRoom { IsPreFinished: false })
                    return;

                int healed = (int)Math.Min(amount, creature.MaxHp - creature.CurrentHp);
                if (healed > 0)
                    BingoSquares.Reach(Heal32InOneCombat, player, HpHealedThisCombat.Add(player, healed), 32);
            });
        }
    }

    // C-IR-H5: the Ironclad version of the core E06. The game adds the Block to the creature
    // before AfterBlockGained runs.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterBlockGained))]
    private static class BlockGained
    {
        [HarmonyPrefix]
        private static void Prefix(Creature creature)
        {
            if (!BingoSquares.IsOpen(Have250Block))
                return;

            CharacterSquares.Guard($"{nameof(IroncladSquares)}.{nameof(BlockGained)}",
                () => CharacterSquares.Reach<Ironclad>(Have250Block, creature.Player, creature.Block, 250));
        }
    }
}
