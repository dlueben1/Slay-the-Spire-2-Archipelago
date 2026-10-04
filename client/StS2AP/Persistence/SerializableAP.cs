using System.Text.Json;
using System.Text.Json.Serialization;

namespace StS2AP.Persistence;

/// <summary>Singleplayer envelope around canonical AP progress and the native run save.</summary>
public sealed class SerializableAP
{
    [JsonPropertyName("player_number")]
    public int PlayerNumber { get; set; } = 1;
    // Custom checkpoints bypass RitsuLib's native save writer, so keep the run identity explicitly.
    [JsonPropertyName("run_id")]
    public Guid RunId { get; set; }

    [JsonPropertyName("progress")]
    public ApRunProgressState Progress { get; set; } = new();

    [JsonPropertyName("save_data")]
    public JsonElement? SaveData { get; set; }
}
