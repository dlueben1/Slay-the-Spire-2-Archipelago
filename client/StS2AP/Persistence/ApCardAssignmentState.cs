using System.Text.Json.Serialization;

namespace StS2AP.Persistence;

public sealed class ApCardAssignmentState
{
    [JsonPropertyName("serialized_cards")]
    public List<string> SerializedCards { get; set; } = new();

    [JsonPropertyName("can_reroll")]
    public bool CanReroll { get; set; }

    [JsonPropertyName("is_rare")]
    public bool IsRare { get; set; }

    [JsonPropertyName("reward_act_index")]
    public int? RewardActIndex { get; set; }

    [JsonPropertyName("has_been_revealed")]
    public bool HasBeenRevealed { get; set; }

    [JsonPropertyName("materialization_strategy_id")]
    public string MaterializationStrategyId { get; set; } = string.Empty;

    [JsonPropertyName("applied_effects")]
    public List<ApRewardEffectSpec> AppliedEffects { get; set; } = new();
}
