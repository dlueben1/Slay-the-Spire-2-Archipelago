import typing
from copy import deepcopy
from dataclasses import dataclass
from typing import List

from Options import OptionSet, Range, Toggle, Visibility, Choice, TextChoice, OptionDict, OptionCounter, \
    PerGameCommonOptions, OptionGroup, DeathLink as ArchipelagoDeathLink

import schema
from schema import Schema, Optional, And

from .characters import character_list
from .constants import NUM_CUSTOM, ASCENSIONS


class PlayerCount(Range):
    """Number of co-op players sharing this AP slot, each with separate items, checks and goals.
    The final character roster must contain at least this many characters.
    Random locks give distinct seeded starts. Fixed locks give Player 1 the configured start
    and the other players distinct seeded starts. Unlocked makes every character available to everyone.
    Each client must select its own player number before connecting."""
    display_name = "Player Count"
    range_start = 1
    range_end = 4
    default = 1


class Characters(OptionSet):
    """Enter the list of characters to play as.  Valid characters are:
        'Ironclad'
        'Silent'
        'Defect'
        'Regent'
        'Necrobinder'"""
    display_name = "Characters"
    valid_keys = character_list
    default = ["Ironclad"]
    valid_keys_casefold = False

class ModdedCharacters(OptionSet):
    """Modded characters to include when Advanced Characters is disabled. Enter each character's
    internal ID. At most 5 modded characters can be present in the generated character set.

    If using a modded character:
    Enter the internal ID of the character to use.

    If you don't know the exact ID to enter with the mod installed go to
    `Archipelago Settings -> Archipelago` to view a list of installed modded character IDs.

    Every configured character must be installed with the matching internal ID. The client
    rejects the AP connection if a configured character cannot be loaded.
    """
    display_name = "Modded Characters"
    default = []

class GoalNumChar(Range):
    """How many characters you need to complete a run with before you goal. 0 means all characters"""
    display_name = "Number of Characters to Goal"
    range_start = 0
    range_end = 5 + NUM_CUSTOM
    default = 0

class PickNumberCharacters(Range):
    """Randomly select from the configured characters this many characters to generate for.
    0 disables.
    For example, if "character" is configured to be:
        characters:
            - Ironclad
            - Silent
            - Defect
    and pick_num_characters is 2, one possible generated set is Ironclad and Defect.
    """
    display_name = "Pick Number of Characters"
    range_start = 0
    range_end = 5 + NUM_CUSTOM
    default = 0

class LockCharacters(Choice):
    """Whether in a multi character run "Unlock [Char]" items should be shuffled in.
    locked_fixed means the unlocked_character option is used to determine which character to start with
    locked_random means which character you start with is randomized
    unlocked means you start with all characters available"""
    display_name = "Lock Characters"
    option_unlocked = 0
    option_locked_random = 1
    option_locked_fixed = 2
    default = 1

class UnlockedCharacter(TextChoice):
    """Which character to start unlocked, if lock_characters is set to locked_fixed.
    Can also enter a character name for modded characters."""
    default = 0
    option_ironclad = 0
    option_silent = 1
    option_defect = 2
    option_regent = 3
    option_necrobinder = 4

class Ascension(OptionSet):
    """Logic assumes Ascension 1 is enabled for relic checks. A single number N enables Ascensions 1 through N.
       When multiple values are supplied, each number enables only that numbered Ascension. You can also provide
       names which are shown below. Names are case-sensitive.

        The ascension names are as follows:
        - 'SwarmingElites'
        - 'WearyTraveler'
        - 'Poverty'
        - 'TightBelt'
        - 'AscendersBane'
        - 'Inflation'
        - 'Scarcity'
        - 'ToughEnemies'
        - 'DeadlyEnemies'
        - 'DoubleBoss'
    """
    def __init__(self, value: typing.Iterable[str], random_str: str | None = None):
        self.value = { str(x) for x in value }
        self.random_str = random_str
        super(OptionSet, self).__init__()

    display_name = "Ascension"
    valid_keys_casefold = False
    valid_keys = { *[str(i) for i in range(1,11)], *ASCENSIONS.keys() }
    default = list(ASCENSIONS.keys())[:1]

# class FinalAct(Toggle):
#     """Whether you will need to collect the 3 keys and beat the final act to complete the game."""
#     display_name = "Final Act"
#     default = 0

class NeowSanity(Toggle):
    """Adds Neow's start-of-run reward as a location and Progressive Ancient reward.

    With Anytime, Neow's relic choices appear in the Archipelago reward menu."""
    display_name = "Neow Sanity"
    default = 0


