using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;

namespace StS2AP.Bingo;

/// <summary>
/// The one entry point square detectors use. A detector watches the game for its square's
/// condition and calls <see cref="Complete"/> when it is met; this class decides whether that
/// means anything for the connected slot (bingo goal, square on the board, not yet checked,
/// local player) and sends the square's location check.
/// </summary>
/// <remarks>
/// Squares are judged per player. In multiplayer every client simulates every player, so a
/// detector passes the player who did the thing and only the local player's squares count.
/// Pass <c>null</c> when a square credits whoever is playing on this client regardless of who
/// caused it (Doom kills count for every player).
/// </remarks>
public static class BingoSquares
{
    /// <summary>True when the connected slot's goal is bingo and its board is loaded.</summary>
    public static bool IsActive => BingoState.IsActive;

    /// <summary>
    /// True when the square is on this slot's board and not checked yet. Detectors call this
    /// before doing any per-event work for a square, so squares that aren't dealt cost nothing.
    /// </summary>
    public static bool IsOpen(string key) => BingoState.IsOpen(key);

    /// <summary>
    /// Reports that a square's condition was met. Safe to call repeatedly and from any hook:
    /// it ignores non-bingo slots, squares not on the board, squares already checked and players
    /// other than the local one.
    /// </summary>
    public static void Complete(string key, Player? player)
    {
        if (player != null && !LocalContext.IsMe(player))
            return;
        BingoState.Complete(key);
    }
}
