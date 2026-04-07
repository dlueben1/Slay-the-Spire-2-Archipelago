using Archipelago.MultiClient.Net.Models;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Rewards;
using StS2AP.Extensions;
using StS2AP.Utils;
using static StS2AP.Data.CharTable;


namespace StS2AP.Models
{
    /// <summary>
    /// Tracks the progress of how far along the player is through their Archipelago game
    /// </summary>
    public class ArchipelagoProgress : IPacketSerializable
    {
        /// <summary>
        /// The maximum possible number of Card Rewards that a player could have replaced with AP locations, regardless of settings.
        /// </summary>
        public const int _maxCardRewards = 20;

        /// <summary>
        /// The maximum possible number of Relic Rewards that a player could have replaced with AP locations, regardless of settings.
        /// </summary>
        public const int _maxRelicRewards = 10;

        /// <summary>
        /// The maximum possible number of Gold Rewards that a player could have replaced with AP locations.
        /// Only used if GoldSanity is on, but this is the upper bound on how many gold rewards we would replace even if it is.
        /// </summary>
        public const int _maxGoldRewards = 20;

        /// <summary>
        /// The maximum possible number of Potion Rewards that a player could have replaced with AP locations.
        /// Only used if PotionSanity is on. Matches the APWorld's 9 locations per character.
        /// </summary>
        public const int _maxPotionRewards = 9;

        public const int _maxBossRewards = 3;

        #region Per-Run Tracker

        /// <summary>
        /// Keeps track of the number of times that the game has tried to provide a Card Reward.
        /// Used to keep track of when to replace a Card Reward with an AP Location.
        /// </summary>
        public int CardRewardsAttempted { get; set; } = 0;

        /// <summary>
        /// Keeps track of the number of times that the game has tried to provide a Rare Card Reward.
        /// Used to keep track of when to replace a Rare Card Reward with an AP Location.
        /// TODO: We may want to enforce this for Bosses only in the future, in case events can provide this.
        /// </summary>
        public int RareCardRewardsAttempted { get; set; } = 0;

        /// <summary>
        /// Keeps track of the number of times the game has tried to provide a Relic Reward.
        /// Used to keep track of when to replace a Relic Reward with an AP Location.
        /// </summary>
        public int RelicRewardsAttempted { get; set; } = 0;

        /// <summary>
        /// Keeps track of the number of times the game has tried to provide a Gold Reward.
        /// It's only used if the player has GoldSanity on.
        /// </summary>
        public int GoldRewardsAttempted { get; set; } = 0;

        /// <summary>
        /// Keeps track of the number of times the game has tried to provide a Potion Reward.
        /// It's only used if the player has PotionSanity on.
        /// </summary>
        public int PotionRewardsAttempted { get; set; } = 0;
        
        /// <summary>
        /// Keeps track of the number of times the game has tried to provide a Boss Reward.
        /// </summary>
        public int BossRewardsDistributed { get; set; } = 0;

        public Dictionary<string, bool> CampfiresChecked { get; set; } = new Dictionary<string, bool>();

        /// <summary>
        /// Maps an Archipelago item's index to the RelicModel that was pre-pulled from the RelicFactory for it.
        /// This ensures that opening/closing the reward screen always shows the same relic for each relic reward.
        /// Cleared on each new run via <see cref="ResetTrackers"/>.
        /// </summary>
        public Dictionary<int, RelicModel> RelicAssignments { get; set; } = new Dictionary<int, RelicModel>();

        /// <summary>
        /// Maps an Archipelago item's index to the CardReward that was pre-populated for it.
        /// This ensures that even if you skip the Card Reward, it will still be the same if you come back to it later.
        /// </summary>
        public Dictionary<int, CardReward> CardAssignments { get; set; } = new Dictionary<int, CardReward>();

