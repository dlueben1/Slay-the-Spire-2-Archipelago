using Archipelago.MultiClient.Net.Models;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using StS2AP.Extensions;
using StS2AP.Utils;


namespace StS2AP.Models
{
    /// <summary>
    /// Tracks the progress of how far along the player is through their Archipelago game
    /// </summary>
    public class ArchipelagoProgress
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

        public const int _maxBossRewards = 3;

        #region Per-Run Tracker

        /// <summary>
        /// Keeps track of the number of times that the game has tried to provide a Card Reward.
        /// Used to keep track of when to replace a Card Reward with an AP Location.
        /// </summary>
        public int CardRewardsAttempted { get; set; } = 0;

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

        public int BossRewardsDistributed { get; set; } = 0;

        public Dictionary<string, bool> CampfiresChecked { get; set; } = new Dictionary<string, bool>();

        /// <summary>
        /// Maps an Archipelago item's LocationId to the RelicModel that was pre-pulled from the RelicFactory for it.
        /// This ensures that opening/closing the reward screen always shows the same relic for each relic reward.
        /// Cleared on each new run via <see cref="ResetTrackers"/>.
        /// </summary>
        public Dictionary<long, RelicModel> RelicAssignments { get; set; } = new Dictionary<long, RelicModel>();

        /// <summary>
        /// Returns the relic assigned to the given location, pulling one from the RelicFactory if it hasn't been assigned yet.
        /// This guarantees that the same relic is shown every time the reward screen is opened for the same item.
        /// </summary>
        /// <param name="locationId">The Archipelago LocationId that identifies this specific relic reward.</param>
        /// <param name="player">The current player, needed by RelicFactory.</param>
        /// <returns>The assigned RelicModel, or null if no player is provided or the factory fails.</returns>
        public RelicModel? GetOrAssignRelic(long locationId, Player player)
        {
            if (RelicAssignments.TryGetValue(locationId, out var existing))
                return existing;

            if (player == null)
            {
                LogUtility.Warn($"Cannot assign relic for location {locationId}: no active player");
                return null;
            }

            try
            {
                var relic = RelicFactory.PullNextRelicFromFront(player);
                RelicAssignments[locationId] = relic;
                LogUtility.Info($"Pre-assigned relic '{relic.Id}' for location {locationId}");
                return relic;
            }
            catch (Exception ex)
            {
                LogUtility.Error($"Failed to pre-assign relic for location {locationId}: {ex.Message}");
                return null;
            }
        }

        public void InitializeTrackers(Player player)
        {
            ResetTrackers();
            var name = player.APName();
            for(int i = 1; i <= 3; i++)
            {
                for(int j = 1; j <=2; j++)
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
            CampfiresChecked.Clear();
            RelicAssignments.Clear();
        }

        #endregion

        #region My Items

        /// <summary>
        /// All items we've received from the multiworld. Gets dumped into `AvailableItems` at the start of each run.
        /// </summary>
        public List<ItemInfo> AllReceivedItems = new List<ItemInfo>();

        /// <summary>
        /// Any items that have been used up in the current run live here. The difference between this and `AllReceivedItems` 
        /// represents the items still available for use.
        /// </summary>
        public List<long> UsedItems = new List<long>();

        /// <summary>
        /// The number of items we've received from the multiworld that we haven't used yet. 
        /// This is what gets displayed in the top bar UI.
        /// </summary>
        public int UnusedItemCount => AllReceivedItems.Where(item => item.GetStSCharID() == GameUtility.CurrentCharacterID).Count() - UsedItems.Count;

        #endregion
    }
}
