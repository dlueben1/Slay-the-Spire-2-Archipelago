namespace StS2AP.Utils;

/// <summary>Number bounds and consent for sharing a numbered AP progression track.</summary>
public sealed class CoopPlayerSelection
{
    public sealed record Member(
        ulong NetId, string? RoomSeed, int? Team, int? Slot, int PlayerCount, int PlayerNumber);

    private Member[]? _approvedRoster;

    public static bool IsValid(int playerCount, int playerNumber) =>
        playerCount is >= 1 and <= 4 && playerNumber >= 1 && playerNumber <= playerCount;

    public static Member[] GetDuplicates(IEnumerable<Member> roster) => roster
        .Where(member => member.RoomSeed != null && member.Team.HasValue && member.Slot.HasValue)
        .GroupBy(member => (member.RoomSeed, member.Team, member.Slot, member.PlayerNumber))
        .Where(group => group.Count() > 1)
        .Select(group => group.First())
        .ToArray();

    public bool RequiresConfirmation(Member[] roster) =>
        GetDuplicates(roster).Length > 0
        && (_approvedRoster == null || !SameRoster(_approvedRoster, roster));

    public bool Confirm(Member[] displayedRoster, Member[] currentRoster)
    {
        if (!SameRoster(displayedRoster, currentRoster))
            return false;
        _approvedRoster = currentRoster.ToArray();
        return true;
    }

    private static bool SameRoster(Member[] left, Member[] right) =>
        left.OrderBy(member => member.NetId).SequenceEqual(right.OrderBy(member => member.NetId));
}
