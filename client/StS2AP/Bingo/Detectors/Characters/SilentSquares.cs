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

    private static readonly PlayerTally SlyTriggeredThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally SlyTriggeredThisTurn = new(TallyScope.Turn);
    private static readonly PlayerTally DiscardedThisTurn = new(TallyScope.Turn);

    // C-SI-E1: Poison ticks deal Unblockable, Unpowered damage with no dealer and no card source
    // (PoisonPower.Trigger), and the Poison power is still on its owner when the killing hit is
    // reported. The kill is credited to whoever applied the Poison; when the game has no applier
    // it goes to this client's player.
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

                PoisonPower? poison = target.GetPower<PoisonPower>();
                if (poison == null)
                    return;

                Player? poisoner = CharacterSquares.PlayerBehind(poison.Applier);
                if (poisoner != null)
                    CharacterSquares.Complete<Silent>(KillWithPoison, poisoner);
                else
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

                if (SlyTriggeredThisCombat.Add(owner) >= 2)
                    BingoSquares.Complete(TriggerTwoSlyInOneCombat, owner);
                if (SlyTriggeredThisTurn.Add(owner) >= 3)
                    BingoSquares.Complete(TriggerThreeSlyInOneTurn, owner);
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
                if (CharacterSquares.Plays<Silent>(owner) && DiscardedThisTurn.Add(owner) >= 8)
                    BingoSquares.Complete(DiscardEightInOneTurn, owner);
            });
        }
    }
}
