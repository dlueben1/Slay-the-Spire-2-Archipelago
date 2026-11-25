using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace StS2AP.Bingo.Detectors.Fight;

/// <summary>How an enemy's death came about, once it is known to be a real kill.</summary>
/// <param name="Killer">The player credited with the kill.</param>
/// <param name="Card">The card whose damage landed the killing blow, if any.</param>
/// <param name="Hit">The killing hit; null for Doom kills, which deal no damage.</param>
internal sealed record EnemyKill(Player Killer, CardModel? Card, DamageResult? Hit);

/// <summary>
/// Links an enemy's death back to its cause. The game kills a creature in three ways that
/// matter here: lethal damage (<c>CreatureCmd.Damage</c> reports the hit, then kills every
/// creature it took to 0 HP), Doom (<c>DoomPower.DoomKill</c>, no damage source), and the
/// cascade that kills the remaining minions once the last primary enemy dies. Cascade deaths
/// are nobody's kill, which keeps "kill the summoner while its summons live" (F-M01) apart from
/// "kill 3 minions" (F-M02).
/// </summary>
internal static class KillLog
{
    private sealed record PendingDamageKill(Player? Killer, CardModel? Card, DamageResult Hit);

    private static readonly Dictionary<Creature, PendingDamageKill> PendingDamageKills =
        new(ReferenceEqualityComparer.Instance);

    private static readonly HashSet<Creature> Doomed = new(ReferenceEqualityComparer.Instance);

    public static void Reset()
    {
        PendingDamageKills.Clear();
        Doomed.Clear();
    }

    /// <summary>Called for every hit on an enemy; remembers the hits that took it to 0 HP.</summary>
    public static void RecordHit(Creature target, Player? killer, CardModel? card, DamageResult hit)
    {
        if (hit.WasTargetKilled)
            PendingDamageKills[target] = new PendingDamageKill(killer, card, hit);
        else
            PendingDamageKills.Remove(target);
    }

    /// <summary>
    /// Resolves a death to its kill, or null when no player killed it (a cascade, damage from
    /// another enemy). Clears the creature's pending records either way.
    /// </summary>
    public static EnemyKill? TakeKill(ICombatState combatState, Creature creature)
    {
        bool damageKilled = PendingDamageKills.Remove(creature, out PendingDamageKill? pending);
        bool doomed = Doomed.Remove(creature);
        if (damageKilled)
            return pending!.Killer is { } killer ? new EnemyKill(killer, pending.Card, pending.Hit) : null;
        // Doom kills count for every player in the combat (user ruling): credit this client's player.
        if (doomed && FightCredit.Me(combatState) is { } me)
            return new EnemyKill(me, null, null);
        return null;
    }

    /// <summary>Forgets a death that was prevented (the enemy revives or changes phase).</summary>
    public static void Forget(Creature creature)
    {
        PendingDamageKills.Remove(creature);
        Doomed.Remove(creature);
    }

    [HarmonyPatch(typeof(DoomPower), nameof(DoomPower.DoomKill))]
    private static class DoomKill
    {
        // DoomKill kills each listed creature in turn, so marking them first lets the death
        // hook tell a Doom kill from a cascade.
        [HarmonyPrefix]
        private static void Prefix(IReadOnlyList<Creature> creatures)
        {
            if (BingoSquares.IsActive)
                FightCredit.Guard("doom kill", () => Doomed.UnionWith(creatures));
        }
    }
}

