using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using static StS2AP.Bingo.Detectors.Combat.CombatDetectorSupport;

namespace StS2AP.Bingo.Detectors.Combat;

/// <summary>
/// Squares about buffs and debuffs held at once. Both are checked whenever a power's amount
/// changes (<see cref="Hook.AfterPowerAmountChanged"/>, which also fires when a power is first
/// applied), since that is the only way a creature's set of powers grows.
/// </summary>
internal static class PowerDetectors
{
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPowerAmountChanged))]
    private static class WatchPowers
    {
        [HarmonyPrefix]
        private static void Prefix(ICombatState combatState, PowerModel power, Creature? applier) =>
            Guard("power changed", () => OnPowerChanged(combatState, power, applier));
    }

    private static void OnPowerChanged(ICombatState combatState, PowerModel power, Creature? applier)
    {
        Creature owner = power.Owner;

        // M08: different buffs on the player.
        if (owner.Player != null)
            CompleteIf("M08", owner.Player, () => DistinctPowers(owner, PowerType.Buff) >= 5);

        // E09: different debuffs on one enemy, credited to the player who applied the latest.
        // A debuff with no player behind it only counts in single-player, where it can only be
        // the one player's doing (a relic or potion effect with no applier).
        if (owner.Side == CombatSide.Enemy && BingoSquares.IsOpen("E09"))
        {
            Player? credited = applier?.Player ?? applier?.PetOwner
                ?? (combatState.Players.Count == 1 ? combatState.Players[0] : null);
            if (credited != null)
                CompleteIf("E09", credited, () => DistinctPowers(owner, PowerType.Debuff) >= 3);
        }
    }

    /// <summary>
    /// Counts the creature's different visible powers of one type. A power's type can depend on
    /// its amount (negative Strength is a debuff), and one at zero is about to be removed.
    /// Hidden bookkeeping powers are not shown to the player and don't count.
    /// </summary>
    private static int DistinctPowers(Creature creature, PowerType type) =>
        creature.Powers
            .Where(p => p.IsVisible && !p.ShouldRemoveDueToAmount() && p.TypeForCurrentAmount == type)
            .Select(p => p.Id)
            .Distinct()
            .Count();
}
