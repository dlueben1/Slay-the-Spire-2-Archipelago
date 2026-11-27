using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;

namespace StS2AP.Bingo.Detectors.Characters;

/// <summary>Necrobinder squares. "Win as Necrobinder" is in <see cref="CharacterWinSquares"/>.</summary>
internal static class NecrobinderSquares
{
    private const string HaveThirtyDoom = "C-NE-E1";
    private const string PlayThreeSouls = "C-NE-E3";
    private const string OstyTo25MaxHp = "C-NE-E4";
    private const string OstyTo45MaxHp = "C-NE-M1";
    private const string KillThreeWithDoom = "C-NE-M2";
    private const string PlayThirteenEthereal = "C-NE-M3";
    private const string HaveNineEnergy = "C-NE-M4";
    private const string HaveThreeHundredDoom = "C-NE-H2";
    private const string OstyHitsFor100 = "C-NE-H3";
    private const string OstyTo100MaxHp = "C-NE-H5";
    private const string SacrificeOstyWith67MaxHp = "C-NE-H6";
    private const string PlayTwentyEthereal = "C-NE-H7";

    private static readonly PlayerTally SoulsPlayedThisCombat = new(TallyScope.Combat);
    private static readonly PlayerTally EtherealPlayedThisCombat = new(TallyScope.Combat);

    /// <summary>
    /// Players (by NetId) whose Sacrifice is resolving. Sacrifice kills Osty with
    /// <c>CreatureCmd.Kill</c>, which carries no card in game 0.111.1, so the death is matched to
    /// the card by timing. Cleared at combat start too, because a play that ends the combat never
    /// reaches <see cref="Hook.AfterCardPlayed"/>.
    /// </summary>
    private static readonly HashSet<ulong> SacrificeResolving = new();

    /// <summary>
    /// Enemies killed by Doom this combat. Doom kills count for every player (contract ruling),
    /// so this is one shared set, not a per-player tally.
    /// </summary>
    private static readonly HashSet<Creature> DoomKillsThisCombat = new();

    static NecrobinderSquares()
    {
        TallyBoundaries.Register(TallyScope.Combat, DoomKillsThisCombat.Clear);
        TallyBoundaries.Register(TallyScope.Combat, SacrificeResolving.Clear);
    }

