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

    /// <summary>
    /// How many events the run's graph log held when this save was made. Reloading the save marks
    /// every later event as undone, including claims on the save's own floor that the reload
    /// lets the player make again. Saves from before this field fall back to the floor.
    /// </summary>
    [JsonPropertyName("run_graph_event_count")]
    public int? RunGraphEventCount { get; set; }
}
