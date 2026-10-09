import typing
from dataclasses import dataclass

from Options import OptionSet, OptionList, Range, Toggle, DefaultOnToggle, Visibility, Choice, TextChoice, OptionDict, \
    PerGameCommonOptions, OptionGroup, DeathLink as ArchipelagoDeathLink

from schema import Schema, Optional, And

from .characters import character_list
from .constants import NUM_CUSTOM, ASCENSIONS


class UseNewLogic(DefaultOnToggle):
    """Use the new logic. Disable to use the previous 1.1.X and below logic.
    New logic allows more random item order—for example, Extra relics and cards can help supplement missing ancients or rests.
    One example is that old logic required your progressive starter card+relic to beat your 1st elite but new logic
    no longer requires this as 'Power' can be made up with extra cards/relics. Similar cases for ancients/smiths/rests.

    Old logic is used automatically (regardless of this option) when floor, gold, and potion checks
     are all disabled to reduce failed generations.
    """
    display_name = "Use New Logic"


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
    """Randomly select from the configured characters this many characters for each player to generate for.
    Each player's selection is rolled independently, and selections may overlap.
    0 gives every player all configured characters.
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
    locked_fixed uses unlocked_character for Player 1 and randomizes the starting character for other players.
    locked_random independently randomizes which character each player starts with.
    unlocked makes every character in each player's roster available."""
    display_name = "Lock Characters"
    option_unlocked = 0
    option_locked_random = 1
    option_locked_fixed = 2
    default = 1

class UnlockedCharacter(TextChoice):
    """Which character Player 1 starts with, if lock_characters is set to locked_fixed.
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
    """Adds Neow's starting Ancient Relic as a Progressive Ancient reward.

    With Anytime mode, Neow's relic choices appear in the Archipelago reward menu.
    """
    display_name = "Neow Sanity"
    default = 0


class AncientRelicLocation(Choice):
    """Controls when Progressive Ancient relic choices are offered.

    Start Of Act presents them through the normal Ancient encounter. Anytime presents
    them as linked choices in the Archipelago reward menu as soon as they are received.
    """
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
    """How many AP Relic items can be claimed via the AP Menu without fighting elites or visiting chests.
    Later AP Relic Items can only be 'claimed' by beating Elites or opening treasure chests.
    i.e. A value of 2 here and receiving 3 relics from AP means you get 2 relics in your AP menu and the 3rd 
    requires beating one elite or visiting one chest. 
    tldr; fight elites and go to chests to get more relics. Lower value = elites matter more so don't skip them
    """
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
    The first item restores the normal card; the second applies its Archaic Tooth transformation.

    Characters without an Archaic Tooth transformation are left unchanged, although their two
    Progressive Starter Card items are still present in the multiworld.

    WARNING: This can make the early game significantly harder for some characters."""
    display_name = "Progressive Starter Card"
    default = 0


class ProgressiveStarterRelic(Toggle):
    """Globally enables progressive starter relics for every configured character.

    Requires Include Floor Checks. Each character gets two Progressive Starter Relic items, which
    replace two floor-check filler items. With none received, the character
    starts without the starter relic that Touch of Orobas would refine (such as Burning Blood, or a
    compatible modded equivalent).

    The first item restores the normal relic; the second applies its Touch of Orobas refinement.

    Characters without a Touch of Orobas refinement are left unchanged, although their two
    Progressive Starter Relic items are still present in the multiworld.

    WARNING: This can make the early game significantly harder for characters whose starting relic
    is central to their early power."""
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



