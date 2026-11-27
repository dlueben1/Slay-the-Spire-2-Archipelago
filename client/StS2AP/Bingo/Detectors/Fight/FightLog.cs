using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace StS2AP.Bingo.Detectors.Fight;

/// <summary>
/// Read-only bookkeeping of the current combat that several fight squares share: what each
/// player did this turn and this combat, which card or potion is resolving, and how pending
/// kills were caused. Nothing here touches game state.
/// </summary>
/// <remarks>
/// A "turn" for these squares runs from the start of the players' turn to the start of the
/// next one, so it includes the enemies' turn: Poison ticks there count toward per-turn
/// damage (user ruling), and Poison and Doom kills count toward per-turn kills.
/// </remarks>
internal static class FightLog
{
    /// <summary>Damage numbers (HP lost plus overkill) each player dealt to enemies this turn.</summary>
    public static readonly PlayerTally DamageThisTurn = new();

    /// <summary>HP each player took off each boss this turn.</summary>
    public static readonly Dictionary<Creature, PlayerTally> BossHpLostThisTurn =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Enemies each player killed this turn.</summary>
    public static readonly PlayerTally KillsThisTurn = new();

    /// <summary>Minions (secondary enemies) each player killed this combat.</summary>
    public static readonly PlayerTally MinionKillsThisCombat = new();

    /// <summary>Kills by each card while it is being played (all plays of one card count as one).</summary>
    public static readonly Dictionary<CardModel, int> KillsByPlayingCard = new(ReferenceEqualityComparer.Instance);

    /// <summary>Enemies with 40+ Max HP that were at full HP when each card began to be played.</summary>
    public static readonly Dictionary<CardModel, HashSet<Creature>> FullHpBigEnemiesAtPlay =
        new(ReferenceEqualityComparer.Instance);

    public const int BigEnemyMaxHp = 40;

    /// <summary>The potion each player is using right now, by player NetId.</summary>
    public static readonly Dictionary<ulong, PotionModel> PotionInUse = new();

    /// <summary>
    /// The player who last hit or was hit by each enemy: the credit for a stun, which the game
    /// triggers from a power or a monster move rather than from the player's action.
    /// </summary>
    public static readonly Dictionary<Creature, Player> LastPlayerToEngage = new(ReferenceEqualityComparer.Instance);

    public static void ResetTurn()
    {
        DamageThisTurn.Clear();
        BossHpLostThisTurn.Clear();
        KillsThisTurn.Clear();
    }

    public static void ResetCombat()
    {
        ResetTurn();
        MinionKillsThisCombat.Clear();
        KillsByPlayingCard.Clear();
        FullHpBigEnemiesAtPlay.Clear();
        PotionInUse.Clear();
        LastPlayerToEngage.Clear();
        KillLog.Reset();
        EnemyEffectDetectors.Reset();
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart))]
    private static class CombatStart
    {
        [HarmonyPrefix]
        private static void Prefix() => FightCredit.Guard("combat start", ResetCombat);
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
    private static class CombatEnd
    {
        // Releases the combat's creatures and cards; the next combat starts clean anyway.
        [HarmonyPrefix]
        private static void Prefix() => FightCredit.Guard("combat end", ResetCombat);
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeSideTurnStart))]
    private static class TurnStart
    {
        [HarmonyPrefix]
        private static void Prefix(CombatSide side)
        {
            if (side == CombatSide.Player)
                FightCredit.Guard("turn start", ResetTurn);
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardPlayed))]
    private static class CardPlayStart
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState combatState, CardPlay cardPlay)
        {
            if (!BingoSquares.IsActive)
                return;
            FightCredit.Guard("card play start", () => OnCardPlayStart(combatState, cardPlay));
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
    private static class CardPlayEnd
    {
        // The card's own damage and kills resolve before this hook; later effects that name the
        // card as their source (end-of-turn damage from a played Power) aren't "with one card".
        [HarmonyPrefix]
        private static void Prefix(CardPlay cardPlay)
        {
            if (!cardPlay.IsLastInSeries)
                return;
            KillsByPlayingCard.Remove(cardPlay.Card);
            FullHpBigEnemiesAtPlay.Remove(cardPlay.Card);
        }
    }

    private static void OnCardPlayStart(ICombatState combatState, CardPlay cardPlay)
    {
        if (cardPlay.Target is { Side: CombatSide.Enemy } target)
            LastPlayerToEngage[target] = cardPlay.Player;

        // A card played several times (replays, Burst-like effects) is still one card.
        if (!cardPlay.IsFirstInSeries)
            return;
        KillsByPlayingCard[cardPlay.Card] = 0;
        if (BingoSquares.IsOpen(KillDetectors.FullHpToDeadKey))
        {
            FullHpBigEnemiesAtPlay[cardPlay.Card] = combatState.Enemies
                .Where(enemy => enemy.IsAlive && enemy.MaxHp >= BigEnemyMaxHp && enemy.CurrentHp == enemy.MaxHp)
                .ToHashSet<Creature>(ReferenceEqualityComparer.Instance);
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforePotionUsed))]
    private static class PotionStart
    {
        [HarmonyPrefix]
        private static void Prefix(PotionModel potion)
        {
            if (BingoSquares.IsActive)
                FightCredit.Guard("potion start", () => PotionInUse[potion.Owner.NetId] = potion);
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPotionUsed))]
    private static class PotionEnd
    {
        [HarmonyPrefix]
        private static void Prefix(PotionModel potion)
        {
            FightCredit.Guard("potion end", () =>
            {
                ulong ownerId = potion.Owner.NetId;
                if (PotionInUse.TryGetValue(ownerId, out PotionModel? inUse) && ReferenceEquals(inUse, potion))
                    PotionInUse.Remove(ownerId);
            });
        }
    }

    /// <summary>
    /// Records who engaged each enemy, before the damage lands: stuns fire from the damage's
    /// follow-up hooks (Block broken, HP threshold, waking up), so the record must already be there.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeDamageReceived))]
    private static class Engagement
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState? combatState, Creature target, Creature? dealer)
        {
            if (combatState == null || !BingoSquares.IsOpen(EnemyEffectDetectors.StunKey))
                return;
            FightCredit.Guard("engagement", () =>
            {
                if (target.Side == CombatSide.Enemy)
                {
                    if (FightCredit.ForDamage(combatState, dealer, target) is { } attacker)
                        LastPlayerToEngage[target] = attacker;
                }
                // An enemy whose attack is fully blocked can stun itself (Imbalanced, Bowlbug
                // Rock): the credit goes to the player who blocked it.
                else if (dealer is { Side: CombatSide.Enemy } && FightCredit.PlayerOf(target) is { } defender)
                {
                    LastPlayerToEngage[dealer] = defender;
                }
            });
        }
    }
}

/// <summary>A per-player counter keyed by player NetId.</summary>
internal sealed class PlayerTally
{
    private readonly Dictionary<ulong, int> _counts = new();

    /// <summary>Adds to the player's count and returns the new total.</summary>
    public int Add(Player player, int amount)
    {
        int total = _counts.GetValueOrDefault(player.NetId) + amount;
        _counts[player.NetId] = total;
        return total;
    }

    public void Clear() => _counts.Clear();
}
