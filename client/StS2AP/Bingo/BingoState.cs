using Archipelago.MultiClient.Net;
using StS2AP.UI;
using StS2AP.Utils;

namespace StS2AP.Bingo;

/// <summary>
/// The connected slot's bingo board and its progress. The board comes from the slot settings;
/// a square is done once its location is checked, so progress is the slot's checked locations
/// (including optimistic local checks, other clients on the same slot and <c>!collect</c>).
/// When enough lines are full this client sends the slot's goal.
/// </summary>
internal static class BingoState
{
    // The session the goal was last sent through. A new session (reconnect, another slot)
    // sends it again; the server ignores a repeat.
    private static ArchipelagoSession? _goalSentSession;
    private static bool _goalAnnounced;
    // Lines already full when last evaluated; null until the first evaluation, which sets it
    // silently so logging in to a board with full lines doesn't announce them again.
    private static int? _knownLines;

    /// <summary>The slot's board, or null unless its goal is bingo.</summary>
    public static BingoBoard? Board =>
        ArchipelagoClient.Settings is { Goal: SlotGoal.Bingo, Bingo: { } board } ? board : null;

    public static bool IsActive => Board != null;

    /// <summary>Whether this client may send checks for the slot: multiplayer guests on another slot may not.</summary>
    private static bool CanSendChecks =>
        ArchipelagoClient.HasAuthenticatedSlot
        && (!MultiplayerSupport.IsMultiplayerScope || MultiplayerSupport.IsLocalOwnApSlot);

    /// <summary>A square counts as done once its check is sent or queued.</summary>
    public static bool IsDone(BingoSquareInfo square) =>
        ArchipelagoClient.CheckedLocations.Contains(square.LocationId)
        || ArchipelagoClient.Progress.PendingLocationChecks.Contains(square.LocationId);

    public static bool IsOpen(string key)
    {
        BingoSquareInfo? square = Board?.FindSquare(key);
        return square != null
            && CanSendChecks
            && ArchipelagoClient.SlotLocationIds.Contains(square.LocationId)
            && !IsDone(square);
    }

    public static int CompletedLines => Board?.CountCompletedLines(IsDone) ?? 0;

    public static int CompletedSquares => Board?.Squares.Count(IsDone) ?? 0;

    /// <summary>Sends a square's check when it is open, announces it and re-evaluates the goal.</summary>
    public static void Complete(string key)
    {
        if (!IsOpen(key) || Board?.FindSquare(key) is not { } square)
            return;

        // The square's own location only: mirroring to missing characters is for character blocks.
        LocationCheckSendResult result = GameUtility.QueueExactChecks([square.LocationId]);
        if (result.AcceptedCount == 0)
        {
            LogUtility.Warn($"[Bingo] Square {square.Key} was met but its check was not recorded ({result.Dispatch})");
            return;
        }

        LogUtility.Success($"[Bingo] Square {square.Key} done: {square.Text}");
        NotificationUtility.ShowRawText($"Bingo square done: {square.Text}", timeout: 4.0);
        Evaluate();
    }

    /// <summary>
    /// Refreshes the board UI and sends the goal once enough lines are full. Called after this
    /// client completes a square, on login and whenever the server reports checked locations.
    /// </summary>
    public static void Evaluate()
    {
        BingoBoard? board = Board;
        if (board == null)
            return;

        int lines = board.CountCompletedLines(IsDone);
        if (lines > _knownLines && lines < board.Lines)
            NotificationUtility.ShowRawText($"Bingo line complete: {lines} of {board.Lines}.", timeout: 4.0);
        _knownLines = Math.Max(_knownLines ?? 0, lines);

        ArchipelagoGoalTrackerUI.UpdateGoalProgress();
        BingoBoardUI.Refresh();

        if (lines < board.Lines)
            return;

        ArchipelagoSession? session = ArchipelagoClient.Session;
        if (session == null || !ArchipelagoClient.IsConnected || ReferenceEquals(_goalSentSession, session))
            return;

        try
        {
            session.SetGoalAchieved();
            _goalSentSession = session;
            LogUtility.Success($"[Bingo] {lines} of {board.Lines} line(s) complete. SetGoalAchieved sent.");
        }
        catch (Exception ex)
        {
            LogUtility.Error($"[Bingo] Could not send the goal: {ex.Message}");
            return;
        }

        if (!_goalAnnounced)
        {
            _goalAnnounced = true;
            NotificationUtility.ShowRawText("Bingo! Goal Complete!", timeout: 6.0);
        }
    }

    internal static void ResetSlotState()
    {
        _goalSentSession = null;
        _goalAnnounced = false;
        _knownLines = null;
        BingoBoardUI.RemoveUI();
    }
}