class BonusItems(OptionList):
    """Add a number of Bonus Items to the item pool.
    These items make the game easier to complete, and are not accounted for in logic.

    Each ordered entry creates one character-agnostic bonus item and must provide either
    a non-empty list of value Pools or one specific Value. For example:

        bonus_items:
            - WAX_RELIC:
                Pools: [Common, Uncommon]
            - WAX_RELIC:
                Value: Orichalcum


    Generation fails if more bonus items are configured than there are filler slots.

    It's HIGHLY recommended to use the YAML Builder on our website to easily generate this option, which you can find at https://sts2ap.net
    """
    display_name = "Bonus Items"
    visibility = Visibility.template
    default = []

    @staticmethod
    def _has_exactly_one_selector(entry: dict) -> bool:
        if len(entry) != 1:
            return False

        reward = next(iter(entry.values()))
        has_pools = "Pools" in reward
        has_value = "Value" in reward
        return has_pools != has_value and (
            not has_pools or bool(reward["Pools"])
        )

    schema = Schema([
        And(
            Schema({
                str: {
                    Optional("Pools"): [And(str, len)],
                    Optional("Value"): And(str, len),
                }
            }),
            _has_exactly_one_selector,
        )
    ])

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

class TrapChance(Range):
    """Chance for filler item to be replaced by a trap."""
    display_name = "Trap Chance"
    range_start = 0
    range_end = 100
    default = 0


class TrapWeight(Choice):
    option_none = 0
    option_low = 1
    option_medium = 3
    option_high = 5
    default = option_low


class WeakTrapWeight(TrapWeight):
    """Applies 1 Weak, reducing damage dealt by the player for 1 turn."""
    display_name = "Weak Trap Weight"
    default = TrapWeight.option_high


class FrailTrapWeight(TrapWeight):
    """Applies 1 Frail, reducing Block gained for one turn."""
    display_name = "Frail Trap Weight"
    default = TrapWeight.option_high


class VulnerableTrapWeight(TrapWeight):
    """Applies 1 Vulnerable, increasing attack damage taken for one turn."""
    display_name = "Vulnerable Trap Weight"
    default = TrapWeight.option_high


class NoDrawTrapWeight(TrapWeight):
    """Blocks extra card draws for the turn"""
    display_name = "No Draw Trap Weight"
    default = TrapWeight.option_medium


class TangledTrapWeight(TrapWeight):
    """Attack cards cost 1 more energy for the turn."""
    display_name = "Tangled Trap Weight"
    default = TrapWeight.option_medium


class VakuuTrapWeight(TrapWeight):
    """Vakuu plays your hand on one additional turn, no earlier than turn two."""
    display_name = "Vakuu Trap Weight"
    default = TrapWeight.option_medium


class ConfusedTrapWeight(TrapWeight):
    """Randomizes card energy costs when drawn for the rest of combat."""
    display_name = "Confused Trap Weight"
    default = TrapWeight.option_low


class SlothTrapWeight(TrapWeight):
    """Applies a six-card limit per turn for the rest of combat."""
    display_name = "Sloth Trap Weight"
    default = TrapWeight.option_low


class DazedTrapWeight(TrapWeight):
    """Shuffles two Dazed into your combat draw pile."""
    display_name = "Dazed Trap Weight"
    default = TrapWeight.option_medium


# Filler Item Weight Options

class FillerWeight(Choice):
    option_none = 0
    option_low = 1
    option_medium = 3
    option_high = 5
    default = option_low

# Character-specific filler items
class OneGoldFillerWeight(FillerWeight):
    """Weight for One Gold filler items. Grants one gold to its associated character. If every filler weight is None, the generator
    uses that character's One Gold as the required safe fallback."""

    display_name = "One Gold Filler Weight"
    default = FillerWeight.option_none


class FiveGoldFillerWeight(FillerWeight):
    """Weight for Five Gold filler items. Grants five gold to its associated character."""

    display_name = "Five Gold Filler Weight"
    default = FillerWeight.option_high

# Universal filler items
class FreeAttackFillerWeight(FillerWeight):
    """Weight for Free Attack filler items. At the start of the next player combat turn, makes the next Attack played cost zero energy."""

    display_name = "Free Attack Filler Weight"
    default = FillerWeight.option_high


class FreePowerFillerWeight(FillerWeight):
    """Weight for Free Power filler items. At the start of the next player combat turn, makes the next Power played cost zero energy."""

    display_name = "Free Power Filler Weight"
    default = FillerWeight.option_high


