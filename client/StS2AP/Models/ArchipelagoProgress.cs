using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        #endregion
    }
}
