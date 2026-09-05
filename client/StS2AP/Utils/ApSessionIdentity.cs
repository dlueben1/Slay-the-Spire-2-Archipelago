using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace StS2AP.Utils;

/// <summary>
/// Identifies the authenticated Archipelago destination that may own durable external effects.
/// The server authority is included so separately hosted rooms with the same generated seed do
/// not share an outbox.
/// </summary>
internal sealed record ApSessionIdentity
{
    [JsonPropertyName("server_authority")]
    public required string ServerAuthority { get; init; }

    [JsonPropertyName("room_seed")]
    public required string RoomSeed { get; init; }

    [JsonPropertyName("ap_team_id")]
    public required int ApTeamId { get; init; }

    [JsonPropertyName("ap_slot_id")]
    public required int ApSlotId { get; init; }

    public static ApSessionIdentity Create(
        string serverAddress,
        string roomSeed,
        int apTeamId,
        int apSlotId
    )
    {
        if (string.IsNullOrWhiteSpace(serverAddress))
        {
            throw new ArgumentException(
                "The AP server address is unavailable.",
                nameof(serverAddress)
            );
        }
        if (string.IsNullOrWhiteSpace(roomSeed))
            throw new ArgumentException("The AP room seed is unavailable.", nameof(roomSeed));
        if (apTeamId < 0)
            throw new ArgumentOutOfRangeException(nameof(apTeamId));
        if (apSlotId < 0)
            throw new ArgumentOutOfRangeException(nameof(apSlotId));

        return new ApSessionIdentity
        {
            ServerAuthority = NormalizeServerAuthority(serverAddress),
            RoomSeed = roomSeed,
            ApTeamId = apTeamId,
            ApSlotId = apSlotId,
        };
    }

    /// <summary>
    /// Hashes a length-prefixed canonical representation. The digest avoids lossy filename
    /// sanitization; the full identity is still verified inside the outbox before use.
    /// </summary>
    public string GetFileKey()
    {
        string canonical = FormattableString.Invariant(
            $"{ServerAuthority.Length}:{ServerAuthority}|{RoomSeed.Length}:{RoomSeed}|{ApTeamId}|{ApSlotId}"
        );
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public override string ToString() =>
        $"{RoomSeed}/ap-team-{ApTeamId}/ap-slot-{ApSlotId}@{ServerAuthority}";

    private static string NormalizeServerAuthority(string serverAddress) =>
        serverAddress.Trim().TrimEnd('/').ToLowerInvariant();
}
