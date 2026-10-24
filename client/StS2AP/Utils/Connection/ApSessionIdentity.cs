using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace StS2AP.Utils;

/// <summary>
/// Identifies the authenticated Archipelago destination that may own durable external effects.
/// The server authority is included so separately hosted rooms with the same generated seed do
/// not share an outbox.
/// </summary>
[JsonConverter(typeof(ApSessionIdentityJsonConverter))]
internal sealed record ApSessionIdentity
{
    public string ServerAuthority { get; }
    public ApSlotIdentity Slot { get; }

    public string RoomSeed => Slot.RoomSeed;
    public int ApTeamId => Slot.ApTeamId;
    public int ApSlotId => Slot.ApSlotId;

    private ApSessionIdentity(string serverAuthority, ApSlotIdentity slot)
    {
        ServerAuthority = serverAuthority;
        Slot = slot;
    }

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
        string authority = NormalizeServerAuthority(serverAddress);
        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new ArgumentException(
                "The AP server address is unavailable.", nameof(serverAddress));
        }
        return new ApSessionIdentity(authority, ApSlotIdentity.Create(roomSeed, apTeamId, apSlotId));
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
        $"{Slot}@{ServerAuthority}";

    private static string NormalizeServerAuthority(string serverAddress) =>
        serverAddress.Trim().TrimEnd('/').ToLowerInvariant();
}
