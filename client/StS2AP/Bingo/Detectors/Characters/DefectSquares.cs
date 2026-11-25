using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
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
    private const string ChannelAllFiveTypes = "C-DE-H1";

    /// <summary>The five vanilla orbs; modded orbs don't count toward "all 5 orb types".</summary>
    private static readonly HashSet<Type> VanillaOrbTypes = new()
    {
        typeof(LightningOrb), typeof(FrostOrb), typeof(DarkOrb), typeof(PlasmaOrb), typeof(GlassOrb),
    };

    private static readonly PlayerTally EvokedThisTurn = new(TallyScope.Turn);
    private static readonly PlayerTally ChanneledThisCombat = new(TallyScope.Combat);
    private static readonly PlayerSet<Type> OrbTypesChanneledThisCombat = new(TallyScope.Combat);

    /// <summary>The outermost card a player is playing and the evokes it has caused so far.</summary>
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
    // C-DE-M6: evokes while a card of the player's is resolving, counted per card play.
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

                if (EvokedThisTurn.Add(owner) >= 3)
                    BingoSquares.Complete(EvokeThreeInOneTurn, owner);
                if (orb is DarkOrb && orb.EvokeVal >= 50m && targets.Any())
                    BingoSquares.Complete(DarkEvokeForFifty, owner);
                if (CardsInPlay.TryGetValue(owner.NetId, out CardInPlay? cardInPlay) && ++cardInPlay.Evokes >= 4)
                    BingoSquares.Complete(EvokeFourWithOneCard, owner);
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
                if (typesHeld >= 3)
                    BingoSquares.Complete(ThreeOrbTypesAtOnce, player);
                if (ChanneledThisCombat.Add(player) >= 12)
                    BingoSquares.Complete(ChannelTwelveInOneCombat, player);
                if (VanillaOrbTypes.Contains(orb.GetType())
                    && OrbTypesChanneledThisCombat.Add(player, orb.GetType()) >= VanillaOrbTypes.Count)
                    BingoSquares.Complete(ChannelAllFiveTypes, player);
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

            CharacterSquares.Guard($"{nameof(DefectSquares)}.{nameof(PowerAmountChanged)}", () =>
            {
                if (power.Amount >= 7)
                    CharacterSquares.Complete<Defect>(HaveSevenFocus, power.Owner.Player);
            });
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
}
