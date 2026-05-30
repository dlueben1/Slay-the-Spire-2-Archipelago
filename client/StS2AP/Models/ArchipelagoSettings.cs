using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Models
{
    /// <summary>
    /// The settings that a player has configured for their Archipelago Slot
    /// </summary>
    public class ArchipelagoSettings
    {
        public int AscensionLevel { get; set; }

        /// <summary>
        /// Whether all cards should be shuffled or not - if not, only every other card will be an AP Item
        /// </summary>
        public bool ShouldShuffleAllCards { get; set; }

        public bool IsSeeded { get; set; }

        public bool NoCharactersLocked { get; set; }

        public int NumCharsGoal { get; set; }
        public int TotalCharacters { get; set; }

        /// <summary>
        /// A collection of characters that are available in the Multiworld (i.e. have checks for this Slot)
        /// 
        /// This is *not* a collection of which characters are unlocked, just which characters *can* be unlocked for this slot.
        /// </summary>
        public string[] AvailableCharacters { get; set; } = Array.Empty<string>();
        public bool CampfireSanity { get; set; }
        public bool GoldSanity { get; set; }
        public bool PotionSanity { get; set; }
        public bool Floorsanity { get; set; }

        #region Death Link Settings

        /// <summary>
        /// Whether this slot is participating in Death Link
        /// </summary>
        public bool IsDeathLinkEnabled { get; set; }

        #endregion
    }
}
