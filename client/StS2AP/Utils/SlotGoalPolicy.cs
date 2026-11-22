namespace StS2AP.Utils;

/// <summary>
/// Decides slot completion from the merged server goal records, which every client connected
/// to the slot updates, rather than from one client's local victories.
/// </summary>
internal static class SlotGoalPolicy
{
    public static bool IsComplete(IEnumerable<string> completed, IEnumerable<string> characters, int numCharsGoal)
    {
        var roster = characters.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        int required = numCharsGoal == 0 ? roster.Length : numCharsGoal;
        if (required < 1 || required > roster.Length)
            return false;
        var records = completed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return roster.Count(records.Contains) >= required;
    }
}
