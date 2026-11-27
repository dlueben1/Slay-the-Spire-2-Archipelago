using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;

namespace StS2AP.Bingo.Detectors.Characters;

/// <summary>Defect squares. "Win as Defect" is in <see cref="CharacterWinSquares"/>.</summary>
internal static class DefectSquares
{
    private const string EvokeThreeInOneTurn = "C-DE-E1";
    private const string ThreeOrbTypesAtOnce = "C-DE-M1";
    private const string HaveSevenFocus = "C-DE-M2";
    private const string ChannelTwelveInOneCombat = "C-DE-M3";
    private const string DarkEvokeForFifty = "C-DE-M5";
    private const string EvokeFourWithOneCard = "C-DE-M6";
    private const string AddTenStatusCardsInOneCombat = "C-DE-M7";
    private const string ChannelAllFiveTypes = "C-DE-H1";
    private const string HaveTenOrbSlots = "C-DE-H3";

    /// <summary>The five vanilla orbs; modded orbs don't count toward "all 5 orb types".</summary>
    private static readonly HashSet<Type> VanillaOrbTypes = new()
    {
        typeof(LightningOrb), typeof(FrostOrb), typeof(DarkOrb), typeof(PlasmaOrb), typeof(GlassOrb),
    };

    private static readonly PlayerTally EvokedThisTurn = new(TallyScope.Turn);
    private static readonly PlayerTally ChanneledThisCombat = new(TallyScope.Combat);
    private static readonly PlayerSet<Type> OrbTypesChanneledThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally StatusCardsAddedThisCombat = new(TallyScope.Combat);

    /// <summary>The outermost card a player is playing and how many evokes it has caused so far.</summary>
    private sealed class CardInPlay(CardModel card)
    {
        public CardModel Card { get; } = card;
        public int Evokes { get; set; }
    }

    /// <summary>
    /// Each player's outermost card play, by NetId. Cards played from inside another card's
    /// play (auto-plays) belong to the outer card, as in the game's Sly achievement.
    /// </summary>
    private static readonly Dictionary<ulong, CardInPlay> CardsInPlay = new();

    static DefectSquares() => TallyBoundaries.Register(TallyScope.Combat, CardsInPlay.Clear);

    // C-DE-E1: every evoke counts, so Dualcast's double evoke of one orb counts twice.
    // C-DE-M5: the Dark orb's evoke value (what it deals before the target's modifiers), and only
    // when it had a target to hit.
    // C-DE-M6: every evoke while a card of the player's is resolving, per card play, so evoking
    // the same orb several times (Quadcast, Multi-Cast) counts each time.
    // AfterOrbEvoked runs before the evoked orb is detached, so its Owner is still set.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterOrbEvoked))]
    private static class OrbEvoked
    {
        [HarmonyPrefix]
        private static void Prefix(OrbModel orb, IEnumerable<Creature> targets)
        {
            if (!BingoSquares.IsOpen(EvokeThreeInOneTurn)
                && !BingoSquares.IsOpen(DarkEvokeForFifty)
                && !BingoSquares.IsOpen(EvokeFourWithOneCard))
                return;

            CharacterSquares.Guard($"{nameof(DefectSquares)}.{nameof(OrbEvoked)}", () =>
            {
                Player owner = orb.Owner;
                if (!CharacterSquares.Plays<Defect>(owner))
                    return;

                BingoSquares.Reach(EvokeThreeInOneTurn, owner, EvokedThisTurn.Add(owner), 3);
                // The evoke value is a decimal; truncating it keeps "50 or more" exactly as before.
                if (orb is DarkOrb && targets.Any())
                    BingoSquares.Reach(DarkEvokeForFifty, owner, (int)orb.EvokeVal, 50);
                if (CardsInPlay.TryGetValue(owner.NetId, out CardInPlay? cardInPlay))
                    BingoSquares.Reach(EvokeFourWithOneCard, owner, ++cardInPlay.Evokes, 4);
            });
        }
    }

