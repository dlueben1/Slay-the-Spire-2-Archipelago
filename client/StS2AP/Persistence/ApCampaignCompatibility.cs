using System.Globalization;
using System.Text.Json;

namespace StS2AP.Persistence;

/// <summary>Rejects unsupported beta campaigns before RitsuLib imports or defaults their AP data.</summary>
internal static class ApCampaignCompatibility
{
    internal const string Refusal = "This campaign uses an unsupported early-beta AP save format. "
        + "Start a new campaign. The saved campaign was preserved.";

    internal static string? GetError(string json, string modId, int schema, IEnumerable<ulong> playerIds)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!TryObject(root, "_ritsulib", out var ritsu)
                || !TryObject(ritsu, "run_saved_data", out var extensions)
                || !TryObject(extensions, modId, out var ap)
                || !TryObject(ap, "ap_run", out var run)
                || !HasSchema(run, "schema", schema)
                || !TryObject(run, "data", out var shared)
                || !HasSchema(shared, "SchemaVersion", schema)
                || !TryObject(ap, "ap_players", out var entry)
                || !HasSchema(entry, "schema", schema)
                || !TryObject(entry, "players", out var players))
            {
                return Refusal;
            }

            foreach (ulong netId in playerIds)
            {
                if (!TryObject(players, netId.ToString(CultureInfo.InvariantCulture), out var player)
                    || !HasSchema(player, "SchemaVersion", schema))
                {
                    return Refusal;
                }
            }

            // A prior card contract may share the schema number but cannot open the current picker.
            foreach (JsonProperty player in players.EnumerateObject())
            {
                if (!HasSchema(player.Value, "SchemaVersion", schema)
                    || !TryObject(player.Value, "Progress", out var progress)
                    || !TryObject(progress, "card_assignments", out var cards))
                {
                    return Refusal;
                }
                foreach (JsonProperty card in cards.EnumerateObject())
                {
                    if (card.Value.ValueKind != JsonValueKind.Object
                        || !card.Value.TryGetProperty("materialization_strategy_id", out var strategy)
                        || strategy.ValueKind != JsonValueKind.String
                        || strategy.GetString() != "ap_rng_replicated_card_v1"
                        || (card.Value.TryGetProperty("applied_effects", out var effects)
                            && (effects.ValueKind != JsonValueKind.Array || effects.GetArrayLength() != 0)))
                    {
                        return Refusal;
                    }
                }
            }
            return null;
        }
        catch (JsonException)
        {
            return Refusal;
        }
    }

    private static bool TryObject(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Object;
    }

    private static bool HasSchema(JsonElement value, string name, int schema) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var version)
        && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out int actual) && actual == schema;
}
