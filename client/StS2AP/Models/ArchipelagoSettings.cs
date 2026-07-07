using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Models
{
    /// <summary>
    /// The settings that a player has configured for their Archipelago Slot.
    /// 
    /// This is intended to be read-only once it's been initialized during the Archipelago connection,
    /// representing what the server-side settings are for this slot, which are configured at generation
    /// time for an Archipelago session.
    /// 
    /// For local/configurable settings, see  <seealso cref="ClientSettings"/>.
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
        /// Whether this slot is participating in Death Link.
        /// </summary>
        public bool IsDeathLinkEnabled { get; set; }

        /// <summary>
        /// If this is enabled, then when a Death Link is received, a Curse card will be added to the player's deck.
        /// </summary>
        public bool EnableDeathFragments { get; set; }

        /// <summary>
        /// The percentage of max health that should be lost when a Death Link is received.
        /// Only applies if the Death Link Type is set to Damage.
        /// 
        /// Normally something like this would be a float, but based on how the YAMLs work,
        /// I think it's easier if the user types in a percentage.
        /// 
        /// This value should be between 1 and 100, inclusive.
        /// </summary>
        public int DeathLinkDamagePercent { get; set; }

        #endregion
    }
}