class AncientRelicLocation(Choice):
    """Controls when Progressive Ancient relic choices are offered.

    Start Of Act presents them through the normal Ancient encounter. Anytime presents
    them as linked choices in the Archipelago reward menu as soon as they are received."""
    display_name = "Ancient Relic Location"
    option_start_of_act = 0
    option_anytime = 1
    default = 1


class AncientRelicPool(Choice):
    """Controls which Ancient relics can appear in each three-choice reward.

    Balanced uses the natural Ancient rolled for that act. Chaos can use relics from
    any Ancient in the appropriate act. True Chaos combines the Act 2 and Act 3 pools for the
    Act 2 and Act 3 Progressive Ancient rewards. Neow's reward always uses Neow's Act 1 pool."""
    display_name = "Ancient Relic Pool"
    option_balanced = 0
    option_chaos = 1
    option_true_chaos = 2
    default = 0


class RelicRewardsAvailableAnytime(Range):
    """How many Relic items can be claimed before earning relic rewards in the run.

    The client snapshots this value at run start. Later Relic items need a reward from an
    Elite, treasure chest, or Black Star before they appear in the AP reward menu. The client's
    local AP relic availability can be overridden in client settings for new runs only."""
    display_name = "Relic Rewards Available Anytime"
    range_start = 0
    range_end = 10
    default = 2


class ReleaseOnVictory(Toggle):
    """Release the winning character's remaining checks when their goal is recorded."""
    display_name = "Release Checks On Victory"
    default = 1


class ProgressiveStarterCard(Toggle):
    """Globally enables progressive special starter cards for every configured character.

    Requires Include Floor Checks. Each character gets two Progressive Starter Card items, which
    replace two floor-check filler items. With none received, the character
    starts without the special starter card that Archaic Tooth would transform (Bash, Neutralize, a compatible modded equivalent etc).
    The first item restores the normal card.

    Characters without an Archaic Tooth transformation are left unchanged, although their two
    Progressive Starter Card items are still present in the multiworld.

    WARNING: This can make the early game significantly harder for some characters. Logic expects you to reach
    Late Act 1 (may need to beat 1 Elite) without your starters."""
    display_name = "Progressive Starter Card"
    default = 0


class ProgressiveStarterRelic(Toggle):
    """Globally enables progressive starter relics for every configured character.

    Requires Include Floor Checks. Each character gets two Progressive Starter Relic items, which
    replace two floor-check filler items. With none received, the character
    starts without the starter relic that Touch of Orobas would refine (such as Burning Blood, or a
    compatible modded equivalent).

    Characters without a Touch of Orobas refinement are left unchanged, although their two
    Progressive Starter Relic items are still present in the multiworld.

    WARNING: This can make the early game significantly harder for characters whose starting relic
    is central to their early power. Logic expects you to reach Late Act 1 (may require beating 1 Elite) without
    your starters."""
    display_name = "Progressive Starter Relic"
    default = 0


class IncludeFloorChecks(Toggle):
    """Add locations for reaching new floors and fill the corresponding item-pool space with
    configurable filler. Ascension Down and Progressive Starter options require these locations."""
    display_name = "Include Floor Checks"
    default = 1

class CampfireSanity(Toggle):
    """Whether to shuffle being able to rest and smith at each campsite per act.  Also adds
    new locations at campsites per act."""
    display_name = "Campfire Sanity"
    default = 0

class ShopSanity(Toggle):
    """Move the configured shop slots to a separate AP shop page as purchasable location checks and
    shuffle items that progressively restore the corresponding slots on the normal shop page."""
    display_name = "Shop Sanity"
    option_true = 1
    option_false = 0
    default = 0

class ShopCardSlots(Range):
    """When shop_sanity is enabled, the number of colored card slots to shuffle."""
    display_name = "Shop Card Slots"
    range_start = 0
    range_end = 5
    default = 2

class ShopNeutralSlots(Range):
    """When shop_sanity is enabled, the number of neutral card slots to shuffle."""
    display_name = "Shop Neutral Card Slots"
    range_start = 0
    range_end = 2
    default = 1

class ShopRelicSlots(Range):
    """When shop_sanity is enabled, the number of relic slots to shuffle."""
    display_name = "Shop Relic Slots"
    range_start = 0
    range_end = 3
    default = 2

class ShopPotionSlots(Range):
    """When shop_sanity is enabled, the number of potion slots to shuffle"""
    display_name = "Shop Potion Slots"
    range_start = 0
    range_end = 3
    default = 2

