using System.Text.Json;
using System.Text.Json.Serialization;

namespace StS2AP.Utils;

/// <summary>
/// What a non-relic bonus item gives, decided once per receipt and run and then reused, so
/// reopening the menu or reloading never rerolls it. Relic bonuses keep the serialized relic itself.
/// The JSON travels in the AP reward menu, so multiplayer replicas build the same reward.
/// </summary>
internal sealed class BonusRewardPayload
{
    [JsonPropertyName("ap_bonus")] public string Category { get; set; } = "";
    [JsonPropertyName("enchantment")] public string? Enchantment { get; set; }
    [JsonPropertyName("amount")] public int Amount { get; set; }
    [JsonPropertyName("cards")] public List<string>? Cards { get; set; }
    // The CARDS bonus's pools as the reward describes them, such as "Regent Attack, Colorless Rare".
    [JsonPropertyName("label")] public string? Label { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Returns the payload when <paramref name="json"/> is one, or null for a serialized relic.</summary>
    public static BonusRewardPayload? TryParse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("ap_bonus", out _)
            ? JsonSerializer.Deserialize<BonusRewardPayload>(json)
            : null;
    }
}
