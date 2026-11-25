using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using StS2AP.Bingo;

namespace StS2AP.Utils.Diagnostics;

/// <summary>
/// Dev-console view of the slot's bingo board, and a way to complete a square without meeting its
/// condition, for testing the board, the goal and the UI. Completing goes through the same entry
/// point as the detectors, so it sends a real check; it refuses in race rooms.
/// </summary>
public sealed class ApBingoCmd : AbstractConsoleCmd
{
    private const string CompleteArgumentName = "complete";

    public override string CmdName => "ap_bingo";

    public override string Args => "[complete <key>]";

    public override string Description => "Lists the AP bingo board, or completes one of its squares (sends its check)";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        BingoBoard? board = BingoState.Board;
        if (board == null)
            return new CmdResult(false, "The connected AP slot's goal is not bingo.");

        if (args.Length == 0)
            return new CmdResult(true, DescribeBoard(board));

        if (args.Length != 2 || !args[0].Equals(CompleteArgumentName, StringComparison.OrdinalIgnoreCase))
            return new CmdResult(false, $"Usage: {CmdName} | {CmdName} {CompleteArgumentName} <key>");

        if (ArchipelagoClient.IsRaceMode != false)
        {
            return new CmdResult(false, ArchipelagoClient.IsRaceMode == true
                ? "Completing squares is disabled in race rooms."
                : "The room's race mode is unknown, so completing squares stays disabled.");
        }

        BingoSquareInfo? square = board.Squares.FirstOrDefault(candidate =>
            candidate.Key.Equals(args[1], StringComparison.OrdinalIgnoreCase));
        if (square == null)
            return new CmdResult(false, $"Square '{args[1]}' is not on this board.");
        if (!BingoSquares.IsOpen(square.Key))
            return new CmdResult(false, $"Square {square.Key} is not open (already done, or this client cannot send checks).");

        BingoSquares.Complete(square.Key, null);
        return BingoState.IsDone(square)
            ? new CmdResult(true, $"Completed {square.Key}: {square.Text}\n{DescribeProgress(board)}")
            : new CmdResult(false, $"Square {square.Key} was not recorded; see the log.");
    }

    public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
    {
        if (args.Length <= 1)
            return CompleteArgument([CompleteArgumentName], [], args.FirstOrDefault() ?? "", CompletionType.Subcommand);
        if (args.Length == 2 && BingoState.Board is { } board)
        {
            IEnumerable<string> openKeys = board.Squares.Where(square => !BingoState.IsDone(square)).Select(square => square.Key);
            return CompleteArgument(openKeys, [args[0]], args[1]);
        }
        return base.GetArgumentCompletions(player, args);
    }

    private static string DescribeBoard(BingoBoard board)
    {
        var lines = new List<string> { DescribeProgress(board) };
        for (int row = 0; row < board.Size; row++)
        {
            for (int column = 0; column < board.Size; column++)
            {
                BingoSquareInfo square = board.SquareAt(row, column);
                string state = BingoState.IsDone(square) ? "done" : BingoState.IsOpen(square.Key) ? "open" : "blocked";
                lines.Add($"R{row + 1}C{column + 1} {square.Key} ({square.Tier}, {state}, location {square.LocationId}): {square.Text}");
            }
        }
        string result = string.Join("\n", lines);
        LogUtility.Info(result);
        return result;
    }

    private static string DescribeProgress(BingoBoard board) =>
        $"Bingo {board.Size}x{board.Size}: {BingoState.CompletedLines} of {board.Lines} line(s), "
            + $"{BingoState.CompletedSquares} of {board.Squares.Count} squares done.";
}
