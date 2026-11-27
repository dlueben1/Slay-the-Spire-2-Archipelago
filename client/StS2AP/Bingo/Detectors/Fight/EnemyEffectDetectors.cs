using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;

namespace StS2AP.Bingo.Detectors.Fight;

/// <summary>Squares about what players do to enemies other than damage: stuns and Artifact.</summary>
internal static class EnemyEffectDetectors
{
    public const string StunKey = "F-M03";
    private const string StripArtifactKey = "F-M08";

    /// <summary>Who applied each debuff that is on its way to an enemy holding Artifact.</summary>
    private static readonly Dictionary<PowerModel, Creature?> PendingDebuffAppliers = new(ReferenceEqualityComparer.Instance);

    public static void Reset() => PendingDebuffAppliers.Clear();

    /// <summary>
    /// F-M03: every enemy stun goes through this overload of <c>CreatureCmd.Stun</c>: Whistle,
    /// and the powers that break a mechanic (waking Lagavulin, breaking Burrowed, Slumber,
    /// Shriek, Plow, Flutter, an attack fully blocked against Imbalanced, Ravenous). None of
    /// them says who caused it, so the credit goes to the player who last hit, targeted or
    /// blocked that enemy, else to the player whose action is resolving. Ravenous stuns a
    /// Corpse Slug when another enemy dies, from that death's hook: its credit goes to the
    /// player who made that kill.
    /// </summary>
    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Stun),
        new[] { typeof(Creature), typeof(Func<IReadOnlyList<Creature>, Task>), typeof(string) })]
    private static class EnemyStunned
    {
        [HarmonyPrefix]
        private static void Prefix(Creature creature)
        {
            if (creature.Side != CombatSide.Enemy || creature.CombatState == null || !BingoSquares.IsOpen(StunKey))
                return;
            FightCredit.Guard("stun", () =>
            {
                // RavenousPower sets IsRavenous just before it stuns the slug.
                var credit = creature.Monster is CorpseSlug { IsRavenous: true }
                    ? KillLog.LatestDeathKiller
                    : FightLog.LastPlayerToEngage.GetValueOrDefault(creature)
                      ?? FightCredit.ActingPlayer(creature.CombatState);
                FightCredit.CompleteOrSoloFallback(StunKey, credit, creature.CombatState);
            });
        }
    }

    /// <summary>
    /// Remembers the applier of a debuff headed for an enemy with Artifact. This hook is the
    /// last one that names the applier; Artifact blocks the debuff a few steps later.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforePowerAmountChanged))]
    private static class PowerChanging
    {
        [HarmonyPrefix]
        private static void Prefix(PowerModel power, Creature target, Creature? applier)
        {
            if (target.Side != CombatSide.Enemy || !BingoSquares.IsOpen(StripArtifactKey))
                return;
            FightCredit.Guard("power changing", () =>
            {
                if (target.HasPower<ArtifactPower>())
                    PendingDebuffAppliers[power] = applier;
            });
        }
    }

    /// <summary>
    /// F-M08: Artifact negates a debuff by reporting itself as a modifier of the received
    /// amount, then spends a charge in its <c>AfterModifyingPowerAmountReceived</c>. Seen from
    /// a prefix, an enemy's Artifact on its last charge is about to be stripped.
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterModifyingPowerAmountReceived))]
    private static class PowerAmountModified
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState combatState, IEnumerable<AbstractModel> modifiers, PowerModel modifiedPower)
        {
            if (!PendingDebuffAppliers.Remove(modifiedPower, out Creature? applier) || !BingoSquares.IsOpen(StripArtifactKey))
                return;
            FightCredit.Guard("artifact", () =>
            {
                bool strippedLastCharge = modifiers.OfType<ArtifactPower>()
                    .Any(artifact => artifact.Owner.Side == CombatSide.Enemy && artifact.Amount == 1);
                if (!strippedLastCharge)
                    return;
                // Cards, relics and potions name the player's creature as applier; fall back to
                // the acting player when none is named. An enemy applier strips nothing for you.
                if (applier is { Side: CombatSide.Enemy })
                    return;
                var credit = FightCredit.PlayerOf(applier) ?? FightCredit.ActingPlayer(combatState);
                FightCredit.CompleteOrSoloFallback(StripArtifactKey, credit, combatState);
            });
        }
    }
}
