using MegaCrit.Sts2.Core.Commands;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StS2AP.Models
{
    
    // Where you can receive Ancient Relics
    public enum AncientRelicLocation
    {
        StartOfAct = 0,
        Anytime = 1,
    }

    // Balanced = Relics from a specific act 2 ancient followed by Relics from a specific act 3 ancient
    // Chaos = Any act 2 ancient relic followed by any act 3 ancient relic
    // TrueChaos = Any act 2 or act 3 ancient relic always.
    public enum AncientRelicPoolMode
    {
        Balanced = 0,
        Chaos = 1,
        TrueChaos = 2,
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
        /// Controls whether Progressive Ancient choices appear at the start of their act or
        /// immediately in the Archipelago reward menu.
        /// </summary>
        public AncientRelicLocation AncientRelicLocation { get; set; } = AncientRelicLocation.Anytime;

        /// <summary>
        /// Controls whether Ancient choices use the rolled Ancient, the appropriate act's
        /// Ancient pool, or the combined Act 2 and Act 3 Ancient pool.
        /// </summary>
        public AncientRelicPoolMode AncientRelicPool { get; set; } = AncientRelicPoolMode.Balanced;

        /// <summary>
        /// Number of relics offered when claiming a Relic received from Archipelago.
        /// This does not affect relic rewards created by the base game or other mods.
        /// </summary>
        public int RelicChoiceCount { get; set; } = 1;

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
