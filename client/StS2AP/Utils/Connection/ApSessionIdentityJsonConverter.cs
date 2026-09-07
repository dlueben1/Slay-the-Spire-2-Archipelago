using System.Text.Json;
using System.Text.Json.Serialization;

namespace StS2AP.Utils;

/// <summary>Preserves the existing flat outbox JSON while validating every restored identity.</summary>
internal sealed class ApSessionIdentityJsonConverter : JsonConverter<ApSessionIdentity>
{
    // A present-but-null outbox identity is invalid, just like missing required fields.
    public override bool HandleNull => true;

    public override ApSessionIdentity Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        var data = JsonSerializer.Deserialize<IdentityData>(ref reader, options)
            ?? throw new JsonException("The AP session identity was null.");
        try
        {
            return ApSessionIdentity.Create(
                data.ServerAuthority, data.RoomSeed, data.ApTeamId, data.ApSlotId, data.PlayerNumber);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("The AP session identity was invalid.", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, ApSessionIdentity value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteString("server_authority", value.ServerAuthority);
        writer.WriteString("room_seed", value.RoomSeed);
        writer.WriteNumber("ap_team_id", value.ApTeamId);
        writer.WriteNumber("ap_slot_id", value.ApSlotId);
        if (value.PlayerNumber != 1)
            writer.WriteNumber("player_number", value.PlayerNumber);
        writer.WriteEndObject();
    }

    private sealed class IdentityData
    {
        [JsonPropertyName("server_authority")]
        public required string ServerAuthority { get; init; }

        [JsonPropertyName("room_seed")]
        public required string RoomSeed { get; init; }

        [JsonPropertyName("ap_team_id")]
        public required int ApTeamId { get; init; }

        [JsonPropertyName("ap_slot_id")]
        public required int ApSlotId { get; init; }

        [JsonPropertyName("player_number")]
        public int PlayerNumber { get; init; } = 1;
    }
}
