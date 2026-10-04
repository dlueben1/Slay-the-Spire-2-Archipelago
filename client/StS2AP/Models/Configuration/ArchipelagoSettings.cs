using System.Collections.Concurrent;

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
        public int PlayerCount { get; set; } = 1;
        public int PlayerNumber { get; set; } = 1;
        private static readonly StringComparer CharacterNameComparer =
            StringComparer.InvariantCultureIgnoreCase;

        private IDictionary<string, CharacterConfig> _characters =
            CreateCharacterMap();

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
        public IDictionary<string, CharacterConfig> Characters
        {
            get => _characters;
            set => _characters = CreateCharacterMap(value);
        }

        public bool NeowSanity { get; set; }

        /// <summary>
        /// Controls whether Progressive Ancient choices appear at the start of their act or
        /// immediately in the Archipelago reward menu.
        /// </summary>
        public AncientRelicLocation AncientRelicLocation { get; set; } = AncientRelicLocation.StartOfAct;

        /// <summary>
        /// Controls whether Ancient choices use the rolled Ancient, the appropriate act's
        /// Ancient pool, or the combined Act 2 and Act 3 Ancient pool. Neow's reward always
        /// remains in Neow's Act 1 pool.
        /// </summary>
        public AncientRelicPoolMode AncientRelicPool { get; set; } = AncientRelicPoolMode.Balanced;

        /// <summary>
        /// Number of Relic receipts that do not need an earned Elite, chest, or Black Star reward.
        /// </summary>
        public int RelicRewardsAvailableAnytime { get; set; } = 10;

        /// <summary>Whether a victory releases the winning character's remaining checks.</summary>
        public bool ReleaseOnVictory { get; set; } = true;

        private Dictionary<string, List<BonusItemDefinition>> _bonusItemsByCategory =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Ordered bonus item definitions from the YAML. Order within a category is significant:
        /// the Nth received bonus item of a category unlocks the Nth definition.
        /// </summary>
        public IReadOnlyList<BonusItemDefinition> BonusItems
        {
            get;
            set
            {
                field = value ?? Array.Empty<BonusItemDefinition>();
                _bonusItemsByCategory = field
                    .GroupBy(definition => definition.Category, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            }
        } = Array.Empty<BonusItemDefinition>();

        /// <summary>Returns the ordered definitions configured for one bonus category.</summary>
        public IReadOnlyList<BonusItemDefinition> BonusItemsFor(string category) =>
            _bonusItemsByCategory.TryGetValue(category, out List<BonusItemDefinition>? definitions)
                ? definitions
                : Array.Empty<BonusItemDefinition>();

        public bool CampfireSanity { get; set; }
        public bool GoldSanity { get; set; }
        public bool PotionSanity { get; set; }
        public bool Floorsanity { get; set; }
        public bool ProgressiveStarterCard { get; set; }
        public bool ProgressiveStarterRelic { get; set; }

        #region Shop Sanity Settings

        public bool ShopSanity { get; set; }
        public int ShopCardSlots { get; set; }
        public int ShopNeutralSlots { get; set; }
        public int ShopRelicSlots { get; set; }
        public int ShopPotionSlots { get; set; }
        public bool ShopRemoveSlots { get; set; }
        public int ShopSanityCosts { get; set; }

        /// <summary>
        /// Total number of generic Shop Slot locations generated per character. Enabling card
        /// removal adds three locations, matching its three progressive act unlock items.
        /// </summary>
        public int TotalShopLocations => ShopCardSlots
            + ShopNeutralSlots
            + ShopRelicSlots
            + ShopPotionSlots
            + (ShopRemoveSlots ? ArchipelagoProgress._maxShopRemoves : 0);

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

        public Version APWorldVersion { get; set; } = new(0, 0, 0);

        #endregion

        private static IDictionary<string, CharacterConfig> CreateCharacterMap(
            IEnumerable<KeyValuePair<string, CharacterConfig>>? entries = null)
        {
            return entries == null
                ? new ConcurrentDictionary<string, CharacterConfig>(CharacterNameComparer)
                : new ConcurrentDictionary<string, CharacterConfig>(
                    entries,
                    CharacterNameComparer
                );
        }
    }
}
