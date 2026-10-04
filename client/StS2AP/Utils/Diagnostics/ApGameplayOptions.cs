using System.Text.Json.Nodes;

namespace StS2AP.Utils;

internal static class ApGameplayOptions
{
    // Read only resolved, allowlisted options; never serialize slot data or character configs.
    internal static JsonObject Create(IReadOnlyDictionary<string, object> slotData)
    {
        var result = new JsonObject();
        foreach (string key in new[]
        {
            "progressive_starter_card", "progressive_starter_relic", "include_floor_checks",
            "neow_sanity", "campfire_sanity", "gold_sanity", "potion_sanity", "shop_sanity",
            "shuffle_all_cards", "seeded", "death_link", "release_on_victory",
        })
        {
            if (!slotData.TryGetValue(key, out object? value) || value is null) continue;
            int number = Convert.ToInt32(value);
            if (number is not (0 or 1)) throw new InvalidDataException("Invalid telemetry option.");
            result[key] = number == 1;
        }
        if (slotData.TryGetValue("ancient_relic_location", out object? location))
            result["ancient_relic_location"] = Convert.ToInt32(location) switch
            {
                0 => "start_of_act", 1 => "anytime",
                _ => throw new InvalidDataException("Invalid Ancient location."),
            };
        if (slotData.TryGetValue("ancient_relic_pool", out object? pool))
            result["ancient_relic_pool"] = Convert.ToInt32(pool) switch
            {
                0 => "balanced", 1 => "chaos", 2 => "true_chaos",
                _ => throw new InvalidDataException("Invalid Ancient pool."),
            };
        return result;
    }
}
