using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace StS2AP.Bingo.Detectors.Characters;

/// <summary>
/// Shared plumbing for the character squares: the character gate, error containment and the
/// per-player tallies that start over each combat or turn.
/// </summary>
internal static class CharacterSquares
{
    private static readonly HashSet<string> ReportedFailures = new();

    /// <summary>True when the player is playing <typeparamref name="TCharacter"/>.</summary>
    public static bool Plays<TCharacter>(Player? player) where TCharacter : CharacterModel
        => player?.Character is TCharacter;

    /// <summary>
    /// Completes a square for the player who met it, provided that player is playing the
    /// square's character.
    /// </summary>
    public static void Complete<TCharacter>(string key, Player? player) where TCharacter : CharacterModel
    {
        if (Plays<TCharacter>(player))
            BingoSquares.Complete(key, player);
    }

    /// <summary>
    /// Reports a "reach a number" square's current value (see <see cref="BingoSquares.Reach"/>)
    /// for the player who reached it, provided that player is playing the square's character.
    /// </summary>
    public static void Reach<TCharacter>(string key, Player? player, int value, int target)
        where TCharacter : CharacterModel
    {
        if (Plays<TCharacter>(player))
            BingoSquares.Reach(key, player, value, target);
    }

    /// <summary>
    /// Completes a square for this client's player whoever caused it (Doom kills count for every
    /// player), still only when this client's player is playing the square's character.
    /// </summary>
    public static void CompleteForLocalPlayer<TCharacter>(string key, ICombatState combatState)
        where TCharacter : CharacterModel
    {
        if (Plays<TCharacter>(LocalContext.GetMe(combatState.Players)))
            BingoSquares.Complete(key, null);
    }

    /// <summary>
    /// Reports a "reach a number" square's current value for this client's player whoever caused
    /// it, with the same character rule as <see cref="CompleteForLocalPlayer{TCharacter}"/>.
    /// </summary>
    public static void ReachForLocalPlayer<TCharacter>(string key, ICombatState combatState, int value, int target)
        where TCharacter : CharacterModel
    {
        if (Plays<TCharacter>(LocalContext.GetMe(combatState.Players)))
            BingoSquares.Reach(key, null, value, target);
    }

    /// <summary>
    /// The player behind a creature: the player itself, or the owner of a pet such as Osty.
    /// </summary>
    public static Player? PlayerBehind(Creature? creature) => creature?.Player ?? creature?.PetOwner;

    /// <summary>
    /// The full size of one hit: what Block absorbed, the HP it took and the overkill beyond
    /// the target's remaining HP. The game's own Rain Shot measures a hit the same way.
    /// </summary>
    public static int HitDamage(DamageResult result)
        => result.TotalDamage + result.OverkillDamage;

    /// <summary>
    /// Runs a detector inside a game hook. A detector bug must never break the hook it watches,
    /// so failures are logged (once per detector) and swallowed.
    /// </summary>
    public static void Guard(string detector, Action detect)
    {
        try
        {
            detect();
        }
        catch (Exception ex)
        {
            if (ReportedFailures.Add(detector))
                LogUtility.Error($"Bingo detector {detector} failed: {ex}");
        }
    }
}

/// <summary>When a per-player tally starts over.</summary>
internal enum TallyScope
{
    /// <summary>Cleared when a combat starts.</summary>
    Combat,

    /// <summary>Cleared when any side's turn starts (and when a combat starts).</summary>
    Turn,
}

/// <summary>
/// A number per player that clears itself at its <see cref="TallyScope"/> boundary. Keyed by
/// NetId because every client simulates every player in multiplayer, and a reload builds new
/// <see cref="Player"/> objects.
/// </summary>
internal sealed class PlayerTally
{
    private readonly Dictionary<ulong, int> _totals = new();

    public PlayerTally(TallyScope scope) => TallyBoundaries.Register(scope, _totals.Clear);

    /// <summary>Adds to the player's total and returns the new total.</summary>
    public int Add(Player player, int amount = 1)
    {
        _totals.TryGetValue(player.NetId, out int total);
        total += amount;
        _totals[player.NetId] = total;
        return total;
    }

    public int Get(Player player) => _totals.GetValueOrDefault(player.NetId);
}

/// <summary>Distinct values per player that clear themselves at the scope's boundary.</summary>
internal sealed class PlayerSet<T>
{
    private readonly Dictionary<ulong, HashSet<T>> _sets = new();

    public PlayerSet(TallyScope scope) => TallyBoundaries.Register(scope, _sets.Clear);

    /// <summary>Adds the value to the player's set and returns how many distinct values it holds.</summary>
    public int Add(Player player, T value)
    {
        if (!_sets.TryGetValue(player.NetId, out HashSet<T>? set))
        {
            set = new HashSet<T>();
            _sets[player.NetId] = set;
        }
        set.Add(value);
        return set.Count;
    }
}

/// <summary>
/// Clears the tallies at combat and turn starts. The prefixes run before the hooks' listeners,
/// so effects that fire at the start of a combat or turn (relics, Innate powers) already count
/// toward the new combat or turn.
/// </summary>
internal static class TallyBoundaries
{
    private static readonly List<Action> CombatResets = new();
    private static readonly List<Action> TurnResets = new();

    public static void Register(TallyScope scope, Action reset)
        => (scope == TallyScope.Combat ? CombatResets : TurnResets).Add(reset);

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart))]
    private static class CombatStart
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            foreach (Action reset in CombatResets)
                reset();
            foreach (Action reset in TurnResets)
                reset();
        }
    }

    // Every side's turn is its own turn, so what happens during the enemies' turn (Poison
    // ticks, enemy moves) never adds to the players' turn before it.
    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeSideTurnStart))]
    private static class SideTurnStart
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            foreach (Action reset in TurnResets)
                reset();
        }
    }
}
