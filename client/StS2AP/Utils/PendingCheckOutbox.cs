using System.Text.Json.Serialization;

namespace StS2AP.Utils;

/// <summary>A versioned durable set of checks owned by one authenticated AP destination.</summary>
internal sealed class PendingCheckOutbox
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schema_version")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("identity")]
    public required ApSessionIdentity Identity { get; init; }

    [JsonPropertyName("location_ids")]
    public required SortedSet<long> LocationIds { get; init; }

    public static PendingCheckOutbox Create(ApSessionIdentity identity) => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        Identity = identity,
        LocationIds = new SortedSet<long>(),
    };
}
