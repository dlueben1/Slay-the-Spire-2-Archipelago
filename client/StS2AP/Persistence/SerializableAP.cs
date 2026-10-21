using System.Text.Json;
using System.Text.Json.Serialization;

namespace StS2AP.Persistence;

/// <summary>Singleplayer envelope around canonical AP progress and the native run save.</summary>
public sealed class SerializableAP
{
    [JsonPropertyName("progress")]
    public ApRunProgressState Progress { get; set; } = new();

    [JsonPropertyName("save_data")]
    public JsonElement? SaveData { get; set; }
}
