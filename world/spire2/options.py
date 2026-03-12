from dataclasses import dataclass

from Options import OptionSet, Range, Toggle, Visibility, Choice, TextChoice, OptionDict, OptionCounter, \
    PerGameCommonOptions

from schema import Schema, Optional, And

from .characters import character_list
from .constants import NUM_CUSTOM


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
        character:
            - Ironclad
            - Silent
            - Defect
    And pick_num_characters is set to 2, then one possible outcome is
    to have a run with Ironclad and Defect, but not the Silent.
    """
    display_name = "Pick Number of Characters"
    range_start = 0
    range_end = 5 + NUM_CUSTOM - 1
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
    default = ""
    option_ironclad = 0
    option_silent = 1
    option_defect = 2
    option_regent = 3
    option_necrobinder = 4

class Ascension(Range):
    """What Ascension do you wish to play with. Note that logic is written assuming ascension 1"""
    display_name = "Ascension"
    range_start = 0
    range_end = 10
    default = 1

# class FinalAct(Toggle):
#     """Whether you will need to collect the 3 keys and beat the final act to complete the game."""
#     display_name = "Final Act"
#     default = 0


# class IncludeFloorChecks(Toggle):
#     """Whether to include reaching new floors as a location.  Adds small amounts of gold as items."""
#     display_name = "Include Floor Checks"
#     default = 1

# class CampfireSanity(Toggle):
#     """Whether to shuffle being able to rest and smith at each campsite per act.  Also adds
#     new locations at campsites per act."""
#     display_name = "Campfire Sanity"
#     default = 0
#
# class ShopSanity(Toggle):
#     """Whether to shuffle shop slots into the pool.  Also adds new locations at the shop per slot shuffled."""
#     display_name = "Shop Sanity"
#     option_true = 1
#     option_false = 0
#     default = 0

# class ShopCardSlots(Range):
#     """When shop_sanity is enabled, the number of colored card slots to shuffle."""
#     display_name = "Shop Card Slots"
#     range_start = 0
#     range_end = 5
#     default = 2
#
# class ShopNeutralSlots(Range):
#     """When shop_sanity is enabled, the number of neutral card slots to shuffle."""
#     display_name = "Shop Neutral Card Slots"
#     range_start = 0
#     range_end = 2
#     default = 1
#
# class ShopRelicSlots(Range):
#     """WHen shop_sanity is enabled, the number of relic slots to shuffle."""
#     display_name = "Shop Relic Slots"
#     range_start = 0
#     range_end = 3
#     default = 2
#
# class ShopPotionSlots(Range):
#     """When shop_sanity is enabled, the number of potion slots to shuffle"""
#     display_name = "Shop Potion Slots"
#     range_start = 0
#     range_end = 3
#     default = 2
#
# class ShopRemoveSlots(Toggle):
#     """When shop_sanity is enabled, whether to shuffle the ability to remove cards at the shop.
#     Progressive based on Act; i.e. you'll gain the ability to remove cards per Act, starting from Act 1.
#     Act 4 will be treated as Act 3."""
#     display_name = "Shop Remove Slots"
#     default = 0
#
# class ShopSanityCosts(Choice):
#     """How expensive the AP shop items should be. Tiered means costs map to typical costs rarity for the slot.
#     Progression = Rare, Useful = Uncommon, Filler = Common
#     Logic does not take this option into account.
#     Fixed=15 gold each
#     Super_Discount_Tiered=20% of tiered costs
#     Discount_Tiered=50% of tiered costs
#     Tiered=Vanilla price for slot
#     """
#     display_name = "Shop Sanity Costs"
#     option_Fixed = 0
#     option_Super_Discount_Tiered = 1
#     option_Discount_Tiered = 2
#     option_Tiered = 3
#     default = 2

# class GoldSanity(Toggle):
#     """Whether to enable shuffling gold rewards into the multiworld. Adds 27 locations per character"""
#     display_name = "Gold Sanity"
#     default = 0
#
# class PotionSanity(Toggle):
#     """Whether to enable shuffling potion drops into the multiworld; adds 9 locations per character."""
#     display_name = "Potion Sanity"
#     default = 0

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
    visibility = Visibility.none
    display_name = "Advanced Characters"
    option_true = 1
    option_false = 0
    default = 0

