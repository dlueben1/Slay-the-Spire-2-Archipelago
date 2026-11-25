using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using static StS2AP.Bingo.Detectors.Combat.CombatDetectorSupport;

namespace StS2AP.Bingo.Detectors.Combat;

/// <summary>Squares about Block and about weathering the enemies' turn.</summary>
internal static class DefenseDetectors
{
    /// <summary>
    /// E06: the game adds the Block to the creature before <see cref="Hook.AfterBlockGained"/>
    /// runs. Osty's summons are not Block and never reach this hook.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterBlockGained))]
    private static class WatchBlock
    {
        [HarmonyPrefix]
        private static void Prefix(Creature creature) =>
            Guard("block gained", () =>
            {
                if (creature.Player != null)
                    CompleteIf("E06", creature.Player, () => creature.Block >= 30);
            });
    }

    // F-E05 / F-M12: one enemy turn, summed over every hit aimed at the player. "Incoming" is
    // each hit's damage after Strength, Vulnerable and the like but before Block, the number
    // the intents show. The turn passes when the player lost no HP: hits soaked by Block or
    // taken by Osty count as no HP loss (user ruling).

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeSideTurnStart))]
    private static class StartEnemyTurn
    {
        [HarmonyPrefix]
        private static void Prefix(CombatSide side) =>
            Guard("enemy turn start", () =>
            {
                if (side == CombatSide.Enemy)
                    EnemyTurnTally.Reset();
            });
    }

    /// <summary>
    /// <see cref="Hook.BeforeDamageReceived"/> fires once per hit with the original target (the
    /// player even when the hit is then redirected to Osty) and the hit's modified amount.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeDamageReceived))]
    private static class CountIncomingDamage
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState? combatState, Creature target, decimal amount) =>
            Guard("incoming damage", () =>
            {
                if (combatState?.CurrentSide == CombatSide.Enemy && target.Player != null && EnemyTurnTally.IsWanted())
                    EnemyTurnTally.AddIncoming(target.Player, amount);
            });
    }

    /// <summary>
    /// Unblocked damage reaches the player's HP through <see cref="Hook.AfterCurrentHpChanged"/>
    /// with the creature that actually lost it, so damage Osty soaks never lands here.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCurrentHpChanged))]
    private static class CountHpLoss
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState? combatState, Creature creature, decimal delta) =>
            Guard("hp lost", () =>
            {
                if (delta < 0 && combatState?.CurrentSide == CombatSide.Enemy && creature.Player != null
                    && EnemyTurnTally.IsWanted())
                    EnemyTurnTally.MarkHpLost(creature.Player);
            });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterSideTurnEnd))]
    private static class EndEnemyTurn
    {
        [HarmonyPrefix]
        private static void Prefix(CombatSide side) =>
            Guard("enemy turn end", () =>
            {
                if (side == CombatSide.Enemy)
                    EnemyTurnTally.Judge();
            });
    }

    /// <summary>
    /// A combat won during the enemies' turn (Thorns, a counter-attack) skips the turn's end, so
    /// the hits weathered up to the win are judged here.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
    private static class EndEnemyTurnByVictory
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState? combatState) =>
            Guard("enemy turn victory", () =>
            {
                if (combatState?.CurrentSide == CombatSide.Enemy)
                    EnemyTurnTally.Judge();
                EnemyTurnTally.Reset();
            });
    }

    /// <summary>Per-player incoming damage and HP loss for the enemy turn in progress.</summary>
    private static class EnemyTurnTally
    {
        private sealed class Turn
        {
            public required Player Player;
            public decimal Incoming;
            public bool LostHp;
        }

        private static readonly Dictionary<ulong, Turn> Turns = new();

        public static bool IsWanted() => BingoSquares.IsOpen("F-E05") || BingoSquares.IsOpen("F-M12");

        public static void Reset() => Turns.Clear();

        public static void AddIncoming(Player player, decimal amount) => TurnOf(player).Incoming += amount;

        public static void MarkHpLost(Player player) => TurnOf(player).LostHp = true;

        public static void Judge()
        {
            foreach (Turn turn in Turns.Values)
            {
                if (turn.LostHp || turn.Player.Creature.IsDead)
                    continue;
                CompleteIf("F-E05", turn.Player, () => turn.Incoming >= 20);
                CompleteIf("F-M12", turn.Player, () => turn.Incoming >= 40);
            }
            Turns.Clear();
        }

        private static Turn TurnOf(Player player)
        {
            if (!Turns.TryGetValue(player.NetId, out Turn? turn))
            {
                turn = new Turn { Player = player };
                Turns[player.NetId] = turn;
            }
            return turn;
        }
    }
}
