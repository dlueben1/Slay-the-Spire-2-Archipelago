using MegaCrit.Sts2.Core.Entities.Players;

namespace StS2AP.Bingo.Detectors.Combat;

/// <summary>Shared plumbing for the combat square detectors.</summary>
internal static class CombatDetectorSupport
{
    /// <summary>
    /// Runs a detector body so that a bug in it can never break the game hook it sits on. The
    /// detectors are prefixes on game methods, and an exception thrown from a prefix would
    /// escape into the game's own combat flow.
    /// </summary>
    public static void Guard(string detector, Action body)
    {
        try
        {
            body();
        }
        catch (Exception ex)
        {
            LogUtility.Error($"Bingo detector {detector} failed: {ex}");
        }
    }

    /// <summary>Completes the square when it is still open and the condition holds.</summary>
    public static void CompleteIf(string key, Player? player, Func<bool> condition)
    {
        if (BingoSquares.IsOpen(key) && condition())
            BingoSquares.Complete(key, player);
    }

    /// <summary>
    /// Reports a "reach a number" square's current value through <see cref="BingoSquares.Reach"/>,
    /// working the value out only while the square is still open.
    /// </summary>
    public static void ReachIfOpen(string key, Player? player, Func<int> value, int target)
    {
        if (BingoSquares.IsOpen(key))
            BingoSquares.Reach(key, player, value(), target);
    }
}