class FreeSkillFillerWeight(FillerWeight):
    """Weight for Free Skill filler items. At the start of the next player combat turn, makes the next Skill played cost zero energy."""

    display_name = "Free Skill Filler Weight"
    default = FillerWeight.option_high


class VigorFillerWeight(FillerWeight):
    """Weight for Vigor filler items. Applies Vigor at the start of the next player combat turn."""

    display_name = "Vigor Filler Weight"
    default = FillerWeight.option_high


class ArtifactFillerWeight(FillerWeight):
    """Weight for Artifact filler items. Applies Artifact at the start of the next player combat turn."""

    display_name = "Artifact Filler Weight"
    default = FillerWeight.option_high


class ThornsFillerWeight(FillerWeight):
    """Weight for Thorns filler items. Applies Thorns at the start of the next player combat turn."""

    display_name = "Thorns Filler Weight"
    default = FillerWeight.option_high


class DexterityFillerWeight(FillerWeight):
    """Weight for Dexterity filler items. Applies Dexterity at the start of the next player combat turn."""

    display_name = "Dexterity Filler Weight"
    default = FillerWeight.option_medium


class StrengthFillerWeight(FillerWeight):
    """Weight for Strength filler items. Applies Strength at the start of the next player combat turn."""

    display_name = "Strength Filler Weight"
    default = FillerWeight.option_medium


class PlatingFillerWeight(FillerWeight):
    """Weight for Plating filler items. Applies Plating at the start of the next player combat turn."""

    display_name = "Plating Filler Weight"
    default = FillerWeight.option_medium


class BufferFillerWeight(FillerWeight):
    """Weight for Buffer filler items. Applies Buffer at the start of the next player combat turn."""

    display_name = "Buffer Filler Weight"
    default = FillerWeight.option_low


class FriendshipFillerWeight(FillerWeight):
    """Weight for Friendship filler items. Applies Friendship at the start of the next player combat turn, raising maximum energy by one."""

    display_name = "Friendship Filler Weight"
    default = FillerWeight.option_medium


class PostCombatCardUpgradeFillerWeight(FillerWeight):
    """Weight for Post-Combat Card Upgrade filler items. At the start of the next player combat turn, applies a buff that upgrades a random deck card after combat."""

    display_name = "Post-Combat Card Upgrade Filler Weight"
    default = FillerWeight.option_low


class PostCombatCardRemovalFillerWeight(FillerWeight):
    """Weight for Post-Combat Card Removal filler items. At the start of the next player combat turn, applies a buff that lets you remove a deck card after combat."""

    display_name = "Post-Combat Card Removal Filler Weight"
    default = FillerWeight.option_low


class AdditionalCardRewardFillerWeight(FillerWeight):
    """Weight for Additional Card Reward filler items. At the start of the next player combat turn, applies a buff that adds a card reward after combat."""

    display_name = "Additional Card Reward Filler Weight"
    default = FillerWeight.option_low


class SingleColorlessCardFillerWeight(FillerWeight):
    """Weight for Single Colorless Card filler items. Grants a one-time card reward containing one random colorless card."""

    display_name = "Single Colorless Card Filler Weight"
    default = FillerWeight.option_medium

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
    use_new_logic: UseNewLogic
    ancient_relic_location: AncientRelicLocation
    ancient_relic_pool: AncientRelicPool
    relic_rewards_available_anytime: RelicRewardsAvailableAnytime
    progressive_starter_card: ProgressiveStarterCard
    progressive_starter_relic: ProgressiveStarterRelic
    shuffle_all_cards: CardReward

    # Bonus items
    bonus_items: BonusItems

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
    # Traps
    trap_chance: TrapChance
    weak_trap_weight: WeakTrapWeight
    frail_trap_weight: FrailTrapWeight
    vulnerable_trap_weight: VulnerableTrapWeight
    no_draw_trap_weight: NoDrawTrapWeight
    tangled_trap_weight: TangledTrapWeight
    vakuu_trap_weight: VakuuTrapWeight
    confused_trap_weight: ConfusedTrapWeight
    sloth_trap_weight: SlothTrapWeight
    dazed_trap_weight: DazedTrapWeight
