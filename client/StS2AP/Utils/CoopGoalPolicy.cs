using StS2AP.Data;

namespace StS2AP.Utils;

internal static class CoopGoalPolicy
{
    public static bool IsComplete(IEnumerable<string> completed, IEnumerable<string> characters,
        int playerCount, int numCharsGoal)
    {
        var roster = characters.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        int required = numCharsGoal == 0 ? roster.Length : numCharsGoal;
        if (playerCount is < 1 or > 4 || required < 1 || required > roster.Length)
            return false;
        var records = completed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Enumerable.Range(1, playerCount).All(number =>
            roster.Count(character => records.Contains(ArchipelagoIdCodec.PlayerName(character, number))) >= required);
    }
}
