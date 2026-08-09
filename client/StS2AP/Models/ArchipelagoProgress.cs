using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Rewards;
using StS2AP.Extensions;
using StS2AP.Utils;
using static StS2AP.Data.CharTable;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Ascension;
using AscensionManager = StS2AP.Utils.AscensionManager;
using static StS2AP.Data.ItemTable;
using System.Text.Json;


namespace StS2AP.Models
{
    /// <summary>
    /// Tracks the progress of how far along the player is through their Archipelago game
    /// PLEASE NOTE IF YOU CHANGE THIS DATASTRUCTURE, YOU NEED TO UPDATE THE SAVE DATA STRUCTURE
    /// AS WELL. SEE SerializableAP
    /// </summary>
    public class ArchipelagoProgress
    {
        /// <summary>
        /// The maximum possible number of Card Rewards that a player could have replaced with AP locations, regardless of settings.
        /// </summary>
        public const int _maxCardRewards = 20;

        /// <summary>
        /// The maximum possible number of Rare Card Rewards. One for each Boss w/ Rewards.
        /// </summary>
        public const int _maxRareCardRewards = 2;

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

        /// <summary>
        /// The number of floor rewards in floorsanity
        /// </summary>
        public const int _maxFloorRewards = 47;

        /// <summary>
        /// Maximum possible number of Campfire Rewards that a player could find.
        /// </summary>
        public const int _maxCampfireChecks = 6;

        /// <summary>
        /// Maximum number of Ancient Rewards a player could find. Depends on settings.
        /// </summary>
        public static int MaxAncientRewards { 
            get {
                return ArchipelagoClient.Settings?.NeowSanity ?? false ? 3 : 2;
            } 
        }

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
        /// Maps an Archipelago Relic item's index to the choices pre-pulled from the RelicFactory for it.
        /// This ensures that opening/closing or saving/loading the reward screen always shows the same choices.
        /// Cleared on each new run via <see cref="ResetTrackers"/>.
        /// </summary>
        public Dictionary<int, List<RelicModel>> RelicChoiceAssignments { get; set; } = new Dictionary<int, List<RelicModel>>();

        /// <summary>
        /// Maps a Progressive Ancient's AP item index to its three linked relic choices.
        /// The complete set is retained so reopening or loading the reward screen cannot reroll it.
        /// </summary>
        public Dictionary<int, List<RelicModel>> AncientRelicChoiceAssignments { get; set; } = new Dictionary<int, List<RelicModel>>();

        /// <summary>
        /// Maps an Archipelago item's index to the CardReward that was pre-populated for it.
        /// This ensures that even if you skip the Card Reward, it will still be the same if you come back to it later.
        /// </summary>
        public Dictionary<int, CardReward> CardAssignments { get; set; } = new Dictionary<int, CardReward>();

        /// <summary>
        /// Maps an Archipelago item's index to the PotionModel that was pre-pulled from the PotionFactory for it.
        /// This ensures that opening/closing the reward screen always shows the same potion for each potion reward.
        /// Cleared on each new run via <see cref="ResetTrackers"/>.
        /// </summary>
        public Dictionary<int, PotionModel> PotionAssignments { get; set; } = new Dictionary<int, PotionModel>();

        public AscensionManager Ascensions = new AscensionManager();

        /// <summary>
        /// Returns the relic choices assigned to the given AP item, pulling them from the RelicFactory
        /// if they have not been assigned yet. The complete choice is persisted by item index.
        /// </summary>
        /// <param name="index">The index of the specific item sent from the Multiworld.</param>
        /// <param name="player">The current player, needed by RelicFactory.</param>
        /// <param name="choiceCount">The configured number of relics to offer.</param>
        /// <returns>The assigned relic choices, or an empty list if no player is provided or the factory fails.</returns>
        public IReadOnlyList<RelicModel> GetOrAssignRelicChoices(int index, Player player, int choiceCount)
        {
            if (RelicChoiceAssignments.TryGetValue(index, out var existing))
                return existing;

            if (player == null)
            {
                LogUtility.Warn($"Cannot assign relic choices for item w/ index {index}: no active player");
                return Array.Empty<RelicModel>();
            }

            try
            {
                var choices = Enumerable.Range(0, choiceCount)
                    .Select(_ => RelicFactory.PullNextRelicFromFront(player))
                    .ToList();
                RelicChoiceAssignments[index] = choices;
                LogUtility.Info(
                    $"Pre-assigned relic choices for item w/ index {index}: " +
                    string.Join(", ", choices.Select(relic => relic.Id.ToString()))
                );
                return choices;
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to pre-assign relic choices for item w/ index {index}: {ex.Message}");
                return Array.Empty<RelicModel>();
            }
        }

