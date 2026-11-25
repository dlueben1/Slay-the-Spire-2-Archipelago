namespace StS2AP.Bingo;

/// <summary>
/// The slot's bingo board and its progress. Stub so detectors compile; the client core work
/// replaces it with the board from slot data, check sending and goal handling.
/// </summary>
internal static class BingoState
{
    public static bool IsActive => false;

    public static bool IsOpen(string key) => false;

    public static void Complete(string key)
    {
    }
}