/// <summary>Squares judged when an enemy dies.</summary>
internal static class KillDetectors
{
    public const string FullHpToDeadKey = "F-E09";

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterDeath))]
    private static class EnemyDied
    {
        // A prefix sees the creature before its powers are removed and before the game removes
        // it from combat or kills its minions, which several squares below rely on.
        // The beta hook has an extra card-source parameter; the ones bound here exist in both.
        [HarmonyPrefix]
        private static void Prefix(ICombatState? combatState, Creature creature, bool wasRemovalPrevented)
        {
            if (combatState == null || creature.Side != CombatSide.Enemy || !BingoSquares.IsActive)
                return;
            FightCredit.Guard("enemy death", () =>
            {
                if (wasRemovalPrevented)
                {
                    KillLog.Forget(creature);
                    return;
                }
                if (KillLog.TakeKill(combatState, creature) is { } kill)
                    OnEnemyKilled(combatState, creature, kill);
            });
        }
    }

    private static void OnEnemyKilled(ICombatState combatState, Creature enemy, EnemyKill kill)
    {
        Player killer = kill.Killer;

        // F-E01: the killing hit left no overkill.
        if (kill.Hit is { OverkillDamage: 0 } && BingoSquares.IsOpen("F-E01"))
            BingoSquares.Complete("F-E01", killer);

        // F-E02: the killing hit overkilled by 25 or more.
        if (kill.Hit is { OverkillDamage: >= 25 } && BingoSquares.IsOpen("F-E02"))
            BingoSquares.Complete("F-E02", killer);

        // F-E03: the killer was resolving a potion when its damage killed the enemy.
        if (kill.Hit != null && FightLog.PotionInUse.ContainsKey(killer.NetId) && BingoSquares.IsOpen("F-E03"))
            BingoSquares.Complete("F-E03", killer);

        // F-E04: the enemy's intent, as this client's player sees it, was an attack for 15+.
        if (BingoSquares.IsOpen("F-E04") && IntendedDamageToMe(combatState, enemy) >= 15)
            BingoSquares.Complete("F-E04", killer);

        // F-M04: the kill came in the enemies' turn (Poison, Thorns, Doom...).
        if (combatState.CurrentSide == CombatSide.Enemy && BingoSquares.IsOpen("F-M04"))
            BingoSquares.Complete("F-M04", killer);

        // F-M06: three kills in one turn (players' turn plus the following enemies' turn).
        if (FightLog.KillsThisTurn.Add(killer, 1) >= 3 && BingoSquares.IsOpen("F-M06"))
            BingoSquares.Complete("F-M06", killer);

        // F-M02: three minions killed in one combat; minions dying with their summoner don't count.
        if (enemy.IsSecondaryEnemy && FightLog.MinionKillsThisCombat.Add(killer, 1) >= 3 && BingoSquares.IsOpen("F-M02"))
            BingoSquares.Complete("F-M02", killer);

        // F-M09: the enemy had 10+ Strength when it died.
        if (enemy.GetPowerAmount<StrengthPower>() >= 10 && BingoSquares.IsOpen("F-M09"))
            BingoSquares.Complete("F-M09", killer);

        bool wasLastPrimary = enemy.IsPrimaryEnemy && !OtherLivingEnemies(combatState, enemy).Any(other => other.IsPrimaryEnemy);

        // F-M01: the last primary enemy died while minions were still alive; the game then kills
        // the minions and the combat is won.
        if (wasLastPrimary && OtherLivingEnemies(combatState, enemy).Any(other => other.IsSecondaryEnemy)
            && BingoSquares.IsOpen("F-M01"))
        {
            BingoSquares.Complete("F-M01", killer);
        }

        // F-E10: a Strike card's damage killed the last boss standing.
        if (wasLastPrimary && kill.Card != null && kill.Card.Tags.Contains(CardTag.Strike)
            && FightCredit.IsBoss(combatState, enemy) && BingoSquares.IsOpen("F-E10"))
        {
            BingoSquares.Complete("F-E10", kill.Card.Owner);
        }

        if (kill.Card != null)
            OnCardKill(enemy, kill.Card);
    }

    private static void OnCardKill(Creature enemy, CardModel card)
    {
        // E08: the card's damage killed a second enemy during the same play.
        if (FightLog.KillsByPlayingCard.TryGetValue(card, out int kills))
        {
            FightLog.KillsByPlayingCard[card] = kills + 1;
            if (kills + 1 >= 2 && BingoSquares.IsOpen("E08"))
                BingoSquares.Complete("E08", card.Owner);
        }

        // F-E09: the enemy had 40+ Max HP and full HP when the card began to be played.
        if (FightLog.FullHpBigEnemiesAtPlay.TryGetValue(card, out HashSet<Creature>? fullHpEnemies)
            && fullHpEnemies.Contains(enemy) && BingoSquares.IsOpen(FullHpToDeadKey))
        {
            BingoSquares.Complete(FullHpToDeadKey, card.Owner);
        }
    }

    // The dying creature is still listed among the enemies until after the death hook.
    private static IEnumerable<Creature> OtherLivingEnemies(ICombatState combatState, Creature enemy) =>
        combatState.Enemies.Where(other => !ReferenceEquals(other, enemy) && other.IsAlive);

    /// <summary>
    /// The total damage of the enemy's next move's attacks. The game computes intent damage
    /// against this client's player (<c>AttackIntent.GetSingleDamage</c>), which is "you".
    /// </summary>
    private static int IntendedDamageToMe(ICombatState combatState, Creature enemy)
    {
        if (enemy.Monster == null)
            return 0;
        IEnumerable<Creature> targets = combatState.Players.Select(player => player.Creature);
        return enemy.Monster.NextMove.Intents
            .OfType<AttackIntent>()
            .Sum(intent => intent.GetTotalDamage(targets, enemy));
    }
}
