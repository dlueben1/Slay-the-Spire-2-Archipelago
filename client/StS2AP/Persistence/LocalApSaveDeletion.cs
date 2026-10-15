using System.Text.Json;

namespace StS2AP.Persistence;

internal static class LocalApSaveDeletion
{
    internal static bool ContainsArchipelagoRunData(string json, string modId)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(modId))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("_ritsulib", out JsonElement ritsu)
                && ritsu.ValueKind == JsonValueKind.Object
                && ritsu.TryGetProperty("run_saved_data", out JsonElement runData)
                && runData.ValueKind == JsonValueKind.Object
                && runData.TryGetProperty(modId, out JsonElement apData)
                && apData.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            // An unknown or damaged native save must be preserved rather than guessed to be AP-owned.
            return false;
        }
    }
}
