using System.Text.Json.Serialization;

namespace StS2AP.Persistence;

/// <summary>
/// Run-scoped AP progress shared by the singleplayer envelope and canonical multiplayer state.
/// </summary>
public sealed class ApRunProgressState
{
    [JsonPropertyName("ancient_settings_for_run")]
    public AncientRewardSettings? AncientSettingsForRun { get; set; }
    [JsonPropertyName("initialized")]
    public bool Initialized { get; set; }
    [JsonPropertyName("card_rewards_attempted")]
    public int CardRewardsAttempted { get; set; }
    [JsonPropertyName("rare_card_rewards_attempted")]
    public int RareCardRewardsAttempted { get; set; }
    [JsonPropertyName("relic_rewards_attempted")]
    public int RelicRewardsAttempted { get; set; }
    [JsonPropertyName("banked_relic_rewards")]
    public int BankedRelicRewards { get; set; }
    [JsonPropertyName("relic_rewards_available_anytime_for_run")]
    public int RelicRewardsAvailableAnytimeForRun { get; set; }
    [JsonPropertyName("relic_receipt_indexes_by_character")]
    public Dictionary<long, List<int>> RelicReceiptIndexesByCharacter { get; set; } = new();
    [JsonPropertyName("gold_rewards_attempted")]
    public int GoldRewardsAttempted { get; set; }
    [JsonPropertyName("potion_rewards_attempted")]
    public int PotionRewardsAttempted { get; set; }
    [JsonPropertyName("boss_rewards_distributed")]
    public int BossRewardsDistributed { get; set; }
    [JsonPropertyName("multiplayer_boss_compensated_acts")]
    public HashSet<int> MultiplayerBossCompensatedActs { get; set; } = new();
    [JsonPropertyName("relic_choice_assignments")]
    public Dictionary<int, List<string>> RelicChoiceAssignments { get; set; } = new();
    [JsonPropertyName("ancient_relic_choice_assignments")]
    public Dictionary<int, List<string>> AncientRelicChoiceAssignments { get; set; } = new();
    [JsonPropertyName("progressive_ancients")]
    public Dictionary<long, int> ProgressiveAncients { get; set; } = new();
    [JsonPropertyName("progressive_rests")]
    public Dictionary<long, int> ProgressiveRests { get; set; } = new();
    [JsonPropertyName("progressive_smiths")]
    public Dictionary<long, int> ProgressiveSmiths { get; set; } = new();
    [JsonPropertyName("checked_campfire_location_ids")]
    public HashSet<long> CheckedCampfireLocationIds { get; set; } = new();
    [JsonPropertyName("card_assignments")]
    public Dictionary<int, ApCardAssignmentState> CardAssignments { get; set; } = new();
    [JsonPropertyName("potion_assignments")]
    public Dictionary<int, string> PotionAssignments { get; set; } = new();
    [JsonPropertyName("used_items")]
    public List<int> UsedItems { get; set; } = new();
    [JsonPropertyName("gold_redeemed")]
    public int GoldRedeemed { get; set; }
    [JsonPropertyName("progressive_starter_card_base_id")]
    public string? ProgressiveStarterCardBaseId { get; set; }
    [JsonPropertyName("progressive_starter_card_upgraded_id")]
    public string? ProgressiveStarterCardUpgradedId { get; set; }
    [JsonPropertyName("progressive_starter_card_tier")]
    public ProgressiveStarterTier ProgressiveStarterCardTier { get; set; } =
        ProgressiveStarterTier.Unsupported;
    [JsonPropertyName("progressive_starter_relic_base_id")]
    public string? ProgressiveStarterRelicBaseId { get; set; }
    [JsonPropertyName("progressive_starter_relic_upgraded_id")]
    public string? ProgressiveStarterRelicUpgradedId { get; set; }
    [JsonPropertyName("progressive_starter_relic_tier")]
    public ProgressiveStarterTier ProgressiveStarterRelicTier { get; set; } =
        ProgressiveStarterTier.Unsupported;
    [JsonPropertyName("pending_location_checks")]
    public HashSet<long> PendingLocationChecks { get; set; } = new();
    [JsonPropertyName("ascensions")]
    public List<int> Ascensions { get; set; } = new();
}