        /// <summary>
        /// Returns the relic assigned to the given location, pulling one from the RelicFactory if it hasn't been assigned yet.
        /// This guarantees that the same relic is shown every time the reward screen is opened for the same item.
        /// </summary>
        /// <param name="index">The index of the specific item sent from the Multiworld.</param>
        /// <param name="player">The current player, needed by RelicFactory.</param>
        /// <returns>The assigned RelicModel, or null if no player is provided or the factory fails.</returns>
        public RelicModel? GetOrAssignRelic(int index, Player player)
        {
            if (RelicAssignments.TryGetValue(index, out var existing))
                return existing;

            if (player == null)
            {
                LogUtility.Warn($"Cannot assign relic for item w/ index {index}: no active player");
                return null;
            }

            try
            {
                var relic = RelicFactory.PullNextRelicFromFront(player);
                RelicAssignments[index] = relic;
                LogUtility.Info($"Pre-assigned relic '{relic.Id}' for item w/ index {index}");
                return relic;
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to pre-assign relic for item w/ index {index}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fires when a run starts, to make sure that all progress trackers are reset and ready to go.
        /// </summary>
        /// <param name="player">The current player, needed to initialize trackers.</param>
        public void InitializeTrackers(Player player)
        {
            ResetTrackers();
            InitializeFromServer(player);
        }

        public void InitializeFromServer(Player player)
        {
            var name = player.APName();
            for (int i = 1; i <= 3; i++)
            {
                for (int j = 1; j <= 2; j++)
                {
                    var checkName = $"{name} Act {i} Campfire {j}";
                    var locationId = ArchipelagoClient.Session.Locations.GetLocationIdFromName("Slay the Spire II", checkName);
                    CampfiresChecked[checkName] = ArchipelagoClient.Session.Locations.AllLocationsChecked.Contains(locationId);
                }
            }
        }

        public void ResetTrackers()
        {
            CardRewardsAttempted = 0;
            RareCardRewardsAttempted = 0;
            BossRewardsDistributed = 0;
            RelicRewardsAttempted = 0;
            GoldRewardsAttempted = 0;
            PotionRewardsAttempted = 0;
            CampfiresChecked.Clear();
            RelicAssignments.Clear();
            CardAssignments.Clear();
            GoldRedeemed = 0;
        }



        #endregion

        #region My Items (From the Multiworld)

        /// <summary>
        /// All items we've received from the multiworld. Gets dumped into `AvailableItems` at the start of each run.
        /// </summary>
        public List<IndexedItemInfo> AllReceivedItems = new List<IndexedItemInfo>();

        /// <summary>
        /// Any items that have been used up in the current run live here. The difference between this and `AllReceivedItems` 
        /// represents the items still available for use.
        /// </summary>
        public List<int> UsedItems = new List<int>();

        /// <summary>
        /// The number of items we've received from the multiworld that we haven't used yet. 
        /// This is what gets displayed in the top bar UI.
        /// </summary>
        public int UnusedItemCount => AllReceivedItems.Where(i => i.Item.GetStSCharID() == GameUtility.CurrentCharacterID && !i.Item.ItemDisplayName.Contains("Progressive") && !i.Item.ItemName.Contains("Progressive")).Count() - UsedItems.Count;

        #endregion

        #region My Gold (From the Multiworld)

        /// <summary>
        /// ALL Gold received from the Multiworld
        /// </summary>
        public Dictionary<APItemCharID, int> GoldReceived { get; set; } = new Dictionary<APItemCharID, int>();

        /// <summary>
        /// The Gold you've redeemed so far this run
        /// </summary>
        public int GoldRedeemed { get; set; } = 0;

        /// <summary>
        /// The amount of Gold you have left to redeem from the Multiworld.
        /// Returns -1 if the value could not be retrieved.
        /// </summary>
        public int GoldRemaining
        {
            get
            {
                try
                {
                    if (!GameUtility.CurrentCharacterID.HasValue) return -1;
                    GoldReceived.TryGetValue(GameUtility.CurrentCharacterID.Value, out int gold);
                    return gold - GoldRedeemed;
                }
                catch
                {
                    return -1;
                }
            }
        }

        #endregion

        #region My Unlocks (From the Multiworld)

        /// <summary>
        /// Collection of all the characters that should be unlocked.
        /// 
        /// If you want to add a character to the unlocked list, you'll need to add it using the `ModelDb.Character<>()` function.
        /// For example, to add the Necrobinder, you'd need to do:
        /// `ArchipelagoClient.Progress.UnlockedCharacters.Add(ModelDb.Character<Characters.Necrobinder>());`
        /// 
        /// Instead of modifying this directly, use <see cref="GameUtility.UnlockCharacter(CharacterModel)"/>
        /// </summary>
        public List<CharacterModel> UnlockedCharacters { get; set; } = new List<CharacterModel>();

        /// <summary>
        /// Keeps track of the number of Progressive Smiths we've received for each character
        /// </summary>
        public Dictionary<APItemCharID, int> ProgressiveSmiths = new Dictionary<APItemCharID, int>();

        /// <summary>
        /// Keeps track of the number of Progressive Rests we've received for each character
        /// </summary>
        public Dictionary<APItemCharID, int> ProgressiveRests = new Dictionary<APItemCharID, int>();

        /// <summary>
        /// Gets the highest Act that a character can rest at
        /// </summary>
        /// <param name="character">The Character's encoded ItemID</param>
        /// <returns>The highest Act (one-based) that the character can rest at</returns>
        public int? MaxRestLevel(APItemCharID character)
        {
            var canRest = ProgressiveRests.TryGetValue(character, out int act);
            if (!canRest) return null;
            return act;
        }

        /// <summary>
        /// Gets the highest Act that a character can smith at
        /// </summary>
        /// <param name="character">The Character's encoded ItemID</param>
        /// <returns>The highest Act (one-based) that the character can smith at</returns>
        public int? MaxSmithLevel(APItemCharID character)
        {
            var canSmith = ProgressiveSmiths.TryGetValue(character, out int act);
            if (!canSmith) return null;
            return act;
        }

        #endregion

        #region StS Save

        /// <summary>
        /// Saves the progress into a JSON object; called as part of saving a SerializableRun.
        /// 
        /// Note: Unlocks (like Characters, Progressive Smith/Rest levels, etc.) should NOT be serialized, as they are already synced at the start of each run. 
        /// Only progress on the current run (like how many rewards have been attempted, what items have been used, etc.) should be serialized here.
        /// </summary>
        public void Serialize(PacketWriter writer)
        {
            writer.WriteInt(CardRewardsAttempted);
            writer.WriteInt(RareCardRewardsAttempted);
            writer.WriteInt(BossRewardsDistributed);
            writer.WriteInt(RelicRewardsAttempted);
            writer.WriteInt(GoldRewardsAttempted);
            writer.WriteInt(PotionRewardsAttempted);

            writer.WriteInt(UsedItems.Count);
            foreach (var used in UsedItems)
            {
                writer.WriteInt(used);
            }
            writer.WriteInt(RelicAssignments.Count());
            foreach (var entry in RelicAssignments)
            {
                writer.WriteInt(entry.Key);
                // Relics are weird, needs to be made mutable in order to serialize
                writer.Write<SerializableRelic>(entry.Value.ToMutable().ToSerializable());
            }

            writer.WriteInt(GoldReceived.Count);
            foreach (var entry in GoldReceived)
            {
                writer.WriteInt(((int)entry.Key));
                writer.WriteInt(entry.Value);
            }

            writer.WriteInt(GoldRedeemed);
        }

        /// <summary>
        /// Reads the AP data from a JSON object; called as part of loading a SerializableRun
        /// </summary>
        public void Deserialize(PacketReader reader)
        {
            try
            {
                CardRewardsAttempted = reader.ReadInt();
                RareCardRewardsAttempted = reader.ReadInt();
                BossRewardsDistributed = reader.ReadInt();
                RelicRewardsAttempted = reader.ReadInt();
                GoldRewardsAttempted = reader.ReadInt();
                PotionRewardsAttempted = reader.ReadInt();
                var usedItemsCount = reader.ReadInt();
                for (int i = 0; i < usedItemsCount; i++)
                {
                    UsedItems.Add(reader.ReadInt());
                }
                var relicAssignmentsCount = reader.ReadInt();
                for (int i = 0; i < relicAssignmentsCount; i++)
                {
                    var index = reader.ReadInt();
                    var relic = reader.Read<SerializableRelic>();
                    RelicAssignments[index] = RelicModel.FromSerializable(relic).CanonicalInstance;
                }
                var goldReceivedCount = reader.ReadInt();
                for (int i = 0; i < goldReceivedCount; i++)
                {
                    var charId = (APItemCharID)reader.ReadInt();
                    var amount = reader.ReadInt();
                    GoldReceived[charId] = amount;
                }

                GoldRedeemed = reader.ReadInt();
            }
            catch(Exception ex)
            {
                LogUtility.Error($"Failed to laod AP save data {ex.Message}");
                throw;
            }
        }

        #endregion
    }
}