    // C-NE-E1, C-NE-H2: the Doom on one enemy after a change, credited to whoever applied that
    // stack (as in the game's 999-Doom achievement). Without a known applier it goes to this
    // client's player.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPowerAmountChanged))]
    private static class PowerAmountChanged
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState combatState, PowerModel power, Creature? applier)
        {
            if (power is not DoomPower
                || (!BingoSquares.IsOpen(HaveThirtyDoom) && !BingoSquares.IsOpen(HaveThreeHundredDoom)))
                return;

            CharacterSquares.Guard($"{nameof(NecrobinderSquares)}.{nameof(PowerAmountChanged)}", () =>
            {
                if (power.Owner.Side != CombatSide.Enemy)
                    return;

                ReachForApplier(HaveThirtyDoom, applier, combatState, power.Amount, 30);
                ReachForApplier(HaveThreeHundredDoom, applier, combatState, power.Amount, 300);
            });
        }

        private static void ReachForApplier(string key, Creature? applier, ICombatState combatState, int doom, int target)
        {
            Player? player = CharacterSquares.PlayerBehind(applier);
            if (player != null)
                CharacterSquares.Reach<Necrobinder>(key, player, doom, target);
            else
                CharacterSquares.ReachForLocalPlayer<Necrobinder>(key, combatState, doom, target);
        }
    }

    // C-NE-E4, C-NE-M1, C-NE-H5: Osty's Max HP after a summon. A summon onto a living Osty raises
    // its Max HP; a revive sets Max HP to the summon amount, so the squares reward keeping Osty
    // alive. Bound Phylactery's summon of 1 each turn counts like any other.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterSummon))]
    private static class Summoned
    {
        [HarmonyPrefix]
        private static void Prefix(Player summoner)
        {
            if (!BingoSquares.IsOpen(OstyTo25MaxHp)
                && !BingoSquares.IsOpen(OstyTo45MaxHp)
                && !BingoSquares.IsOpen(OstyTo100MaxHp))
                return;

            CharacterSquares.Guard($"{nameof(NecrobinderSquares)}.{nameof(Summoned)}", () =>
            {
                if (summoner.Osty is not { IsAlive: true } osty)
                    return;

                CharacterSquares.Reach<Necrobinder>(OstyTo25MaxHp, summoner, osty.MaxHp, 25);
                CharacterSquares.Reach<Necrobinder>(OstyTo45MaxHp, summoner, osty.MaxHp, 45);
                CharacterSquares.Reach<Necrobinder>(OstyTo100MaxHp, summoner, osty.MaxHp, 100);
            });
        }
    }

    // C-NE-M2: enemies DoomPower.DoomKill killed, counted once each across the combat and credited
    // to every player.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterDiedToDoom))]
    private static class DiedToDoom
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState combatState, IReadOnlyList<Creature> creatures)
        {
            if (!BingoSquares.IsOpen(KillThreeWithDoom))
                return;

            CharacterSquares.Guard($"{nameof(NecrobinderSquares)}.{nameof(DiedToDoom)}", () =>
            {
                foreach (Creature creature in creatures)
                {
                    if (creature.Side == CombatSide.Enemy && creature.IsDead)
                        DoomKillsThisCombat.Add(creature);
                }
                CharacterSquares.ReachForLocalPlayer<Necrobinder>(
                    KillThreeWithDoom, combatState, DoomKillsThisCombat.Count, 3);
            });
        }
    }

    // C-NE-M3, C-NE-H7: Ethereal cards played this combat, counted when the play starts so the
    // card that ends the combat still counts. A replayed card counts once.
    // C-NE-H6: marks the player's Sacrifice as resolving, for OstyDying below.
    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardPlayed))]
    private static class CardPlayStarting
    {
        [HarmonyPrefix]
        private static void Prefix(CardPlay cardPlay)
        {
            bool etherealOpen = BingoSquares.IsOpen(PlayThirteenEthereal) || BingoSquares.IsOpen(PlayTwentyEthereal);
            bool sacrificeOpen = BingoSquares.IsOpen(SacrificeOstyWith67MaxHp);
            if (!etherealOpen && !sacrificeOpen)
                return;

            CharacterSquares.Guard($"{nameof(NecrobinderSquares)}.{nameof(CardPlayStarting)}", () =>
            {
                Player player = cardPlay.Player;
                if (!CharacterSquares.Plays<Necrobinder>(player))
                    return;

                if (sacrificeOpen && cardPlay.Card is Sacrifice)
                    SacrificeResolving.Add(player.NetId);

                if (etherealOpen
                    && cardPlay.IsFirstInSeries
                    && cardPlay.Card.Keywords.Contains(CardKeyword.Ethereal))
                {
                    int played = EtherealPlayedThisCombat.Add(player);
                    CharacterSquares.Reach<Necrobinder>(PlayThirteenEthereal, player, played, 13);
                    CharacterSquares.Reach<Necrobinder>(PlayTwentyEthereal, player, played, 20);
                }
            });
        }
    }

    // C-NE-H6: Osty's Max HP as the player's own Sacrifice kills it. Bone Shards also kills Osty
    // after its attack, but that isn't a Sacrifice, so it doesn't count.
    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeDeath))]
    private static class OstyDying
    {
        [HarmonyPrefix]
        private static void Prefix(Creature creature)
        {
            if (SacrificeResolving.Count == 0
                || creature.Monster is not Osty
                || !BingoSquares.IsOpen(SacrificeOstyWith67MaxHp))
                return;

            CharacterSquares.Guard($"{nameof(NecrobinderSquares)}.{nameof(OstyDying)}", () =>
            {
                Player? owner = creature.PetOwner;
                if (owner != null && SacrificeResolving.Contains(owner.NetId))
                    CharacterSquares.Reach<Necrobinder>(SacrificeOstyWith67MaxHp, owner, creature.MaxHp, 67);
            });
        }
    }

    // C-NE-M4: the player's energy whenever it changes (turn refill, gains, spending). Every energy
    // change in combat goes through this setter.
    [HarmonyPatch(typeof(PlayerCombatState), nameof(PlayerCombatState.Energy), MethodType.Setter)]
    private static class EnergySet
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerCombatState __instance)
        {
            if (!BingoSquares.IsOpen(HaveNineEnergy))
                return;

            CharacterSquares.Guard($"{nameof(NecrobinderSquares)}.{nameof(EnergySet)}",
                () => CharacterSquares.Reach<Necrobinder>(HaveNineEnergy, __instance._player, __instance.Energy, 9));
        }
    }

    // C-NE-E3: plays of the Soul card. A replayed card counts once.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
    private static class CardPlayed
    {
        [HarmonyPrefix]
        private static void Prefix(CardPlay cardPlay)
        {
            if (SacrificeResolving.Count > 0 && cardPlay.Card is Sacrifice)
                SacrificeResolving.Remove(cardPlay.Player.NetId);

            if (!BingoSquares.IsOpen(PlayThreeSouls))
                return;

            CharacterSquares.Guard($"{nameof(NecrobinderSquares)}.{nameof(CardPlayed)}", () =>
            {
                Player player = cardPlay.Player;
                if (cardPlay.IsFirstInSeries
                    && cardPlay.Card is Soul
                    && CharacterSquares.Plays<Necrobinder>(player))
                    BingoSquares.Reach(PlayThreeSouls, player, SoulsPlayedThisCombat.Add(player), 3);
            });
        }
    }

    // C-NE-H3: one hit by Osty (AttackCommand.FromOsty makes Osty the dealer), credited to Osty's
    // owner. A hit is measured in full: Block absorbed, HP taken and overkill.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageGiven))]
    private static class DamageGiven
    {
        [HarmonyPrefix]
        private static void Prefix(Creature? dealer, DamageResult results)
        {
            if (dealer?.Monster is not Osty || !BingoSquares.IsOpen(OstyHitsFor100))
                return;

            CharacterSquares.Guard($"{nameof(NecrobinderSquares)}.{nameof(DamageGiven)}",
                () => CharacterSquares.Reach<Necrobinder>(
                    OstyHitsFor100, dealer.PetOwner, CharacterSquares.HitDamage(results), 100));
        }
    }
}
