using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using StS2AP.Utils;

namespace StS2AP.Bingo.Detectors.Fight;

/// <summary>
/// Works out which player gets the credit for something that happened in combat. Every client
/// simulates every player, so detectors pass that player to <see cref="BingoSquares.Complete"/>
/// or <see cref="BingoSquares.Reach"/> and only the local player's squares count.
/// </summary>
internal static class FightCredit
{
    /// <summary>The player a creature acts for: the player itself, or the owner of a pet (Osty).</summary>
    public static Player? PlayerOf(Creature? creature) => creature?.Player ?? creature?.PetOwner;

    /// <summary>
    /// The player who dealt damage to an enemy, or null when no player did (an enemy hitting
    /// its ally, Demise on an unpoisoned enemy).
    /// </summary>
    public static Player? ForDamage(ICombatState combatState, Creature? dealer, Creature target)
    {
        if (dealer != null)
            return PlayerOf(dealer);

        // Poison ticks have no dealer. They run at the start of the poisoned creature's side
        // turn, so dealer-less damage to an enemy during the enemies' turn is its Poison, and
        // the credit goes to whoever applied that Poison.
        if (combatState.CurrentSide == CombatSide.Enemy)
            return PlayerOf(target.GetPower<PoisonPower>()?.Applier);

        // Other dealer-less damage in the players' turn (Haunt and the like) is an effect of the
        // action being resolved, such as the card play that triggered it.
        return ActingPlayer(combatState);
    }

    /// <summary>The player whose action (card play, potion, end turn) the game is resolving.</summary>
    public static Player? ActingPlayer(ICombatState combatState)
    {
        GameAction? action = RunManager.Instance?.ActionExecutor?.CurrentlyRunningAction;
        if (action == null)
            return null;
        ulong ownerId = action.OwnerId;
        return combatState.Players.FirstOrDefault(player => player.NetId == ownerId);
    }

    /// <summary>This client's player, for effects the rulings credit to everyone (Doom kills).</summary>
    public static Player? Me(ICombatState combatState) => LocalContext.GetMe(combatState);

    /// <summary>
    /// Completes a square for the credited player. When no player could be traced, single-player
    /// credits this client's player (nobody else could have done it); multiplayer gives no credit
    /// rather than handing the square to every client.
    /// </summary>
    public static void CompleteOrSoloFallback(string key, Player? credit, ICombatState combatState)
    {
        if (credit != null)
            BingoSquares.Complete(key, credit);
        else if (combatState.Players.Count <= 1)
            BingoSquares.Complete(key, null);
    }

    /// <summary>
    /// A boss is a primary enemy in a boss encounter, so minions such as the Kin followers
    /// don't count. Two-part bosses (Kaiser Crab) count each part as a boss.
    /// </summary>
    public static bool IsBoss(ICombatState combatState, Creature creature) =>
        combatState.Encounter?.RoomType == RoomType.Boss && creature.IsPrimaryEnemy;

    /// <summary>
    /// Runs a detector body. Detectors run inside the game's own hooks, so an exception must
    /// never escape into the combat flow.
    /// </summary>
    public static void Guard(string detector, Action body)
    {
        try
        {
            body();
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Bingo fight detector '{detector}' failed: {ex}");
        }
    }
}