class CharacterOptions(OptionDict):
    """The configuration for advanced characters.  Each character's options can be configured
    independently of each other.  No validation is done on the character name, so use carefully.
    Format is:
        <char name>:
            ascension: <number>
            ascension_down: <number>

    If using a modded character:
    Enter the internal ID of the character to use.

     if you don't know the exact ID to enter with the mod installed go to
    `Mods -> Archipelago Multi-world -> config` to view a list of installed modded character IDs.

    If the chosen character mod is not installed, checks will be sent when another character
    sends them.  If none of the chosen character mods are installed, you will be playing
    a very boring Ironclad run.
    """
    # For those wondering why on earth there's an advanced character option
    # it's to support modded characters.
    visibility = Visibility.none
    default = {
        "ironclad": {
            "ascension": 1,
            # "final_act": 1,
            "ascension_down": 0,
        }
    }
    schema = Schema({
        str: {
            Optional("ascension", default=1): And(int,lambda n: 0 <= n <= 10),
            # Optional("final_act", default=0): And(int, lambda n: 0 <= n <= 1),
            Optional("ascension_down", default=0): And(int, lambda n: 0 <= n <= 10),
        }
    })

class AscensionDown(Range):
    """The number of ascension downs to add to the item pool, per character. Only valid when
    `use_advanced_characters` is false (see `advanced_characters`), and when `include_floor_checks` is true.
    Will be ignored if invalid.

    Logic does NOT account for this."""
    visibility = Visibility.none
    display_name = "Ascension Down"
    range_start = 0
    range_end = 10
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

# class FillerWeights(OptionCounter):
#     """
#     The list of filler and corresponding weights that will be added to the item pool
#     1 Gold - One gold, character bound
#     5 Gold - Five gold, character bound
#     CAW CAW - CAW CAW
#     Combat Buff - Similar to traps in function, but beneficial instead of detrimental, not character bound
#     """
#     display_name = "Filler Weights"
#     min = 0
#     default = {
#         "1 Gold": 40,
#         "5 Gold": 60,
#         "CAW CAW": 0,
#         "Combat Buff": 0,
#     }
#     valid_keys = [
#         "5 Gold",
#         "1 Gold",
#         "CAW CAW",
#         "Combat Buff",
#     ]




@dataclass
class Spire2Options(PerGameCommonOptions):
    characters: Characters
    pick_num_characters: PickNumberCharacters
    num_chars_goal: GoalNumChar
    lock_characters: LockCharacters
    unlocked_character: UnlockedCharacter
    use_advanced_characters: AdvancedChar
    advanced_characters: CharacterOptions
    # final_act: FinalAct
    ascension: Ascension
    ascension_down: AscensionDown
    shuffle_all_cards: CardReward
    # include_floor_checks: IncludeFloorChecks
    # filler_weights: FillerWeights
    # trap_chance: TrapChance
    # trap_weights: TrapWeights
    # campfire_sanity: CampfireSanity
    # gold_sanity: GoldSanity
    # potion_sanity: PotionSanity
    # shop_sanity: ShopSanity
    # shop_card_slots: ShopCardSlots
    # shop_neutral_card_slots: ShopNeutralSlots
    # shop_relic_slots: ShopRelicSlots
    # shop_potion_slots: ShopPotionSlots
    # shop_remove_slots: ShopRemoveSlots
    # shop_sanity_costs: ShopSanityCosts
    seeded: SeededRun

# option_groups: List[OptionGroup] = [
#     OptionGroup("Sanities", [
#         IncludeFloorChecks,
#         CampfireSanity,
#         GoldSanity,
#         PotionSanity,
#         KeySanity,
#         ShopSanity,
#         ShopCardSlots,
#         ShopNeutralSlots,
#         ShopRelicSlots,
#         ShopPotionSlots,
#         ShopRemoveSlots,
#         ShopSanityCosts,
#     ]),
#     OptionGroup("Traps", [
#         TrapChance,
#         TrapWeights
#     ]),
#     OptionGroup("Misc", [
#         ChattyMC,
#         FillerWeights,
#         SeededRun,
#     ]),
# ]