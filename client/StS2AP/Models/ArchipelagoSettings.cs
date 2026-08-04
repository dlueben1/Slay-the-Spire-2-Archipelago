using MegaCrit.Sts2.Core.Commands;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Models
{
    public enum AncientChaosMode
    {
        Balanced = 0,
        ActOrdered = 1,
        FullPool = 2,
    }

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
        // TODO: update to be a set
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
        /// Official Name -> CharacterConfig
        /// </summary>
        public IDictionary<string, CharacterConfig> Characters { get; set;} = new ConcurrentDictionary<string, CharacterConfig>(StringComparer.InvariantCultureIgnoreCase);

        /// <summary>
        /// The collection of characters that are unrecognized by the mod.  Needed so we can send out unlocks.
        /// </summary>
        public IDictionary<string, CharacterConfig> UnrecognizedCharacters { get; set; } = new ConcurrentDictionary<string, CharacterConfig>(StringComparer.InvariantCultureIgnoreCase);

        public bool NeowSanity { get; set; }

        /// <summary>
        /// Controls whether Ancient Unlocks use the normal start-of-act rewards, ordered Act 2/3
        /// AP reward pools, or the combined Act 2/3 AP reward pool.
        /// </summary>
        public AncientChaosMode AncientChaos { get; set; }

        public bool CampfireSanity { get; set; }
        public bool GoldSanity { get; set; }
        public bool PotionSanity { get; set; }
        public bool Floorsanity { get; set; }

        #region Shop Sanity Settings

        public bool ShopSanity { get; set; }
        public int ShopCardSlots { get; set; }
        public int ShopNeutralSlots { get; set; }
        public int ShopRelicSlots { get; set; }
        public int ShopPotionSlots { get; set; }
        public bool ShopRemoveSlots { get; set; }
        public int ShopSanityCosts { get; set; }

        #endregion

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
