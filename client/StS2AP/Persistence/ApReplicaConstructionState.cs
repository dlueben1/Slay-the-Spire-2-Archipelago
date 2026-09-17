using System.Text.Json.Serialization;

namespace StS2AP.Persistence;

/// <summary>
/// Process-local cursors used while every multiplayer replica independently constructs native
/// rewards for one player. The fixed host persists its copy as a reconnect baseline, but live
/// owner progress messages must never advance another replica's copy.
/// </summary>
public sealed class ApReplicaConstructionState
{
    [JsonPropertyName("initialized")]
    public bool Initialized { get; set; }

    [JsonPropertyName("card_rewards_attempted")]
    public int CardRewardsAttempted { get; set; }

    [JsonPropertyName("rare_card_rewards_attempted")]
    public int RareCardRewardsAttempted { get; set; }

    [JsonPropertyName("gold_rewards_attempted")]
    public int GoldRewardsAttempted { get; set; }

    [JsonPropertyName("potion_rewards_attempted")]
    public int PotionRewardsAttempted { get; set; }

    [JsonPropertyName("multiplayer_boss_compensated_acts")]
    public HashSet<int> MultiplayerBossCompensatedActs { get; set; } = new();

    /// <summary>
    /// Seeds a new replica from the authoritative checkpoint. Once live construction begins,
    /// later owner publications are deliberately ignored by this method.
    /// </summary>
    public bool EnsureInitialized(
        int cardRewardsAttempted,
        int rareCardRewardsAttempted,
        int goldRewardsAttempted,
        int potionRewardsAttempted,
        IEnumerable<int> multiplayerBossCompensatedActs)
    {
        if (Initialized)
            return false;

        CardRewardsAttempted = cardRewardsAttempted;
        RareCardRewardsAttempted = rareCardRewardsAttempted;
        GoldRewardsAttempted = goldRewardsAttempted;
        PotionRewardsAttempted = potionRewardsAttempted;
        MultiplayerBossCompensatedActs = new HashSet<int>(multiplayerBossCompensatedActs);
        Initialized = true;
        return true;
    }

    public int IncrementCardRewards() => ++CardRewardsAttempted;

    public int IncrementRareCardRewards() => ++RareCardRewardsAttempted;

    public int IncrementGoldRewards() => ++GoldRewardsAttempted;

    public int IncrementPotionRewards() => ++PotionRewardsAttempted;

    public bool TryMarkBossCompensation(int act) => MultiplayerBossCompensatedActs.Add(act);
}
