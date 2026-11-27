#if STS2_AT_LEAST_0_114_0
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
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
    private const string ArrowHitFor60 = "C-FO-M4";
    private const string ArrowHitFor150 = "C-FO-H1";
    private const string GenerateTenSadness = "C-FO-H3";
    private const string GenerateSevenJoy = "C-FO-H4";

    private static readonly PlayerTally ArrowsRetrievedThisCombat = new(TallyScope.Combat);
    private static readonly PlayerSet<Creature> EnemiesImpaledThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally ArrowsShotThisTurn = new(TallyScope.Turn);
    private static readonly PlayerTally SadnessGeneratedThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally JoyGeneratedThisCombat = new(TallyScope.Combat);

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
                if (CharacterSquares.Plays<Fool>(owner))
                    BingoSquares.Reach(RetrieveTwoArrows, owner, ArrowsRetrievedThisCombat.Add(owner), 2);
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
                if (CharacterSquares.Plays<Fool>(owner))
                    BingoSquares.Reach(ImpaleTwoEnemies, owner, EnemiesImpaledThisCombat.Add(owner, power.Owner), 2);
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
                if (CharacterSquares.Plays<Fool>(owner))
                    BingoSquares.Reach(ShootThreeInOneTurn, owner, ArrowsShotThisTurn.Add(owner), 3);
            });
        }
    }

    // C-FO-M1: after a melody card resolves, the game's own MelodyCmd count of the distinct
    // melodies (Wah, Cho, Ki) played this turn, from the combat history; all three complete it.
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
                if (isMelody && CharacterSquares.Plays<Fool>(player))
                    BingoSquares.Reach(PlayWahChoKi, player, MelodyCmd.MelodiesThisTurn(player), 3);
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

            CharacterSquares.Guard($"{nameof(FoolSquares)}.{nameof(PowerAmountChanged)}",
                () => CharacterSquares.Reach<Fool>(HaveTwentyAim, power.Owner.Player, power.Amount, 20));
        }
    }

    // C-FO-M4, C-FO-H1: one hit carrying ValueProp.FromArrow (every arrow-shot card sets it),
    // measured in full: Block absorbed, HP taken and overkill. Multi-hit arrows (Twin Shot, Repeat
    // Shot) are judged per hit, not summed.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageGiven))]
    private static class DamageGiven
    {
        [HarmonyPrefix]
        private static void Prefix(Creature? dealer, DamageResult results, ValueProp props, CardModel? cardSource)
        {
            if (!props.HasFlag(ValueProp.FromArrow)
                || (!BingoSquares.IsOpen(ArrowHitFor60) && !BingoSquares.IsOpen(ArrowHitFor150)))
                return;

            CharacterSquares.Guard($"{nameof(FoolSquares)}.{nameof(DamageGiven)}", () =>
            {
                Player? shooter = dealer?.Player ?? cardSource?.Owner;
                int damage = CharacterSquares.HitDamage(results);
                CharacterSquares.Reach<Fool>(ArrowHitFor60, shooter, damage, 60);
                CharacterSquares.Reach<Fool>(ArrowHitFor150, shooter, damage, 150);
            });
        }
    }

    // C-FO-H3, C-FO-H4: Sadness and Joy cards generated this combat. Every way they appear raises
    // this hook: added to a pile (Jest, Rummage, Ennui, Bizarre Lamp, Crystalline Euphoria) or
    // transformed into (Mourning Blow becoming Sadness; Punch of Joy and Ecstasy turning Sadness
    // into Joy). They count for the player who made them, which is not always the card's owner:
    // Self Deprecate gives Joy to teammates and Crystalline Euphoria can be thrown at one.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardGeneratedForCombat))]
    private static class CardGenerated
    {
        [HarmonyPrefix]
        private static void Prefix(CardModel card, Player? creator)
        {
            if (card is Sadness)
            {
                if (BingoSquares.IsOpen(GenerateTenSadness))
                    CharacterSquares.Guard($"{nameof(FoolSquares)}.{nameof(CardGenerated)}",
                        () => ReachGenerated(GenerateTenSadness, SadnessGeneratedThisCombat, creator ?? card.Owner, 10));
            }
            else if (card is Joy)
            {
                if (BingoSquares.IsOpen(GenerateSevenJoy))
                    CharacterSquares.Guard($"{nameof(FoolSquares)}.{nameof(CardGenerated)}",
                        () => ReachGenerated(GenerateSevenJoy, JoyGeneratedThisCombat, creator ?? card.Owner, 7));
            }
        }

        private static void ReachGenerated(string key, PlayerTally generatedThisCombat, Player player, int target)
        {
            if (CharacterSquares.Plays<Fool>(player))
                BingoSquares.Reach(key, player, generatedThisCombat.Add(player), target);
        }
    }
}
#endif
