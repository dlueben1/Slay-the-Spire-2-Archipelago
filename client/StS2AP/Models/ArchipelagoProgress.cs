using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Rewards;
using StS2AP.Extensions;
using StS2AP.Data;
using StS2AP.Utils;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Entities.Ascension;
using AscensionManager = StS2AP.Utils.AscensionManager;
using static StS2AP.Data.ItemTable;
using System.Text.Json;


namespace StS2AP.Models
{
    /// <summary>
    /// Runtime AP progress for the current player. <see cref="ToRunProgressState"/> and
    /// <see cref="FromRunProgressState"/> are the single persistence boundary used by both
    /// singleplayer saves and multiplayer host checkpoints.
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
        /// The maximum Progressive Shop Card Removal level a player could receive (one per Act).
        /// </summary>
        public const int _maxShopRemoves = 3;

        /// <summary>
        /// The number of floor rewards in floorsanity
        /// </summary>
        public const int _maxFloorRewards = 47;

        /// <summary>
        /// Maximum possible number of Campfire Rewards that a player could find.
        /// </summary>
        public const int _maxCampfireChecks = 6;

        public const int _maxAncientChecks = 3;

        /// <summary>
        /// Maximum number of Ancient Rewards a player could find. Depends on settings.
        /// </summary>
        public static int MaxConfiguredAncients { 
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
        /// Eligible Elite, chest, and Black Star rewards encountered this run. Attempts after ten
        /// are still counted so we can give 'natural relics'.
        /// </summary>
        public int RelicRewardsAttempted { get; set; } = 0;

        /// <summary>
        /// Earned relic rewards not yet paired with a received Relic item. A bank is spent when
        /// the receipt is committed to either a native reward or a saved AP-menu assignment.
        /// </summary>
        public int BankedRelicRewards { get; set; } = 0;

        /// <summary>
        /// Run-start snapshot of the effective anytime setting. Changing local settings cannot
        /// move receipts between anytime and reward-required while a run is in progress.
        /// </summary>
        public int RelicRewardsAvailableAnytimeForRun { get; set; } = 2;

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

        /// <summary>
        /// One-based acts whose structurally missing multiplayer checks were already supplied at
        /// boss entry. Persisting this prevents boss-room reloads from advancing reward counters twice.
        /// </summary>
        public HashSet<int> MultiplayerBossCompensatedActs { get; set; } = new();

        /// <summary>
        /// Campfiresanity locations already present in the AP slot or queued by this client.
        /// Location IDs are deterministic across STS replicas and are the canonical identity.
        /// </summary>
        public HashSet<long> CheckedCampfireLocationIds { get; set; } = new();

        /// <summary>
        /// Maps an Archipelago Relic item's index to the choices pre-pulled from the RelicFactory for it.
        /// Only AP-menu rewards use this; native Elite, chest, and Black Star rewards never do.
        /// This ensures that reopening or loading the AP menu shows the same choice.
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
        /// Returns the relic choices assigned to the given AP item. Singleplayer preserves the
        /// native RelicFactory behavior. Multiplayer uses AP's stable selector so building a
        /// mirrored reward recipe cannot advance only one replica's reward RNG or grab bags.
        /// </summary>
        /// <param name="index">The index of the specific item sent from the Multiworld.</param>
        /// <param name="player">The current player, needed by RelicFactory.</param>
        /// <param name="choiceCount">The number of relics to persist for this item.</param>
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
                List<RelicModel> choices;
                if (MultiplayerSupport.IsRealMultiplayerRun)
                {
                    var reservedRelicIds = RelicChoiceAssignments.Values
                        .SelectMany(assignment => assignment)
                        .Select(relic => relic.Id)
                        .ToHashSet();
                    choices = StandardRelicPool.CreateChoices(
                        player,
                        $"{player.NetId}:{index}",
                        choiceCount,
                        reservedRelicIds
                    ).ToList();
                }
                else
                {
                    choices = Enumerable.Range(0, choiceCount)
                        .Select(_ => RelicFactory.PullNextRelicFromFront(player))
                        .ToList();
                }
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
        public IReadOnlyList<RelicModel> GetOrAssignAncientRelicChoices(
            int index,
            Player player,
            string? stableChoiceKey = null)
        {
            if (AncientRelicChoiceAssignments.TryGetValue(index, out var existing))
                return existing;

            var reservedRelicIds = AncientRelicChoiceAssignments.Values
                .SelectMany(assignment => assignment)
                .Select(relic => relic.Id)
                .ToHashSet();
            
            // AllReceivedItems contains multiple reward types, so restrict it to this
            // character's Progressive Ancients. ArchipelagoClient adds these entries only for
            // Anytime mode. Sorting them by AP item index maps their ordinals to Neow/Act 2/Act 3
            // when Neow Sanity is enabled, or Act 2/Act 3 otherwise.
            var characterOffset = player.GetAPCharacterNumber();
            var orderedAncientItemIndices = AllReceivedItems
                .Where(item => item.Item.GetAPCharacterNumber() == characterOffset &&
                               item.Item.GetCharacterItemType() == APItem.ProgressiveAncient)
                .OrderBy(item => item.Index)
                .Select(item => item.Index)
                .ToList();

            // This is the item's zero-based position in the ordered list above, not its AP item index.
            var rewardOrdinal = orderedAncientItemIndices.IndexOf(index);
            var includesNeowReward = ArchipelagoClient.Settings?.NeowSanity ?? false;
            var maxRewardOrdinal = includesNeowReward ? 2 : 1;
            var expectedProgression = includesNeowReward ? "Neow/Act 2/Act 3" : "Act 2/Act 3";
            if (rewardOrdinal < 0)
            {
                LogUtility.Error($"Could not map Ancient reward item index {index} to its {expectedProgression} progression");
                return Array.Empty<RelicModel>();
            }
            if (rewardOrdinal > maxRewardOrdinal)
            {
                // Preserve surplus receipts as unavailable rows. Neow Sanity adds one
                // claimable reward before the Act 2 and Act 3 rewards.
                LogUtility.Info(
                    $"Progressive Ancient item index {index} has no {expectedProgression} reward; "
                        + $"claimableOrdinal={rewardOrdinal}"
                );
                return Array.Empty<RelicModel>();
            }

            // With Neow Sanity, ordinals 0/1/2 map to Act indices 0/1/2.
            // Otherwise ordinals 0/1 map to Act indices 1/2.
            var ancientActIndex = rewardOrdinal + (includesNeowReward ? 0 : 1);
            var poolMode = AncientSettingsUtility.Current.Pool;
            // True Chaos combines only Act 2/3; Neow's reward remains Neow-only.
            int? poolActIndex = ancientActIndex == 0
                ? 0
                : (poolMode == AncientRelicPoolMode.TrueChaos ? null : ancientActIndex);
            // AP slot + received index is supplied by the multiplayer grant ledger. The
            // singleplayer fallback retains the historical item-index key.
            var choiceKey = stableChoiceKey ?? index.ToString();
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
        public PotionModel? GetOrAssignPotion(int index, Player? player)
        {
            if( PotionAssignments.TryGetValue(index,out var existing))
            {
                return existing;
            }

            if(player == null)
            {
                LogUtility.Warn($"Cannot assign potion for item w/ index {index}; no active player");
                return null;
            }

            try
            {
                var potion = PotionFactory.CreateRandomPotionOutOfCombat(
                    player,
                    player.PlayerRng.Rewards
                ).ToMutable();
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
            RefreshCheckedCampfiresFromClient();
            CharacterConfig? currentConfig = GameUtility.CurrentConfig;
            if (currentConfig == null)
            {
                const string message =
                    "Cannot initialize AP progress without a current character configuration";
                LogUtility.Error(message);
                throw new InvalidOperationException(message);
            }
            Ascensions.Initialize(currentConfig);
            LogUtility.Info($"Starting game with ascension levels {string.Join(",", Ascensions.CurrentAscension)}");
        }

        public void RefreshCheckedCampfiresFromClient()
        {
            CheckedCampfireLocationIds.UnionWith(
                ArchipelagoClient.CheckedLocations.Where(LocationData.IsCampfireLocationId)
            );
            CheckedCampfireLocationIds.UnionWith(
                PendingLocationChecks.Where(LocationData.IsCampfireLocationId)
            );
        }

        public AncientRewardSettings? AncientSettingsForRun { get; set; }

        public void ResetTrackers()
        {
            AncientSettingsForRun = MultiplayerSupport.IsRealMultiplayerRun
                ? AncientSettingsUtility.Current
                : AncientSettingsUtility.ForNewRun;
            LogUtility.Info($"Ancient rewards for new run: mode={AncientSettingsForRun.Location}, pool={AncientSettingsForRun.Pool}");
            Items.StartNewRun();
            CardRewardsAttempted = 0;
            RareCardRewardsAttempted = 0;
            BossRewardsDistributed = 0;
            RelicRewardsAttempted = 0;
            BankedRelicRewards = 0;
            RelicRewardsAvailableAnytimeForRun = RelicRewardUtility.EffectiveAvailableAnytime;
            GoldRewardsAttempted = 0;
            PotionRewardsAttempted = 0;
            MultiplayerBossCompensatedActs.Clear();
            CheckedCampfireLocationIds.Clear();
            ShopSlotsChecked.Clear();
            RelicChoiceAssignments.Clear();
            AncientRelicChoiceAssignments.Clear();
            CardAssignments.Clear();
            PotionAssignments.Clear();
            Ascensions.Reset();
            GoldRedeemed = 0;
            ProgressiveStarterCardBaseId = null;
            ProgressiveStarterCardUpgradedId = null;
            ProgressiveStarterCardTier = ProgressiveStarterTier.Unsupported;
            ProgressiveStarterRelicBaseId = null;
            ProgressiveStarterRelicUpgradedId = null;
            ProgressiveStarterRelicTier = ProgressiveStarterTier.Unsupported;
        }



        #endregion

        #region My Items (From the Multiworld)

        /// <summary>
        /// Owns selected receipts and their per-run consumption. Slot changes replace this progress;
        /// history refreshes preserve consumption, while ResetTrackers starts fresh consumption.
        /// </summary>
        public ApReceivedItemLedger Items { get; private init; } = new();

        /// <summary>
        /// Selected receipts used by rewards and progression. Aggregate gold and some unlocks
        /// are tracked separately; this is not the complete AP server history.
        /// </summary>
        public IReadOnlyList<IndexedItemInfo> AllReceivedItems => Items.Received;

        /// <summary>
        /// Consumed receipt indexes for this run, including restored indexes awaiting AP history.
        /// Menu availability also depends on item kind, character, and reward access rules.
        /// </summary>
        public IReadOnlyList<int> UsedItems => Items.UsedIndexes;

        /// <summary>
        /// Checks earned during this run that have not yet been confirmed by the AP server.
        /// In multiplayer this is persisted only in the fixed host's per-player run data.
        /// </summary>
        public HashSet<long> PendingLocationChecks { get; set; } = new();

        /// <summary>
        /// The number of items we've received from the multiworld that we haven't used yet. 
        /// This is what gets displayed in the top bar UI.
        /// </summary>
        public int UnusedItemCount
        {
            get
            {
                var player = GameUtility.CurrentPlayer;
                return player == null
                    ? 0
                    : AllReceivedItems.Count(item => IsAvailableInRewardMenu(item, player));
            }
        }

        /// <summary>
        /// Returns whether a received item should currently appear as a row in the AP reward menu.
        /// The top-bar count and the menu itself must use this same predicate so the badge cannot
        /// advertise rewards that the menu filters out.
        /// </summary>
        public bool IsAvailableInRewardMenu(IndexedItemInfo item, Player player)
        {
            var itemId = item.Item.GetCharacterItemType();
            return item.Item.GetAPCharacterNumber() == GameUtility.CurrentAPCharacterNumber
                && !Items.IsUsed(item.Index)
                && itemId.CanBePickedUp()
                && (
                    itemId != APItem.Relic
                    || RelicRewardUtility.IsAvailableInRewardMenu(item, player)
                );
        }

        #endregion

        #region My Gold (From the Multiworld)

        /// <summary>
        /// ALL Gold received from the Multiworld
        /// </summary>
        public Dictionary<long, int> GoldReceived { get; set; } = new Dictionary<long, int>();

        /// <summary>
        /// Multiplayer receipt count used to divide cumulative buff gold without rounding each
        /// item separately. Rebuilt with GoldReceived from AP history, not saved or reset per run.
        /// </summary>
        internal int UniversalBuffsConvertedToGold { get; set; }

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
        /// Counts the Progressive Starter Card and Relic items received for each character.
        /// These are reconstructed from the Archipelago item history when a save is loaded.
        /// </summary>
        public Dictionary<long, int> ProgressiveStarterCards = new Dictionary<long, int>();
        public Dictionary<long, int> ProgressiveStarterRelics = new Dictionary<long, int>();

        /// <summary>
        /// The Orobas-recognized starter models and authoritative applied tiers for the active run.
        /// The IDs must be saved because tier zero removes the models that would otherwise be
        /// rediscovered for modded characters. Keeping the tier separately distinguishes that state
        /// from a player or another mod removing an already-unlocked starter.
        /// </summary>
        public string? ProgressiveStarterCardBaseId { get; set; }
        public string? ProgressiveStarterCardUpgradedId { get; set; }
        public ProgressiveStarterTier ProgressiveStarterCardTier { get; set; } = ProgressiveStarterTier.Unsupported;
        public string? ProgressiveStarterRelicBaseId { get; set; }
        public string? ProgressiveStarterRelicUpgradedId { get; set; }
        public ProgressiveStarterTier ProgressiveStarterRelicTier { get; set; } = ProgressiveStarterTier.Unsupported;

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
            ArchipelagoSettings? settings = ArchipelagoClient.Settings;
            if (settings == null)
            {
                const string message = "Cannot calculate Progressive Ancient access without AP slot settings.";
                LogUtility.Error(message);
                throw new InvalidOperationException(message);
            }
            int count;
            if(!ProgressiveAncients.TryGetValue(offset, out count))
            {
                count = 0;
            }
            if(!settings.NeowSanity)
            {
                count++;
            }
            return count;
        }

        #endregion

        #region StS Save

        public ApRunProgressState ToRunProgressState()
        {
            RefreshCheckedCampfiresFromClient();
            return new ApRunProgressState
            {
                Initialized = true,
                AncientSettingsForRun = AncientSettingsForRun,
                CardRewardsAttempted = CardRewardsAttempted,
                RareCardRewardsAttempted = RareCardRewardsAttempted,
                RelicRewardsAttempted = RelicRewardsAttempted,
                BankedRelicRewards = BankedRelicRewards,
                RelicRewardsAvailableAnytimeForRun = RelicRewardsAvailableAnytimeForRun,
                RelicReceiptIndexesByCharacter = GetRelicReceiptIndexSnapshot(),
                GoldRewardsAttempted = GoldRewardsAttempted,
                PotionRewardsAttempted = PotionRewardsAttempted,
                BossRewardsDistributed = BossRewardsDistributed,
                MultiplayerBossCompensatedActs = new HashSet<int>(
                    MultiplayerBossCompensatedActs
                ),
                UsedItems = new List<int>(UsedItems),
                GoldRedeemed = GoldRedeemed,
                RelicChoiceAssignments = RelicChoiceAssignments.Select(kv =>
                    new KeyValuePair<int, List<string>>(
                        kv.Key,
                        kv.Value.Select(relic => SerializeAssignment(
                            (relic.IsMutable ? relic : relic.ToMutable()).ToSerializable()
                        )).ToList()
                    )
                ).ToDictionary(),
                AncientRelicChoiceAssignments = AncientRelicChoiceAssignments.Select(kv =>
                    new KeyValuePair<int, List<string>>(
                        kv.Key,
                        kv.Value.Select(relic => SerializeAssignment(
                            (relic.IsMutable ? relic : relic.ToMutable()).ToSerializable()
                        )).ToList()
                    )
                ).ToDictionary(),
                ProgressiveAncients = new Dictionary<long, int>(ProgressiveAncients),
                ProgressiveRests = new Dictionary<long, int>(ProgressiveRests),
                ProgressiveSmiths = new Dictionary<long, int>(ProgressiveSmiths),
                CheckedCampfireLocationIds = new HashSet<long>(CheckedCampfireLocationIds),
                ProgressiveStarterCardBaseId = ProgressiveStarterCardBaseId,
                ProgressiveStarterCardUpgradedId = ProgressiveStarterCardUpgradedId,
                ProgressiveStarterCardTier = ProgressiveStarterCardTier,
                ProgressiveStarterRelicBaseId = ProgressiveStarterRelicBaseId,
                ProgressiveStarterRelicUpgradedId = ProgressiveStarterRelicUpgradedId,
                ProgressiveStarterRelicTier = ProgressiveStarterRelicTier,
                CardAssignments = CardAssignments.ToDictionary(
                    kv => kv.Key,
                    kv => new ApCardAssignmentState
                    {
                        SerializedCards = kv.Value.Cards
                            .Select(card => SerializeAssignment(card.ToSerializable()))
                            .ToList(),
                        CanReroll = kv.Value.CanReroll,
                        IsRare = kv.Value is ApMirroredRewardDispatcher.ApNativeCardReward native
                            && native.IsRare,
                        RewardActIndex = kv.Value is ApMirroredRewardDispatcher.ApNativeCardReward assigned
                            ? assigned.RewardActIndex
                            : null,
                        HasBeenRevealed = kv.Value is ApMirroredRewardDispatcher.ApNativeCardReward revealed
                            && revealed.HasBeenRevealed,
                        MaterializationStrategyId = kv.Value is ApMirroredRewardDispatcher.ApNativeCardReward materialized
                            ? materialized.MaterializationStrategyId
                            : string.Empty,
                        AppliedEffects = kv.Value is ApMirroredRewardDispatcher.ApNativeCardReward effected
                            ? effected.AppliedEffects.Select(effect => new ApRewardEffectSpec
                            {
                                EffectId = effect.EffectId,
                                BeforeValue = effect.BeforeValue,
                                AfterValue = effect.AfterValue,
                            }).ToList()
                            : new List<ApRewardEffectSpec>(),
                    }
                ),
                PotionAssignments = PotionAssignments.ToDictionary(
                    kv => kv.Key,
                    kv => SerializeAssignment(
                        (kv.Value.IsMutable ? kv.Value : kv.Value.ToMutable()).ToSerializable(-1)
                    )
                ),
                PendingLocationChecks = new HashSet<long>(PendingLocationChecks),
                Ascensions = Ascensions.CurrentAscension
                    .Select(level => (int)level)
                    .Distinct()
                    .OrderBy(level => level)
                    .ToList(),
            };
        }

        public Dictionary<long, List<int>> GetRelicReceiptIndexSnapshot() =>
            AllReceivedItems
                .Where(receipt =>
                    ArchipelagoIdCodec.IsCharacterItemId(receipt.Item.ItemId)
                    && receipt.Item.GetCharacterItemType() == APItem.Relic)
                .GroupBy(receipt => receipt.Item.GetAPCharacterNumber())
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(receipt => receipt.Index).Distinct().Order().ToList()
                );

        public SerializableAP ToSerializable(SerializableRun run)
        {
            using var runJson = JsonDocument.Parse(JsonSerializationUtility.ToJson(run));
            return new SerializableAP
            {
                PlayerNumber = CoopSlot.PlayerNumber,
                Progress = ToRunProgressState(),
                SaveData = runJson.RootElement.Clone(),
            };
        }

        public static ArchipelagoProgress FromSerializable(SerializableAP saveData, Player player)
        {
            if (saveData.PlayerNumber != CoopSlot.PlayerNumber)
                throw new InvalidDataException($"This save belongs to Player {saveData.PlayerNumber}, not Player {CoopSlot.PlayerNumber}.");
            return FromRunProgressState(saveData.Progress, player);
        }

        public static ArchipelagoProgress FromRunProgressState(
            ApRunProgressState saveData,
            Player player)
        {
            LogUtility.Info($"Card Assignments {string.Join(",", saveData.CardAssignments)}");
            var progress = new ArchipelagoProgress()
            {
                AncientSettingsForRun = saveData.AncientSettingsForRun,
                CardRewardsAttempted = saveData.CardRewardsAttempted,
                RareCardRewardsAttempted = saveData.RareCardRewardsAttempted,
                RelicRewardsAttempted = saveData.RelicRewardsAttempted,
                BankedRelicRewards = saveData.BankedRelicRewards,
                RelicRewardsAvailableAnytimeForRun = saveData.RelicRewardsAvailableAnytimeForRun,
                GoldRewardsAttempted = saveData.GoldRewardsAttempted,
                PotionRewardsAttempted = saveData.PotionRewardsAttempted,
                BossRewardsDistributed = saveData.BossRewardsDistributed,
                MultiplayerBossCompensatedActs = new HashSet<int>(
                    saveData.MultiplayerBossCompensatedActs
                ),
                Items = ApReceivedItemLedger.FromUsedIndexes(saveData.UsedItems),
                GoldRedeemed = saveData.GoldRedeemed,
                RelicChoiceAssignments = saveData.RelicChoiceAssignments.Select(kv =>
                    new KeyValuePair<int, List<RelicModel>>(
                        kv.Key,
                        kv.Value.Select(json => RelicModel.FromSerializable(
                            DeserializeAssignment<SerializableRelic>(json)
                        )).ToList()
                    )
                ).ToDictionary(),
                AncientRelicChoiceAssignments = saveData.AncientRelicChoiceAssignments.Select(kv =>
                    new KeyValuePair<int, List<RelicModel>>(
                        kv.Key,
                        kv.Value.Select(json => RelicModel.FromSerializable(
                            DeserializeAssignment<SerializableRelic>(json)
                        )).ToList()
                    )
                ).ToDictionary(),
                ProgressiveAncients = new Dictionary<long, int>(saveData.ProgressiveAncients),
                ProgressiveRests = new Dictionary<long, int>(saveData.ProgressiveRests),
                ProgressiveSmiths = new Dictionary<long, int>(saveData.ProgressiveSmiths),
                CheckedCampfireLocationIds = new HashSet<long>(
                    saveData.CheckedCampfireLocationIds
                ),
                ProgressiveStarterCardBaseId = saveData.ProgressiveStarterCardBaseId,
                ProgressiveStarterCardUpgradedId = saveData.ProgressiveStarterCardUpgradedId,
                ProgressiveStarterCardTier = saveData.ProgressiveStarterCardTier,
                ProgressiveStarterRelicBaseId = saveData.ProgressiveStarterRelicBaseId,
                ProgressiveStarterRelicUpgradedId = saveData.ProgressiveStarterRelicUpgradedId,
                ProgressiveStarterRelicTier = saveData.ProgressiveStarterRelicTier,
                PotionAssignments = saveData.PotionAssignments.ToDictionary(
                    kv => kv.Key,
                    kv => PotionModel.FromSerializable(
                        DeserializeAssignment<SerializablePotion>(kv.Value)
                    )
                ),
                PendingLocationChecks = new HashSet<long>(saveData.PendingLocationChecks ?? new HashSet<long>()),
            };

            var cardRewards = new Dictionary<int, CardReward>();
            foreach(var kv in saveData.CardAssignments)
            {
                var cards = kv.Value.SerializedCards.Select(json =>
                    player.RunState.LoadCard(
                        DeserializeAssignment<SerializableCard>(json),
                        player
                    )
                ).ToList();
                cardRewards[kv.Key] = ApMirroredRewardDispatcher.RestorePersistedCardAssignment(
                    kv.Key,
                    kv.Value,
                    player,
                    cards
                );
            }

            var ascensionLevels = saveData.Ascensions?.Select((level) => (AscensionLevel)level).ToHashSet() ?? new HashSet<AscensionLevel>();

            CharacterConfig currentConfig = GameUtility.CurrentConfig
                ?? throw new InvalidDataException(
                    "Cannot restore AP progress without a current character configuration"
                );
            progress.Ascensions.Initialize(currentConfig, ascensionLevels);

            progress.CardAssignments = cardRewards;

            return progress;
        }

        private static string SerializeAssignment<T>(T value) =>
            JsonSerializer.Serialize(value, SerializationUtility.CombinedOptions);

        private static T DeserializeAssignment<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, SerializationUtility.CombinedOptions)
            ?? throw new InvalidDataException($"Could not restore AP assignment {typeof(T).Name}.");

        #endregion
    }
}
