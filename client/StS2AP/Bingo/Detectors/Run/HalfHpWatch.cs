using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Runs;

namespace StS2AP.Bingo.Detectors.Run;

/// <summary>
/// For R-E09 ("Reach a boss without dropping below half HP this act"): remembers, per act and
/// player, whether the player's HP went below half at any moment.
/// </summary>
/// <remarks>
/// The run history only records HP as each map point is left, which misses a drop that is
/// healed back within the same fight. The live record covers that, and the history covers
/// what the live record cannot see: drops before the board loaded, or before a save-and-quit.
/// A run loaded from a save, including a checkpoint reload, is a new <see cref="RunState"/>
/// object, so the live record starts over and only the (rewound) history speaks for the past.
/// Osty is a creature of its own, so hits it takes do not count as the player's HP.
/// </remarks>
internal static class HalfHpWatch
{
    private const string SquareKey = "R-E09";

    private static IRunState? _trackedRun;
    private static readonly HashSet<(int ActIndex, ulong PlayerId)> DroppedBelowHalf = [];

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCurrentHpChanged))]
    private static class HpChanged
    {
        [HarmonyPrefix]
        private static void Prefix(IRunState runState, Creature creature) =>
            RunSquare.Detect("half HP watch", (runState, creature), static args => Observe(args.runState, args.creature));
    }

    private static void Observe(IRunState runState, Creature creature)
    {
        if (creature.Player is not Player player
            || runState is not RunState
            || !BingoSquares.IsOpen(SquareKey)
            || !IsBelowHalf(creature.CurrentHp, creature.MaxHp))
        {
            return;
        }

        if (!ReferenceEquals(runState, _trackedRun))
        {
            _trackedRun = runState;
            DroppedBelowHalf.Clear();
        }
        DroppedBelowHalf.Add((runState.CurrentActIndex, player.NetId));
    }

    /// <summary>Whether the player has been below half HP in the current act, as far as is known.</summary>
    public static bool DroppedThisAct(IRunState runState, Player player)
    {
        if (ReferenceEquals(runState, _trackedRun)
            && DroppedBelowHalf.Contains((runState.CurrentActIndex, player.NetId)))
        {
            return true;
        }

        if (IsBelowHalf(player.Creature.CurrentHp, player.Creature.MaxHp))
            return true;

        // Map points not left yet have no HP recorded (MaxHp 0).
        IEnumerable<PlayerMapPointHistoryEntry> act =
            RunHistory.StatsFor(RunHistory.CurrentAct(runState), player);
        return act.Any(stats => stats.MaxHp > 0 && IsBelowHalf(stats.CurrentHp, stats.MaxHp));
    }

    private static bool IsBelowHalf(int currentHp, int maxHp) => currentHp * 2 < maxHp;
}
