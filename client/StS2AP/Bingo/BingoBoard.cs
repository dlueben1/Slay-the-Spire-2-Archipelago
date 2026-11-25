using Newtonsoft.Json.Linq;
using StS2AP.Data;
using StS2AP.Utils;

namespace StS2AP.Bingo;

/// <summary>What the slot has to do to send its goal.</summary>
public enum SlotGoal
{
    /// <summary>Win runs with enough characters (<see cref="ArchipelagoSettings.NumCharsGoal"/>).</summary>
    Characters = 0,

    /// <summary>Complete enough rows, columns or diagonals of the bingo board.</summary>
    Bingo = 1,
}

/// <summary>One square of the slot's bingo board, as the APWorld dealt it.</summary>
public sealed class BingoSquareInfo
{
    /// <summary>The catalog key detectors use, for example <c>E04</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public long LocationId { get; set; }

    /// <summary>"E", "M" or "H": roughly the act by which the square is doable.</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>Player-facing wording, prefixed with the character for character squares.</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// The slot's bingo board from slot data: a square grid stored row-major, and how many full lines
/// (rows, columns and the two diagonals) complete the goal. Which squares are done is not stored
/// here; it is the slot's checked locations on the server.
/// </summary>
/// <remarks>
/// Part of <see cref="ArchipelagoSettings"/>, so the multiplayer host's frozen settings snapshot
/// carries it into the run save: keep it plain settable properties.
/// </remarks>
public sealed class BingoBoard
{
    public int Size { get; set; }

    /// <summary>Full lines needed for the goal, between 1 and <see cref="LineCount"/>.</summary>
    public int Lines { get; set; }

    public List<BingoSquareInfo> Squares { get; set; } = new();

    /// <summary>Every row, every column and both diagonals.</summary>
    public int LineCount => (2 * Size) + 2;

    public BingoSquareInfo? FindSquare(string key) =>
        Squares.FirstOrDefault(square => string.Equals(square.Key, key, StringComparison.Ordinal));

    public BingoSquareInfo SquareAt(int row, int column) => Squares[(row * Size) + column];

    /// <summary>Counts the full lines on the board, judging each square with <paramref name="isDone"/>.</summary>
    public int CountCompletedLines(Func<BingoSquareInfo, bool> isDone)
    {
        if (Size < 1 || Squares.Count != Size * Size)
            return 0;

        bool[] done = Squares.Select(isDone).ToArray();
        bool Done(int row, int column) => done[(row * Size) + column];

        int lines = 0;
        for (int i = 0; i < Size; i++)
        {
            int index = i;
            if (Enumerable.Range(0, Size).All(column => Done(index, column)))
                lines++;
            if (Enumerable.Range(0, Size).All(row => Done(row, index)))
                lines++;
        }
        if (Enumerable.Range(0, Size).All(i => Done(i, i)))
            lines++;
        if (Enumerable.Range(0, Size).All(i => Done(i, Size - 1 - i)))
            lines++;
        return lines;
    }

    /// <summary>
    /// Reads the <c>bingo</c> slot-data object. Throws <see cref="InvalidDataException"/> when it
    /// is malformed: a bingo slot that played the characters goal instead could never finish.
    /// </summary>
    public static BingoBoard Parse(object? slotDataValue)
    {
        if (slotDataValue is not JObject boardObject)
            throw new InvalidDataException("The AP slot's goal is bingo, but its slot data has no bingo board.");

        int size = boardObject.Value<int?>("size")
            ?? throw new InvalidDataException("The bingo board has no size.");
        int lines = boardObject.Value<int?>("lines")
            ?? throw new InvalidDataException("The bingo board has no line count.");
        if (size < 1)
            throw new InvalidDataException($"The bingo board has an invalid size {size}.");
        if (boardObject["squares"] is not JArray squareArray || squareArray.Count != size * size)
        {
            throw new InvalidDataException(
                $"The bingo board needs {size * size} squares for size {size}.");
        }

        var board = new BingoBoard { Size = size };
        foreach (JToken token in squareArray)
        {
            if (token is not JObject squareObject)
                throw new InvalidDataException($"A bingo square is not an object: {token}");

            var square = new BingoSquareInfo
            {
                Key = squareObject.Value<string>("key") ?? string.Empty,
                LocationId = squareObject.Value<long?>("location") ?? -1,
                Tier = squareObject.Value<string>("tier") ?? string.Empty,
                Text = squareObject.Value<string>("text") ?? string.Empty,
            };
            if (string.IsNullOrWhiteSpace(square.Key) || !ArchipelagoIdCodec.IsBingoLocationId(square.LocationId))
                throw new InvalidDataException($"A bingo square has no key or an invalid location: {token}");
            if (board.FindSquare(square.Key) != null)
                throw new InvalidDataException($"The bingo board deals square {square.Key} twice.");
            board.Squares.Add(square);
        }

        int clampedLines = Math.Clamp(lines, 1, board.LineCount);
        if (clampedLines != lines)
            LogUtility.Warn($"SLOT - Bingo asks for {lines} lines; a {size}x{size} board has {board.LineCount}. Using {clampedLines}.");
        board.Lines = clampedLines;
        return board;
    }
}