class ShopRemoveSlots(Toggle):
    """When shop_sanity is enabled, whether to shuffle the ability to remove cards at the shop.
    Progressive based on Act; i.e. you'll gain the ability to remove cards per Act, starting from Act 1.
    Act 4 will be treated as Act 3."""
    display_name = "Shop Remove Slots"
    default = 0

class ShopSanityCosts(Choice):
    """Controls the price paid for location checks on the AP shop page. Normal shop-item prices are
    unaffected. Tiered modes first calculate a vanilla-style baseline for the AP card, relic, or potion.

    Fixed: 15 gold per AP check.
    Super Discount Tiered: 20 percent of the calculated baseline.
    Discount Tiered: 50 percent of the calculated baseline.
    Tiered: the full calculated baseline.

    Generation logic does not account for these prices."""
    display_name = "Shop Sanity Costs"
    option_Fixed = 0
    option_Super_Discount_Tiered = 1
    option_Discount_Tiered = 2
    option_Tiered = 3
    default = 2

class GoldSanity(Toggle):
    """Whether to replace combat, elite, and boss gold rewards with AP locations. Adds 22 locations per character."""
    display_name = "Gold Sanity"
    default = 0

class PotionSanity(Toggle):
    """Whether to replace potion rewards with AP locations. Adds 9 locations per character."""
    display_name = "Potion Sanity"
    default = 0

class CardReward(Toggle):
    """Whether every card reward is shuffled.  If false, then every other card reward is shuffled
    """
    display_name = "Shuffle All Card Rewards"
    default = False

class SeededRun(Toggle):
    """Whether each character should have a fixed seed to climb the spire with or not."""
    display_name = "Seeded Run"
    default = 0

class AdvancedChar(Toggle):
    """Whether to use the advanced characters feature. The normal options for character, ascension, etc. are ignored.
    See the "advanced_characters" option.
    """
    visibility = Visibility.template
    display_name = "Advanced Characters"
    option_true = 1
    option_false = 0
    default = 0

class CharacterOptions(OptionDict):
    """The configuration for advanced characters.  Each character's options can be configured
    independently of each other.  No validation is done on the character name, so use carefully.
    Format is:
        <char name>:
            ascension:
                - <string or number>
            ascension_down:
                - <string or number>

    If using a modded character:
    Enter the internal ID of the character to use.

    If you don't know the exact ID to enter with the mod installed go to
    `Archipelago Settings -> Archipelago` to view a list of installed modded character IDs.

    Every configured character must be installed with the matching internal ID. The client
    rejects the AP connection if a configured character cannot be loaded.
    """
    # For those wondering why on earth there's an advanced character option
    # it's to support modded characters.
    visibility = Visibility.template
    default = {
        "ironclad": {
            "ascension": [1],
            # "final_act": 1,
            "ascension_down": [],
        }
    }
    schema = Schema({
        str: {
            Optional("ascension", default=[1]): [And(int,lambda n: 1 <= n <= 10), str],
            # Optional("final_act", default=0): And(int, lambda n: 0 <= n <= 1),
            Optional("ascension_down", default=[]): [And(int,lambda n: 1 <= n <= 10), str],
        }
    })

class AscensionDown(OptionSet):
    """Ascension Down items to add for each character.
    Only enabled Ascensions can receive a corresponding Ascension Down.

    A single number N selects the highest N enabled Ascensions. When multiple values are supplied, each
    number selects only that numbered Ascension. Names are case-sensitive.
    Supports both numbers and names.

    - 'SwarmingElites'
    - 'WearyTraveler'
    - 'Poverty'
    - 'TightBelt'
    - 'AscendersBane'
    - 'Inflation'
    - 'Scarcity'
    - 'ToughEnemies'
    - 'DeadlyEnemies'
    - 'DoubleBoss'

    Logic does not account for receiving these items."""
    def __init__(self, value: typing.Iterable[str], random_str: str | None = None):
        self.value = { str(x) for x in value }
        self.random_str = random_str
        super(OptionSet, self).__init__()

    display_name = "Ascension Down"
    valid_keys_casefold = False
    valid_keys = { *[str(i) for i in range(1,11)], *ASCENSIONS.keys() }
    default = list()

# Death Link Options

class DeathLink(ArchipelagoDeathLink):
    """Share deaths with other Death Link players. Dying sends a Death Link. Receiving one while in
    a run damages the current character by Death Link Damage Percent of maximum health and can add a
    Death Fragment if the character survives. Local client settings can override these slot settings."""


class EnableDeathFragments(Toggle):
    """When Death Link is enabled, add a permanent Death Fragment curse after receiving a Death Link
    while in a run, provided the character survives the configured damage. If received during combat,
    another copy is also added to that combat's draw pile."""
    display_name = "Enable Death Fragments"
    default = 1