        /// <summary>
        /// Returns the three Ancient relics assigned to a Progressive Ancient, creating the deterministic
        /// assignment on first use. An empty list indicates that a valid three-relic pool could not be built.
        /// </summary>
        public IReadOnlyList<RelicModel> GetOrAssignAncientRelicChoices(int index, Player player)
        {
            if (AncientRelicChoiceAssignments.TryGetValue(index, out var existing))
                return existing;

            if (player == null)
            {
                LogUtility.Warn($"Cannot assign Ancient relic choices for item w/ index {index}: no active player");
                return Array.Empty<RelicModel>();
            }

            var reservedRelicIds = AncientRelicChoiceAssignments.Values
                .SelectMany(assignment => assignment)
                .Select(relic => relic.Id)
                .ToHashSet();
            
            // AllReceivedItems contains multiple reward types, so restrict it to this
            // character's Progressive Ancients. ArchipelagoClient adds these entries only for
            // Anytime mode; with Neow Sanity enabled, it omits the first unlock because
            // that remains Neow's start-of-run reward. Sorting the remaining entries by
            // AP item index maps ordinal 0 to Act 2 and ordinal 1 to Act 3.
            var characterOffset = player.Character.GetCharacterOffset();
            var orderedAncientItemIndices = AllReceivedItems
                .Where(item => item.Item.GetCharacterOffset() == characterOffset &&
                               item.Item.GetCharacterSpecificItemID() == APItem.ProgressiveAncient)
                .OrderBy(item => item.Index)
                .Select(item => item.Index)
                .ToList();

            // This is the item's zero-based position in the ordered list above, not its AP item index.
            var rewardOrdinal = orderedAncientItemIndices.IndexOf(index);
            if (rewardOrdinal is < 0 or > 1)
            {
                LogUtility.Error($"Could not map Ancient reward item index {index} to its Act 2/3 progression");
                return Array.Empty<RelicModel>();
            }

            // ModelDb uses zero-based Act indices: 1 is Act 2 and 2 is Act 3 so convert accordingly
            var ancientActIndex = rewardOrdinal + 1;
            var poolMode = ArchipelagoClient.Settings?.AncientRelicPool ?? AncientRelicPoolMode.Balanced;
            int? poolActIndex = (poolMode == AncientRelicPoolMode.TrueChaos) ? null : ancientActIndex;
            var choiceKey = index.ToString();
            AncientEventModel? naturalAncient = null;
            if (poolMode == AncientRelicPoolMode.Balanced)
            {
                // Balanced must choose from one Ancient, so prefer the Ancient already rolled
                // into this run's ActModel and use a stable same-act fallback only if necessary.
                naturalAncient = AncientRelicPool.ResolveSpecificAncient(
                    player,
                    ancientActIndex,
                    choiceKey,
                    reservedRelicIds
                );
                if (naturalAncient == null)
                    return Array.Empty<RelicModel>();
            }

            var choices = AncientRelicPool.CreateChoices(
                player,
                choiceKey,
                reservedRelicIds,
                poolActIndex,
                naturalAncient
            ).ToList();
            if (choices.Count != AncientRelicPool.ChoiceCount)
                return Array.Empty<RelicModel>();

            AncientRelicChoiceAssignments[index] = choices;
            return choices;
        }