    // C-DE-M1: the orb slots hold 3 different orb types right after a channel.
    // C-DE-M3: every channel counts. C-DE-H1: all five vanilla orb types channelled this combat.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterOrbChanneled))]
    private static class OrbChanneled
    {
        [HarmonyPrefix]
        private static void Prefix(Player player, OrbModel orb)
        {
            if (!BingoSquares.IsOpen(ThreeOrbTypesAtOnce)
                && !BingoSquares.IsOpen(ChannelTwelveInOneCombat)
                && !BingoSquares.IsOpen(ChannelAllFiveTypes))
                return;

            CharacterSquares.Guard($"{nameof(DefectSquares)}.{nameof(OrbChanneled)}", () =>
            {
                if (!CharacterSquares.Plays<Defect>(player))
                    return;

                int typesHeld = player.PlayerCombatState?.OrbQueue.Orbs.Select(o => o.GetType()).Distinct().Count() ?? 0;
                BingoSquares.Reach(ThreeOrbTypesAtOnce, player, typesHeld, 3);
                BingoSquares.Reach(ChannelTwelveInOneCombat, player, ChanneledThisCombat.Add(player), 12);
                if (VanillaOrbTypes.Contains(orb.GetType()))
                {
                    int typesChanneled = OrbTypesChanneledThisCombat.Add(player, orb.GetType());
                    BingoSquares.Reach(ChannelAllFiveTypes, player, typesChanneled, VanillaOrbTypes.Count);
                }
            });
        }
    }

    // C-DE-M2: the player's own Focus reaching 7, from any source.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPowerAmountChanged))]
    private static class PowerAmountChanged
    {
        [HarmonyPrefix]
        private static void Prefix(PowerModel power)
        {
            if (power is not FocusPower || !BingoSquares.IsOpen(HaveSevenFocus))
                return;

            CharacterSquares.Guard($"{nameof(DefectSquares)}.{nameof(PowerAmountChanged)}",
                () => CharacterSquares.Reach<Defect>(HaveSevenFocus, power.Owner.Player, power.Amount, 7));
        }
    }

    // C-DE-M6 bracket: a card play opens on the player's first BeforeCardPlayed and closes on that
    // same card's AfterCardPlayed. Each play of a replayed card is its own bracket.
    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardPlayed))]
    private static class CardPlayStarting
    {
        [HarmonyPrefix]
        private static void Prefix(CardPlay cardPlay)
        {
            if (!BingoSquares.IsOpen(EvokeFourWithOneCard))
                return;

            CharacterSquares.Guard($"{nameof(DefectSquares)}.{nameof(CardPlayStarting)}", () =>
            {
                Player player = cardPlay.Player;
                if (CharacterSquares.Plays<Defect>(player) && !CardsInPlay.ContainsKey(player.NetId))
                    CardsInPlay[player.NetId] = new CardInPlay(cardPlay.Card);
            });
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
    private static class CardPlayFinished
    {
        [HarmonyPrefix]
        private static void Prefix(CardPlay cardPlay)
        {
            if (CardsInPlay.Count == 0)
                return;

            CharacterSquares.Guard($"{nameof(DefectSquares)}.{nameof(CardPlayFinished)}", () =>
            {
                ulong netId = cardPlay.Player.NetId;
                if (CardsInPlay.TryGetValue(netId, out CardInPlay? cardInPlay) && cardInPlay.Card == cardPlay.Card)
                    CardsInPlay.Remove(netId);
            });
        }
    }

    // C-DE-M7: Status cards generated into the player's own piles by the player. The game passes
    // the player as the creator when their cards, powers, relics or potions generate a card
    // (Overclock, Turbo, Boost Away, Tea of Discourtesy), and null when an enemy or an enemy's
    // power does (Slimed, Dazed, Wounds), so the creator tells the two apart.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardGeneratedForCombat))]
    private static class CardGenerated
    {
        [HarmonyPrefix]
        private static void Prefix(CardModel card, Player? creator)
        {
            if (card.Type != CardType.Status || !BingoSquares.IsOpen(AddTenStatusCardsInOneCombat))
                return;

            CharacterSquares.Guard($"{nameof(DefectSquares)}.{nameof(CardGenerated)}", () =>
            {
                Player owner = card.Owner;
                if (creator != owner || !CharacterSquares.Plays<Defect>(owner))
                    return;

                BingoSquares.Reach(AddTenStatusCardsInOneCombat, owner,
                    StatusCardsAddedThisCombat.Add(owner), 10);
            });
        }
    }

    // C-DE-H3: the orb slot count after any slot gain. The game caps it at 10
    // (OrbQueue.maxCapacity), so this square means reaching the cap. AddSlots is synchronous,
    // so the postfix sees the new capacity.
    [HarmonyPatch(typeof(OrbCmd), nameof(OrbCmd.AddSlots))]
    private static class SlotsAdded
    {
        [HarmonyPostfix]
        private static void Postfix(Player player)
        {
            if (!BingoSquares.IsOpen(HaveTenOrbSlots))
                return;

            CharacterSquares.Guard($"{nameof(DefectSquares)}.{nameof(SlotsAdded)}", () =>
                CharacterSquares.Reach<Defect>(HaveTenOrbSlots, player,
                    player.PlayerCombatState?.OrbQueue.Capacity ?? 0, OrbQueue.maxCapacity));
        }
    }
}
