namespace StS2AP.Utils;

/// <summary>Consent for several lobby members connecting to the same AP slot.</summary>
public sealed class CoopPlayerSelection
{
    public sealed record Member(ulong NetId, string? RoomSeed, int? Team, int? Slot);

    private Member[]? _approvedRoster;

    public static Member[] GetDuplicates(IEnumerable<Member> roster) => roster
        .Where(member => member.RoomSeed != null && member.Team.HasValue && member.Slot.HasValue)
        .GroupBy(member => (member.RoomSeed, member.Team, member.Slot))
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