        /// <summary>
        /// Returns the potion assigned to the given location, pulling one from the PotionFactory if it hasn't been assigned yet.
        /// This guarantees that the same potion is shown every time the reward screen is opened for the same item.
        /// </summary>
        /// <param name="index">The index of the specific item sent from the Multiworld.</param>
        /// <param name="player">The current player, needed by PotionFactory.</param>
        /// <returns>The assigned PotionModel, or null if no player is provided or the factory fails.</returns>
        public PotionModel? GetOrAssignPotion(int index, Player player)
        {
            if( PotionAssignments.TryGetValue(index,out var existing))
            {
                return existing;
            }

            if(player == null)
            {
                LogUtility.Warn($"Cannot assign potion for item w/ index {index}; no active player");
            }

            try
            {
                var potion = PotionFactory.CreateRandomPotionOutOfCombat(player, player.PlayerRng.Rewards);
                PotionAssignments[index] = potion;
                LogUtility.Info($"Pre-assigned potion '{potion.Id}' for item w/ index {index}");
                return potion;
            }
            catch(Exception ex)
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
            Ascensions.Initialize(GameUtility.CurrentConfig);
            LogUtility.Info($"Starting game with ascension levels {string.Join(",", Ascensions.CurrentAscension)}");
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
            RelicChoiceAssignments.Clear();
            AncientRelicChoiceAssignments.Clear();
            CardAssignments.Clear();
            PotionAssignments.Clear();
            Ascensions.Reset();
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
        public int UnusedItemCount => AllReceivedItems.Where(i => i.Item.GetCharacterOffset() == GameUtility.CurrentConfig?.CharOffset && !i.Item.ItemDisplayName.Contains("Progressive") && !i.Item.ItemName.Contains("Progressive")).Count() - UsedItems.Count;

        #endregion

        #region My Gold (From the Multiworld)

        /// <summary>
        /// ALL Gold received from the Multiworld
        /// </summary>
        public Dictionary<long, int> GoldReceived { get; set; } = new Dictionary<long, int>();

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
                    var config = GameUtility.CurrentConfig;
                    if(config == null)
                    {
                        return -1;
                    }
                    GoldReceived.TryGetValue(config.CharOffset, out int gold);
                    return gold - GoldRedeemed;
                }
                catch
                {
                    return -1;
                }
            }
        }

        /// <summary>
        ///  Helper function to apply the Poverty Ascension modifier affect
        /// </summary>
        private static int ApplyPoverty(int amount)
        {
            return amount * 3 / 4;
        }
        
        /// <summary>
        /// Calculates the Poverty Refund based on the Gold Redeemed
        /// Should only be called on getting Poverty Ascension Down
        /// </summary>
        /// <returns></returns>
        public int CalculatePovertyRefund()
        {
            return GoldRedeemed - ApplyPoverty(GoldRedeemed);
        }

        /// <summary>
        /// Helps prepare the Gold Reward Display to be displayed to the user accounting for Ascension 3 poverty
        /// </summary>
        /// <returns></returns>
        public ArchipelagoGoldOffer PrepareGoldOffer()
        {
            int consumedBefore = GoldRedeemed;
            int sourceAmount = GoldRemaining;
            bool povertyApplied = Ascensions.HasLevel(AscensionLevel.Poverty);
            
            var consumedAfter = consumedBefore + sourceAmount;

            // consumedBefore and consumedAfter are needed to handle cumulative rounding like if you receive multiple
            // 1 gold rewards in a row which always rounds to 0.
            int grantedAmount = povertyApplied
                ? ApplyPoverty(consumedAfter) - ApplyPoverty(consumedBefore)
                : sourceAmount;

            return new ArchipelagoGoldOffer(
                SourceAmount: sourceAmount,
                GrantedAmount: grantedAmount,
                WithheldAmount: sourceAmount - grantedAmount,
                PovertyApplied: povertyApplied
            );
        }

        /// <summary>
        /// Handles the edge-case when you get an Ascension Down during the AP reward menu.
        /// Updates the GoldRedeemed global state as well.
        /// </summary>
        /// <param name="offer"></param>
        /// <returns> The amount to grant to the player</returns>
        public int ConsumeGoldOffer(ArchipelagoGoldOffer offer)
        {
            bool povertyCurrentlyApplied = Ascensions.HasLevel(AscensionLevel.Poverty);

            GoldRedeemed += offer.SourceAmount;

            if (offer.PovertyApplied && povertyCurrentlyApplied)
            {
                return offer.GrantedAmount;
            }

            if (offer.PovertyApplied)
            {
                // received an Ascension Down while viewing the reward so give proper amount
                return offer.GrantedAmount + offer.WithheldAmount;
            }
            
            return offer.GrantedAmount;
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
        public Dictionary<long, int> ProgressiveSmiths = new Dictionary<long, int>();

        /// <summary>
        /// Keeps track of the number of Progressive Rests we've received for each character
        /// </summary>
        public Dictionary<long, int> ProgressiveRests = new Dictionary<long, int>();

        /// <summary>
        /// Keeps track of the number of Progressive Ancients we've received for each character
        /// </summary>
        public Dictionary<long, int> ProgressiveAncients = new Dictionary<long, int>();

        /// <summary>
        /// Gets the highest Act that a character can rest at
        /// </summary>
        /// <param name="character">The Character's offset</param>
        /// <returns>The highest Act (one-based) that the character can rest at</returns>
        public int? MaxRestLevel(long offset)
        {
            var canRest = ProgressiveRests.TryGetValue(offset, out int act);
            if (!canRest) return null;
            return act;
        }

        /// <summary>
        /// Gets the highest Act that a character can smith at
        /// </summary>
        /// <param name="character">The Character's offset</param>
        /// <returns>The highest Act (one-based) that the character can smith at</returns>
        public int? MaxSmithLevel(long offset)
        {
            var canSmith = ProgressiveSmiths.TryGetValue(offset, out int act);
            if (!canSmith) return null;
            return act;
        }
        public Dictionary<long, int> ShopCardSlotsReceived = new Dictionary<long, int>();
        public Dictionary<long, int> ShopNeutralSlotsReceived = new Dictionary<long, int>();
        public Dictionary<long, int> ShopRelicSlotsReceived = new Dictionary<long, int>();
        public Dictionary<long, int> ShopPotionSlotsReceived = new Dictionary<long, int>();
        public Dictionary<long, int> ShopRemovesReceived = new Dictionary<long, int>();
        public int? MaxShopRemoveLevel(long character)
        {
            var canRemove = ShopRemovesReceived.TryGetValue(character, out int act);
            if (!canRemove) return null;
            return act;
        }
        public Dictionary<string, bool> ShopSlotsChecked { get; set; } = new Dictionary<string, bool>();

        /// <summary>
        /// Returns the highest Act that a character can redeem Progressive Ancients at
        /// </summary>
        /// <param name="character"> The Character's offset</param>
        /// <returns>The highest Act (one-based) that the character can redeem Progressive Ancients at</returns>
        public int MaxProgressiveAncientLevel(long offset)
        {
            int count;
            if(!ProgressiveAncients.TryGetValue(offset, out count))
            {
                count = 0;
            }
            if(!ArchipelagoClient.Settings.NeowSanity)
            {
                count++;
            }
            return count;
        }

        #endregion

        #region StS Save

        public SerializableAP ToSerializable(SerializableRun run)
        {
            using var runJson = JsonDocument.Parse(JsonSerializationUtility.ToJson(run));
            return new SerializableAP()
            {
                SaveData = runJson.RootElement.Clone(),
                CardRewardsAttempted = CardRewardsAttempted,
                RareCardRewardsAttempted = RareCardRewardsAttempted,
                RelicRewardsAttempted = RelicRewardsAttempted,
                GoldRewardsAttempted = GoldRewardsAttempted,
                PotionRewardsAttempted = PotionRewardsAttempted,
                BossRewardsDistributed = BossRewardsDistributed,
                UsedItems = UsedItems,
                GoldRedeemed = GoldRedeemed,
                RelicChoiceAssignments = RelicChoiceAssignments.Select(kv =>
                    new KeyValuePair<int, List<SerializableRelic>>(
                        kv.Key,
                        kv.Value.Select(relic => (relic.IsMutable ? relic : relic.ToMutable()).ToSerializable()).ToList()
                    )
                ).ToDictionary(),
                AncientRelicChoiceAssignments = AncientRelicChoiceAssignments.Select(kv =>
                    new KeyValuePair<int, List<SerializableRelic>>(
                        kv.Key,
                        kv.Value.Select(relic => (relic.IsMutable ? relic : relic.ToMutable()).ToSerializable()).ToList()
                    )
                ).ToDictionary(),
                CardAssignments = CardAssignments.Select((KeyValuePair<int, CardReward> kv) => new KeyValuePair<int, SerializableReward>(kv.Key, kv.Value.ToSerializable())).ToDictionary(),
                CardAssignmentModels = CardAssignments.Select((KeyValuePair<int, CardReward> kv) =>
                new KeyValuePair<int, List<SerializableCard>>(kv.Key, kv.Value.Cards.Select(c => c.ToSerializable()).ToList())).ToDictionary(),
                PotionAssignments = PotionAssignments.Select((KeyValuePair<int, PotionModel> kv) => new KeyValuePair<int, SerializablePotion>(kv.Key, kv.Value.ToMutable().ToSerializable(-1))).ToDictionary(),
                Ascensions = Ascensions.CurrentAscension.Select((level) => ((int)level)).ToList()
            };
        }

        public static ArchipelagoProgress FromSerializable(SerializableAP saveData, Player player)
        {
            LogUtility.Info($"Card Assignments {string.Join(",", saveData.CardAssignments)}");
            var progress = new ArchipelagoProgress()
            {
                CardRewardsAttempted = saveData.CardRewardsAttempted,
                RareCardRewardsAttempted = saveData.RareCardRewardsAttempted,
                RelicRewardsAttempted = saveData.RelicRewardsAttempted,
                GoldRewardsAttempted = saveData.GoldRewardsAttempted,
                PotionRewardsAttempted = saveData.PotionRewardsAttempted,
                BossRewardsDistributed = saveData.BossRewardsDistributed,
                UsedItems = new List<int>(saveData.UsedItems),
                GoldRedeemed = saveData.GoldRedeemed,
                RelicChoiceAssignments = saveData.RelicChoiceAssignments.Select(kv =>
                    new KeyValuePair<int, List<RelicModel>>(
                        kv.Key,
                        kv.Value.Select(RelicModel.FromSerializable).ToList()
                    )
                ).ToDictionary(),
                AncientRelicChoiceAssignments = (saveData.AncientRelicChoiceAssignments ?? new Dictionary<int, List<SerializableRelic>>()).Select(kv =>
                    new KeyValuePair<int, List<RelicModel>>(
                        kv.Key,
                        kv.Value.Select(RelicModel.FromSerializable).ToList()
                    )
                ).ToDictionary(),
                PotionAssignments = saveData.PotionAssignments.Select((KeyValuePair<int, SerializablePotion> kv) => new KeyValuePair<int, PotionModel>(kv.Key, PotionModel.FromSerializable(kv.Value).CanonicalInstance)).ToDictionary(),
            };

            var cardModels = new Dictionary<int, List<CardModel>>();
            foreach(var kv in saveData.CardAssignmentModels)
            {
                var models = kv.Value.Select(cs => player.RunState.CreateCard(CardModel.FromSerializable(cs).CanonicalInstance, player)).ToList();
                cardModels[kv.Key] = models;
            }

            var cardsInfo = typeof(CardReward).GetField("_cards", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if(cardsInfo == null)
            {
                LogUtility.Error("Failed to reflectively access card reward field; cannot repopulate from save");
                return progress;
            }

            var cardRewards = new Dictionary<int, CardReward>();
            foreach(var kv in saveData.CardAssignments)
            {
                if(cardModels.TryGetValue(kv.Key, out var cards))
                {
                    var reward = (CardReward) CardReward.FromSerializable(kv.Value, player);
                    cardRewards[kv.Key] = reward;
                    List<CardCreationResult> cardCreations = (List<CardCreationResult>) cardsInfo.GetValue(reward);
                    foreach(var card in cards)
                    {
                        cardCreations?.Add(new CardCreationResult(card));
                    }
                }
                else
                {
                    LogUtility.Error($"Could not recover card list from save for reward {kv.Key}");
                }
            }

            var ascensionLevels = saveData.Ascensions?.Select((level) => (AscensionLevel)level).ToHashSet() ?? new HashSet<AscensionLevel>();

            progress.Ascensions.Initialize(GameUtility.CurrentConfig, ascensionLevels);

            progress.CardAssignments = cardRewards;

            return progress;
        }

        #endregion
    }
}
