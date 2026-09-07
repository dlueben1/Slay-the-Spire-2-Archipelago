using System.Diagnostics.CodeAnalysis;
using StS2AP.Domain;

namespace StS2AP.Utils;

/// <summary>
/// The room/team/slot/player identity frozen into a multiplayer run. Server address is deliberately
/// excluded: external effects and reconnects use the stronger ApSessionIdentity instead.
/// </summary>
internal sealed record ApSlotIdentity
{
    private ParticipantSlot Value { get; }
    public string RoomSeed => Value.RoomSeed;
    public int ApTeamId => Value.ApTeamId;
    public int ApSlotId => Value.ApSlotId;
    public int PlayerNumber => Value.PlayerNumber;

    private ApSlotIdentity(ParticipantSlot value)
    {
        Value = value;
    }

    public static ApSlotIdentity Create(string roomSeed, int apTeamId, int apSlotId, int playerNumber = 1)
    {
        var decoded = ParticipantSlot.Decode(roomSeed, apTeamId, apSlotId, playerNumber);
        return decoded.IsOk
            ? new ApSlotIdentity(decoded.ResultValue)
            : throw new ArgumentException("The AP room/team/slot/player identity is incomplete or invalid.");
    }

    /// <summary>Decodes the nullable identity fields at the saved-run boundary.</summary>
    public static bool TryCreate(string? roomSeed, int? apTeamId, int? apSlotId,
        [NotNullWhen(true)] out ApSlotIdentity? identity)
    {
        var decoded = ParticipantSlot.Decode(roomSeed!, apTeamId, apSlotId);
        identity = decoded.IsOk ? new ApSlotIdentity(decoded.ResultValue) : null;
        return decoded.IsOk;
    }

    public override string ToString() => Value.ToString();
}