class DeathLinkDamagePercent(Range):
    """If Death Link is enabled, this setting determines how much damage you take when you receive a Death Link, 
    as a percentage of your max health. 
    If you do not want to take any damage, set this to 0. 
    If you want to be killed whenever you receive a Death Link, set this to 100."""
    display_name = "Death Link Damage Percent"
    range_start = 0
    range_end = 100
    default = 0

# class TrapChance(Range):
#     """Chance that a filler item is replaced with a trap.  Requires `include_floor_checks`
#     for any traps to be added.
#     """
#     display_name = "Trap Chance"
#     range_start = 0
#     range_end = 100
#     default = 0

# class TrapWeights(OptionCounter):
#     """
#     The list of traps and corresponding weights that will be added to the item pool.
#     Debuff Trap - Start next combat with a weaker debuff
#     Strong debuff Trap - Start next combat with a strong debuff
#     Killer debuff Trap - Start next combat with a debuff has a good chance of killing you
#     Buff Trap - Next combat, enemies start buffed
#     Strong Buff Trap - Next combat, enemies start with a strong buff
#     Status Card Trap - Start next combat with status cards in your draw pile
#     Gremlin Trap - Next combat, a random gremlin is added to the enemies
#     """
#     display_name = "Trap Weights"
#     min = 0
#     default = {trap: 1 for trap in trap_item_table.keys()}
#     valid_keys = sorted(trap_item_table.keys())

# Filler Item Weight Options

# Factory function to create filler weight Choice classes dynamically
def _create_filler_weight_class(item_name: str, description: str, default_weight: int = 1):
    """Create a Choice class for filler item weights.
    
    Args:
        item_name: The display name of the item (e.g., "One Gold", "Free Attack")
        description: Description of what the item does
    
    Returns:
        A Choice class with standard weight options (none=0, low=1, medium=3, high=5)
    """
    class_name = item_name.replace(" ", "").replace("-", "") + "FillerWeight"
    display_name = f"{item_name} Filler Weight"
    docstring = f"""Weight for {item_name} filler items. {description}"""
    
    return type(
        class_name,
        (Choice,),
        {
            "__module__": __name__,
            "__doc__": docstring,
            "display_name": display_name,
            "option_none": 0,
            "option_low": 1,
            "option_medium": 3,
            "option_high": 5,
            "default": default_weight,
        }
    )

# Character-specific filler items
OneGoldFillerWeight = _create_filler_weight_class(
    "One Gold",
    """Grants one gold to its associated character. If every filler weight is None, the generator
    uses that character's One Gold as the required safe fallback.""",
    default_weight = 0
)

FiveGoldFillerWeight = _create_filler_weight_class(
    "Five Gold",
    """Grants five gold to its associated character.""",
    default_weight = 5
)

# Universal filler items
FreeAttackFillerWeight = _create_filler_weight_class(
    "Free Attack",
    "At the start of the next player combat turn, makes the next Attack played cost zero energy.",
    default_weight = 5
)

FreePowerFillerWeight = _create_filler_weight_class(
    "Free Power",
    "At the start of the next player combat turn, makes the next Power played cost zero energy.",
    default_weight = 5
)

FreeSkillFillerWeight = _create_filler_weight_class(
    "Free Skill",
    "At the start of the next player combat turn, makes the next Skill played cost zero energy.",
    default_weight = 5
)

VigorFillerWeight = _create_filler_weight_class(
    "Vigor",
    "Applies Vigor at the start of the next player combat turn.",
    default_weight = 5
)

ArtifactFillerWeight = _create_filler_weight_class(
    "Artifact",
    "Applies Artifact at the start of the next player combat turn.",
    default_weight = 5
)

ThornsFillerWeight = _create_filler_weight_class(
    "Thorns",
    "Applies Thorns at the start of the next player combat turn.",
    default_weight = 5
)

DexterityFillerWeight = _create_filler_weight_class(
    "Dexterity",
    "Applies Dexterity at the start of the next player combat turn.",
    default_weight = 3
)

StrengthFillerWeight = _create_filler_weight_class(
    "Strength",
    "Applies Strength at the start of the next player combat turn.",
    default_weight = 3
)

PlatingFillerWeight = _create_filler_weight_class(
    "Plating",
    "Applies Plating at the start of the next player combat turn.",
    default_weight = 3
)

BufferFillerWeight = _create_filler_weight_class(
    "Buffer",
    "Applies Buffer at the start of the next player combat turn.",
    default_weight = 1
)

