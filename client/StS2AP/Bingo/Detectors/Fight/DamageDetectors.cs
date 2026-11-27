using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace StS2AP.Bingo.Detectors.Fight;

/// <summary>
/// Squares judged from each hit on an enemy. <c>Hook.AfterDamageGiven</c> runs once per
/// damaged creature with the finished <see cref="DamageResult"/>, after Block and HP have
/// changed and before any killed creature is removed.
/// </summary>
/// <remarks>
/// "Damage dealt" is the number the game shows: HP lost plus overkill. Damage soaked by Block
/// isn't dealt. Boss percentages count HP lost only, against the boss's (multiplayer-scaled)
/// Max HP.
/// </remarks>
internal static class DamageDetectors
{
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageGiven))]
    private static class EnemyHit
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState combatState, Creature? dealer, DamageResult results, Creature target, CardModel? cardSource)
        {
            if (target.Side != CombatSide.Enemy || !BingoSquares.IsActive)
                return;
            FightCredit.Guard("enemy hit", () => OnEnemyHit(combatState, dealer, results, target, cardSource));
        }
    }

    private static void OnEnemyHit(ICombatState combatState, Creature? dealer, DamageResult hit, Creature target, CardModel? cardSource)
    {
        Player? attacker = FightCredit.ForDamage(combatState, dealer, target);
        KillLog.RecordHit(target, attacker, cardSource, hit);
        if (attacker == null)
            return;

        int damageDealt = hit.UnblockedDamage + hit.OverkillDamage;
        // A hit has a dealer (a player, Osty, a player's orb or potion); Poison ticks don't.
        bool isHit = dealer != null;

        // E05: one hit showed 25+ damage.
        if (isHit && BingoSquares.IsOpen("E05"))
            BingoSquares.Reach("E05", attacker, damageDealt, 25);

        // F-M07: one hit broke all of an enemy's Block, which was 25 or more. Block that held
        // isn't broken, so only a breaking hit counts toward the best.
        if (isHit && hit.WasBlockBroken && BingoSquares.IsOpen("F-M07"))
            BingoSquares.Reach("F-M07", attacker, hit.BlockedDamage, 25);

        // F-E06: 60+ damage dealt this turn, Poison ticks included.
        int damageThisTurn = FightLog.DamageThisTurn.Add(attacker, damageDealt);
        if (BingoSquares.IsOpen("F-E06"))
            BingoSquares.Reach("F-E06", attacker, damageThisTurn, 60);

        if (hit.UnblockedDamage > 0 && FightCredit.IsBoss(combatState, target))
            OnBossHpLost(target, attacker, hit.UnblockedDamage);
    }

    private static void OnBossHpLost(Creature boss, Player attacker, int hpLost)
    {
        if (!FightLog.BossHpLostThisTurn.TryGetValue(boss, out PlayerTally? tally))
        {
            tally = new PlayerTally();
            FightLog.BossHpLostThisTurn[boss] = tally;
        }
        int lostThisTurn = tally.Add(attacker, hpLost);

        // Judged as a whole percentage of the boss's Max HP, so the best carries over between
        // bosses of different sizes. Rounding down keeps "at least 25%" exact: the floor reaches
        // a whole-number target only when the true share does.
        int percentLost = boss.MaxHp > 0 ? lostThisTurn * 100 / boss.MaxHp : 100;

        // F-M11: a quarter of one boss's Max HP in one turn.
        if (BingoSquares.IsOpen("F-M11"))
            BingoSquares.Reach("F-M11", attacker, percentLost, 25);

        // F-H03: half of one boss's Max HP in one turn.
        if (BingoSquares.IsOpen("F-H03"))
            BingoSquares.Reach("F-H03", attacker, percentLost, 50);
    }
}
