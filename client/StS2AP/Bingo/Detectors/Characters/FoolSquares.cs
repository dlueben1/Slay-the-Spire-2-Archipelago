#if STS2_AT_LEAST_0_114_0
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace StS2AP.Bingo.Detectors.Characters;

/// <summary>
/// Fool squares (game 0.114.0 and later: the Fool, arrows, Aim and melodies don't exist before).
/// "Win as Fool" is in <see cref="CharacterWinSquares"/>.
/// </summary>
internal static class FoolSquares
{
    private const string RetrieveTwoArrows = "C-FO-E1";
    private const string ImpaleTwoEnemies = "C-FO-E2";
    private const string PlayWahChoKi = "C-FO-M1";
    private const string ShootThreeInOneTurn = "C-FO-M2";
    private const string HaveTwentyAim = "C-FO-M3";
    private const string ArrowHitFor60 = "C-FO-H1";

    private static readonly PlayerTally ArrowsRetrievedThisCombat = new(TallyScope.Combat);
    private static readonly PlayerSet<Creature> EnemiesImpaledThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally ArrowsShotThisTurn = new(TallyScope.Turn);

    // C-FO-E1: an impaled arrow comes back to its owner through ArrowCmd.DislodgeArrow: after
    // three hits, when the impaled enemy dies, or through Recall-style cards. Each counts.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterArrowDislodged))]
    private static class ArrowDislodged
    {
        [HarmonyPrefix]
        private static void Prefix(ArrowSlot arrow)
        {
            if (!BingoSquares.IsOpen(RetrieveTwoArrows))
                return;

            CharacterSquares.Guard($"{nameof(FoolSquares)}.{nameof(ArrowDislodged)}", () =>
            {
                Player owner = arrow.owner;
                if (CharacterSquares.Plays<Fool>(owner) && ArrowsRetrievedThisCombat.Add(owner) >= 2)
                    BingoSquares.Complete(RetrieveTwoArrows, owner);
            });
        }
    }

    // C-FO-E2: distinct enemies the player's arrows impaled this combat. The ImpaledPower sits on
    // the impaled enemy.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterArrowImpaled))]
    private static class ArrowImpaled
    {
        [HarmonyPrefix]
        private static void Prefix(ImpaledPower power, ArrowSlot arrow)
        {
            if (!BingoSquares.IsOpen(ImpaleTwoEnemies))
                return;

            CharacterSquares.Guard($"{nameof(FoolSquares)}.{nameof(ArrowImpaled)}", () =>
            {
                Player owner = arrow.owner;
                if (CharacterSquares.Plays<Fool>(owner) && EnemiesImpaledThisCombat.Add(owner, power.Owner) >= 2)
                    BingoSquares.Complete(ImpaleTwoEnemies, owner);
            });
        }
    }

    // C-FO-M2: arrows shot this turn. The hook also fires when the player had no arrow to shoot
    // (arrow is null); those don't count.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterArrowShot))]
    private static class ArrowShot
    {
        [HarmonyPrefix]
        private static void Prefix(Player owner, ArrowSlot? arrow)
        {
            if (arrow == null || !BingoSquares.IsOpen(ShootThreeInOneTurn))
                return;

            CharacterSquares.Guard($"{nameof(FoolSquares)}.{nameof(ArrowShot)}", () =>
            {
                if (CharacterSquares.Plays<Fool>(owner) && ArrowsShotThisTurn.Add(owner) >= 3)
                    BingoSquares.Complete(ShootThreeInOneTurn, owner);
            });
        }
    }

    // C-FO-M1: after a melody card resolves, the game's own MelodyCmd count of Wah, Cho and Ki
    // played this turn (from the combat history) reaches all three.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
    private static class CardPlayed
    {
        [HarmonyPrefix]
        private static void Prefix(CardPlay cardPlay)
        {
            if (!BingoSquares.IsOpen(PlayWahChoKi))
                return;

            CharacterSquares.Guard($"{nameof(FoolSquares)}.{nameof(CardPlayed)}", () =>
            {
                Player player = cardPlay.Player;
                bool isMelody = cardPlay.Card.Tags.Any(tag => tag is CardTag.Wah or CardTag.Cho or CardTag.Ki);
                if (isMelody && CharacterSquares.Plays<Fool>(player) && MelodyCmd.MelodiesThisTurn(player) >= 3)
                    BingoSquares.Complete(PlayWahChoKi, player);
            });
        }
    }

    // C-FO-M3: the player's own Aim reaching 20.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPowerAmountChanged))]
    private static class PowerAmountChanged
    {
        [HarmonyPrefix]
        private static void Prefix(PowerModel power)
        {
            if (power is not AimPower || !BingoSquares.IsOpen(HaveTwentyAim))
                return;

            CharacterSquares.Guard($"{nameof(FoolSquares)}.{nameof(PowerAmountChanged)}", () =>
            {
                if (power.Amount >= 20)
                    CharacterSquares.Complete<Fool>(HaveTwentyAim, power.Owner.Player);
            });
        }
    }

    // C-FO-H1: one hit carrying ValueProp.FromArrow (every arrow-shot card sets it), measured in
    // full: Block absorbed, HP taken and overkill. Multi-hit arrows (Twin Shot, Repeat Shot) are
    // judged per hit, not summed.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageGiven))]
    private static class DamageGiven
    {
        [HarmonyPrefix]
        private static void Prefix(Creature? dealer, DamageResult results, ValueProp props, CardModel? cardSource)
        {
            if (!props.HasFlag(ValueProp.FromArrow) || !BingoSquares.IsOpen(ArrowHitFor60))
                return;

            CharacterSquares.Guard($"{nameof(FoolSquares)}.{nameof(DamageGiven)}", () =>
            {
                Player? shooter = dealer?.Player ?? cardSource?.Owner;
                if (CharacterSquares.HitDamage(results) >= 60)
                    CharacterSquares.Complete<Fool>(ArrowHitFor60, shooter);
            });
        }
    }
}
#endif
