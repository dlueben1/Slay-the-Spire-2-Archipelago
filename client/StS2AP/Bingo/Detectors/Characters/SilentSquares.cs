using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace StS2AP.Bingo.Detectors.Characters;

/// <summary>Silent squares. "Win as Silent" is in <see cref="CharacterWinSquares"/>.</summary>
internal static class SilentSquares
{
    private const string KillWithPoison = "C-SI-E1";
    private const string TriggerTwoSlyInOneCombat = "C-SI-E2";
    private const string DiscardEightInOneTurn = "C-SI-M3";
    private const string TriggerThreeSlyInOneTurn = "C-SI-M4";
    private const string TwoHundredBlock = "C-SI-H2";
    private const string TwentyFiveShivsInOneTurn = "C-SI-H3";

    // C-SI-M5 "Draw 8 extra cards in one turn" shares the core draw count in
    // Combat/TurnAndPileDetectors.cs.

    private static readonly PlayerTally SlyTriggeredThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally SlyTriggeredThisTurn = new(TallyScope.Turn);
    private static readonly PlayerTally DiscardedThisTurn = new(TallyScope.Turn);
    private static readonly PlayerTally ShivsPlayedThisTurn = new(TallyScope.Turn);

    // C-SI-E1: Poison ticks deal Unblockable, Unpowered damage with no dealer and no card source
    // (PoisonPower.Trigger), and the Poison power is still on its owner when the killing hit is
    // reported. Like Doom kills, a Poison kill counts for every player: one PoisonPower holds
    // everyone's stacks and its Applier is only whoever created it, so it can't name the killer.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageGiven))]
    private static class DamageGiven
    {
        private const ValueProp PoisonTickProps = ValueProp.Unblockable | ValueProp.Unpowered;

        [HarmonyPrefix]
        private static void Prefix(
            ICombatState combatState, Creature? dealer, DamageResult results, ValueProp props,
            Creature target, CardModel? cardSource)
        {
            if (!results.WasTargetKilled || !BingoSquares.IsOpen(KillWithPoison))
                return;

            CharacterSquares.Guard($"{nameof(SilentSquares)}.{nameof(DamageGiven)}", () =>
            {
                if (dealer != null || cardSource != null || target.Side != CombatSide.Enemy
                    || (props & PoisonTickProps) != PoisonTickProps)
                    return;

                if (target.HasPower<PoisonPower>())
                    CharacterSquares.CompleteForLocalPlayer<Silent>(KillWithPoison, combatState);
            });
        }
    }

    // C-SI-E2, C-SI-M4: a Sly card discarded from hand is auto-played with AutoPlayType.SlyDiscard
    // (CardCmd.DiscardAndDraw); every such auto-play counts as one trigger.
    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardAutoPlayed))]
    private static class CardAutoPlayed
    {
        [HarmonyPrefix]
        private static void Prefix(CardModel card, AutoPlayType type)
        {
            if (type != AutoPlayType.SlyDiscard
                || (!BingoSquares.IsOpen(TriggerTwoSlyInOneCombat) && !BingoSquares.IsOpen(TriggerThreeSlyInOneTurn)))
                return;

            CharacterSquares.Guard($"{nameof(SilentSquares)}.{nameof(CardAutoPlayed)}", () =>
            {
                Player owner = card.Owner;
                if (!CharacterSquares.Plays<Silent>(owner))
                    return;

                BingoSquares.Reach(TriggerTwoSlyInOneCombat, owner, SlyTriggeredThisCombat.Add(owner), 2);
                BingoSquares.Reach(TriggerThreeSlyInOneTurn, owner, SlyTriggeredThisTurn.Add(owner), 3);
            });
        }
    }

    // C-SI-M3: AfterCardDiscarded fires only for discards by effects (CardCmd.DiscardAndDraw).
    // The end-of-turn hand flush reports through AfterFlush instead, so it never counts.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardDiscarded))]
    private static class CardDiscarded
    {
        [HarmonyPrefix]
        private static void Prefix(CardModel card)
        {
            if (!BingoSquares.IsOpen(DiscardEightInOneTurn))
                return;

            CharacterSquares.Guard($"{nameof(SilentSquares)}.{nameof(CardDiscarded)}", () =>
            {
                Player owner = card.Owner;
                if (CharacterSquares.Plays<Silent>(owner))
                    BingoSquares.Reach(DiscardEightInOneTurn, owner, DiscardedThisTurn.Add(owner), 8);
            });
        }
    }

    // C-SI-H2: like E06, the Block is already on the creature when AfterBlockGained runs.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterBlockGained))]
    private static class BlockGained
    {
        [HarmonyPrefix]
        private static void Prefix(Creature creature)
        {
            if (!BingoSquares.IsOpen(TwoHundredBlock))
                return;

            CharacterSquares.Guard($"{nameof(SilentSquares)}.{nameof(BlockGained)}",
                () => CharacterSquares.Reach<Silent>(TwoHundredBlock, creature.Player, creature.Block, 200));
        }
    }

    // C-SI-H3: a Shiv is any card with the game's Shiv tag (upgraded Shivs too), the test Helical
    // Dart and Phantom Blades use. As with "Play 8 cards in one turn", only the first play of a
    // replayed card counts, and Shivs played during the enemies' turn don't.
    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardPlayed))]
    private static class CardPlayed
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState combatState, CardPlay cardPlay)
        {
            if (!BingoSquares.IsOpen(TwentyFiveShivsInOneTurn) || !cardPlay.IsFirstInSeries
                || combatState.CurrentSide != CombatSide.Player)
                return;

            CharacterSquares.Guard($"{nameof(SilentSquares)}.{nameof(CardPlayed)}", () =>
            {
                Player player = cardPlay.Player;
                if (CharacterSquares.Plays<Silent>(player) && cardPlay.Card.Tags.Contains(CardTag.Shiv))
                    BingoSquares.Reach(TwentyFiveShivsInOneTurn, player, ShivsPlayedThisTurn.Add(player), 25);
            });
        }
    }
}
