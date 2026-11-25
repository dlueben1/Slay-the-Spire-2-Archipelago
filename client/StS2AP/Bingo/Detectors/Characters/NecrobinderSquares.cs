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
    private const string ReviveOsty = "C-NE-E2";
    private const string PlayThreeSouls = "C-NE-E3";
    private const string OstyTo25MaxHp = "C-NE-M1";
    private const string KillThreeWithDoom = "C-NE-M2";
    private const string HaveThreeHundredDoom = "C-NE-H2";
    private const string OstyHitsFor100 = "C-NE-H3";

    private static readonly PlayerTally SoulsPlayedThisCombat = new(TallyScope.Combat);

    /// <summary>
    /// Enemies killed by Doom this combat. Doom kills count for every player (contract ruling),
    /// so this is one shared set, not a per-player tally.
    /// </summary>
    private static readonly HashSet<Creature> DoomKillsThisCombat = new();

    static NecrobinderSquares() => TallyBoundaries.Register(TallyScope.Combat, DoomKillsThisCombat.Clear);

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

                if (power.Amount >= 30)
                    CompleteForApplier(HaveThirtyDoom, applier, combatState);
                if (power.Amount >= 300)
                    CompleteForApplier(HaveThreeHundredDoom, applier, combatState);
            });
        }

        private static void CompleteForApplier(string key, Creature? applier, ICombatState combatState)
        {
            Player? player = CharacterSquares.PlayerBehind(applier);
            if (player != null)
                CharacterSquares.Complete<Necrobinder>(key, player);
            else
                CharacterSquares.CompleteForLocalPlayer<Necrobinder>(key, combatState);
        }
    }

    // C-NE-E2: OstyCmd.Summon raises this only when it brings back an Osty that died this combat,
    // never for the first summon.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterOstyRevived))]
    private static class OstyRevived
    {
        [HarmonyPrefix]
        private static void Prefix(Creature osty)
        {
            if (!BingoSquares.IsOpen(ReviveOsty))
                return;

            CharacterSquares.Guard($"{nameof(NecrobinderSquares)}.{nameof(OstyRevived)}",
                () => CharacterSquares.Complete<Necrobinder>(ReviveOsty, osty.PetOwner));
        }
    }

    // C-NE-M1: Osty's Max HP after a summon. A summon onto a living Osty raises its Max HP; a
    // revive sets Max HP to the summon amount, so the square rewards keeping Osty alive.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterSummon))]
    private static class Summoned
    {
        [HarmonyPrefix]
        private static void Prefix(Player summoner)
        {
            if (!BingoSquares.IsOpen(OstyTo25MaxHp))
                return;

            CharacterSquares.Guard($"{nameof(NecrobinderSquares)}.{nameof(Summoned)}", () =>
            {
                if (summoner.Osty is { IsAlive: true, MaxHp: >= 25 })
                    CharacterSquares.Complete<Necrobinder>(OstyTo25MaxHp, summoner);
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
                if (DoomKillsThisCombat.Count >= 3)
                    CharacterSquares.CompleteForLocalPlayer<Necrobinder>(KillThreeWithDoom, combatState);
            });
        }
    }

    // C-NE-E3: plays of the Soul card.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
    private static class CardPlayed
    {
        [HarmonyPrefix]
        private static void Prefix(CardPlay cardPlay)
        {
            if (!BingoSquares.IsOpen(PlayThreeSouls))
                return;

            CharacterSquares.Guard($"{nameof(NecrobinderSquares)}.{nameof(CardPlayed)}", () =>
            {
                Player player = cardPlay.Player;
                if (cardPlay.Card is Soul
                    && CharacterSquares.Plays<Necrobinder>(player)
                    && SoulsPlayedThisCombat.Add(player) >= 3)
                    BingoSquares.Complete(PlayThreeSouls, player);
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

            CharacterSquares.Guard($"{nameof(NecrobinderSquares)}.{nameof(DamageGiven)}", () =>
            {
                if (CharacterSquares.HitDamage(results) >= 100)
                    CharacterSquares.Complete<Necrobinder>(OstyHitsFor100, dealer.PetOwner);
            });
        }
    }
}
