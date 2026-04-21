using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace StS2AP.Models
{
    /// <summary>
    /// Needs to Mirror ArchipelagoProgress, but in a fashion that is safe to serialize to JSON
    /// </summary>
    public class SerializableAP
    {
        [JsonPropertyName("save_data")]
        public SerializableRun? SaveData { get; set; }
        [JsonPropertyName("card_rewards_attempted")]
        public int CardRewardsAttempted { get; set; }
        [JsonPropertyName("rare_card_rewards_attempted")]
        public int RareCardRewardsAttempted { get; set; }
        [JsonPropertyName("relic_rewards_attempted")]
        public int RelicRewardsAttempted { get; set; }
        [JsonPropertyName("gold_rewards_attempted")]
        public int GoldRewardsAttempted { get; set; }
        [JsonPropertyName("potion_rewards_attempted")]
        public int PotionRewardsAttempted { get; set; }
        [JsonPropertyName("boss_rewards_distributed")]
        public int BossRewardsDistributed { get; set; }
        [JsonPropertyName("relic_assignments")]
        public Dictionary<int, SerializableRelic> RelicAssignments { get; set; } = new Dictionary<int, SerializableRelic>();
        [JsonPropertyName("card_assignments")]
        public Dictionary<int, SerializableReward> CardAssignments { get; set; } = new Dictionary<int, SerializableReward>();
        [JsonPropertyName("potion_assignments")]
        public Dictionary<int, SerializablePotion> PotionAssignments { get; set; } = new Dictionary<int, SerializablePotion>();
        [JsonPropertyName("used_items")]
        public List<int> UsedItems { get; set; } = new List<int>();
        [JsonPropertyName("gold_redeemed")]
        public int GoldRedeemed { get; set; }

    }


    [JsonSerializable(typeof(SerializableAP))]
    public partial class APSerializationContext : JsonSerializerContext
    {
        // Code gets generated I guess
    }

    public class SerializationUtility
    {

        public static JsonSerializerOptions CombinedOptions{ get; }

        static SerializationUtility() 
        {
            LogUtility.Info("Getting assembly");
            var megaAssembly = typeof(RunSaveManager).Assembly;
            LogUtility.Info("Getting megaContext");
            var contextType = megaAssembly.GetType("MegaCrit.Sts2.Core.Saves.MegaCritSerializerContext");
            LogUtility.Info("Getting Default");
            var fieldInfo = contextType.GetField("Default", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            LogUtility.Info("Getting Dereferencing Default");
            var megaResolver = (IJsonTypeInfoResolver?)fieldInfo?.GetValue(null);

            LogUtility.Info("Getting Options");
            var optionsInfo = contextType.GetField("s_defaultOptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            LogUtility.Info("Dereferencing Options");
            var megaOptions = (JsonSerializerOptions?)optionsInfo?.GetValue(null);

            CombinedOptions = new JsonSerializerOptions(megaOptions)
            {
                TypeInfoResolver = JsonTypeInfoResolver.Combine(megaResolver, APSerializationContext.Default)
            };
        }

    }
}