FriendshipFillerWeight = _create_filler_weight_class(
    "Friendship",
    "Applies Friendship at the start of the next player combat turn, raising maximum energy by one.",
    default_weight = 3
)

PostCombatCardUpgradeFillerWeight = _create_filler_weight_class(
    "Post-Combat Card Upgrade",
    "At the start of the next player combat turn, applies a buff that upgrades a random deck card after combat.",
    default_weight = 1
)

PostCombatCardRemovalFillerWeight = _create_filler_weight_class(
    "Post-Combat Card Removal",
    "At the start of the next player combat turn, applies a buff that lets you remove a deck card after combat.",
    default_weight = 1
)

AdditionalCardRewardFillerWeight = _create_filler_weight_class(
    "Additional Card Reward",
    "At the start of the next player combat turn, applies a buff that adds a card reward after combat.",
    default_weight = 1
)

SingleColorlessCardFillerWeight = _create_filler_weight_class(
    "Single Colorless Card",
    """Grants a one-time card reward containing one random colorless card.""",
    default_weight = 3
)

# Filler Items Option Group
filler_item_options = OptionGroup(
    "Filler Items",
    [
        OneGoldFillerWeight,
        FiveGoldFillerWeight,
        FreeAttackFillerWeight,
        FreePowerFillerWeight,
        FreeSkillFillerWeight,
        DexterityFillerWeight,
        StrengthFillerWeight,
        PlatingFillerWeight,
        FriendshipFillerWeight,
        ThornsFillerWeight,
        ArtifactFillerWeight,
        BufferFillerWeight,
        VigorFillerWeight,
        PostCombatCardUpgradeFillerWeight,
        PostCombatCardRemovalFillerWeight,
        AdditionalCardRewardFillerWeight,
        #SingleColorlessCardFillerWeight,
    ]
)




@dataclass
class Spire2Options(PerGameCommonOptions):
    player_count: PlayerCount
    # Character options
    characters: Characters
    modded_characters: ModdedCharacters
    pick_num_characters: PickNumberCharacters
    num_chars_goal: GoalNumChar
    lock_characters: LockCharacters
    unlocked_character: UnlockedCharacter
    # final_act: FinalAct
    ascension: Ascension
    ascension_down: AscensionDown

    # Main game flow
    ancient_relic_location: AncientRelicLocation
    ancient_relic_pool: AncientRelicPool
    relic_rewards_available_anytime: RelicRewardsAvailableAnytime
    progressive_starter_card: ProgressiveStarterCard
    progressive_starter_relic: ProgressiveStarterRelic
    shuffle_all_cards: CardReward

    # Sanities
    include_floor_checks: IncludeFloorChecks
    neow_sanity: NeowSanity
    campfire_sanity: CampfireSanity
    gold_sanity: GoldSanity
    potion_sanity: PotionSanity
    shop_sanity: ShopSanity
    shop_card_slots: ShopCardSlots
    shop_neutral_card_slots: ShopNeutralSlots
    shop_relic_slots: ShopRelicSlots
    shop_potion_slots: ShopPotionSlots
    shop_remove_slots: ShopRemoveSlots
    shop_sanity_costs: ShopSanityCosts

    # Death Link
    death_link: DeathLink
    enable_death_fragments: EnableDeathFragments
    death_link_damage_percent: DeathLinkDamagePercent

    # Advanced options
    release_on_victory: ReleaseOnVictory
    seeded: SeededRun
    use_advanced_characters: AdvancedChar
    advanced_characters: CharacterOptions

    # Filler item weights
    one_gold_filler_weight: OneGoldFillerWeight
    five_gold_filler_weight: FiveGoldFillerWeight
    free_attack_filler_weight: FreeAttackFillerWeight
    free_power_filler_weight: FreePowerFillerWeight
    free_skill_filler_weight: FreeSkillFillerWeight
    vigor_filler_weight: VigorFillerWeight
    artifact_filler_weight: ArtifactFillerWeight
    thorns_filler_weight: ThornsFillerWeight
    buffer_filler_weight: BufferFillerWeight
    dexterity_filler_weight: DexterityFillerWeight
    strength_filler_weight: StrengthFillerWeight
    plating_filler_weight: PlatingFillerWeight
    friendship_filler_weight: FriendshipFillerWeight
    post_combat_card_upgrade_filler_weight: PostCombatCardUpgradeFillerWeight
    post_combat_card_removal_filler_weight: PostCombatCardRemovalFillerWeight
    additional_card_reward_filler_weight: AdditionalCardRewardFillerWeight
    #single_colorless_card_filler_weight: SingleColorlessCardFillerWeight
    # trap_chance: TrapChance
    # trap_weights: TrapWeights
