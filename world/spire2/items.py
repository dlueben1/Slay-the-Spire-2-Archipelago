from BaseClasses import ItemClassification
import typing
from typing import Dict
from collections import defaultdict
from enum import auto, Enum

from worlds.spire2.characters import CharacterDefinition, character_registry
from worlds.spire2.constants import CHAR_OFFSET, ASCENSIONS


class ItemType(Enum):
    CARD_REWARD = auto()
    RARE_CARD_REWARD = auto()
    RELIC = auto()
    PROGRESSIVE_ANCIENT = auto()
    GOLD = auto()
    EVENT = auto()
    CAMPFIRE = auto()
    SHOP_CARD = auto()
    SHOP_NEUTRAL = auto()
    SHOP_RELIC = auto()
    SHOP_POTION = auto()
    SHOP_REMOVE = auto()
    CHAR_UNLOCK = auto()
    POTION = auto()
    ASCENSION_DOWN = auto()
    PROGRESSIVE_STARTER_CARD = auto()
    PROGRESSIVE_STARTER_RELIC = auto()
    BONUS_WAX_RELIC = auto()
    BONUS_RELIC = auto()
    BONUS_ENCHANTMENT = auto()
    BONUS_ENCHANTED_CARDS = auto()
    BONUS_MAX_HP = auto()
    BONUS_CARDS = auto()
    INTENT_SIGHT = auto()
    HEALTH_SIGHT = auto()
    PILE_SIGHT = auto()
    MAP_SIGHT = auto()
    CARD_SIGHT = auto()
    TRAP = auto()
    CAW_CAW = auto()
    BUFF = auto()
    FILLER_CARD_REWARD = auto()
    OTHER = auto()

class ItemData(typing.NamedTuple):
    code: typing.Optional[int]
    type: ItemType
    classification: ItemClassification
    event: bool = False
    is_victory: bool = False
    char_offset: int = -1

    @staticmethod
    def increment(base: 'ItemData', char_offset: int) -> 'ItemData':
        newcode = base.code + char_offset if base.code is not None else base.code
        return ItemData(newcode, base.type, base.classification, base.event, base.is_victory, char_offset//CHAR_OFFSET)


class BonusItemData(typing.NamedTuple):
    item_name: str
    item_data: ItemData

# Items in this table get unique variations for each character. For example, "Five Gold" becomes "Ironclad Five Gold", "Silent Five Gold", etc.
base_item_table: Dict[str, ItemData] = {
    'Card Reward': ItemData(1, ItemType.CARD_REWARD, ItemClassification.progression_deprioritized),
    'Rare Card Reward': ItemData(2, ItemType.RARE_CARD_REWARD, ItemClassification.progression),
    'Relic': ItemData(3, ItemType.RELIC, ItemClassification.progression),
    'Progressive Ancient': ItemData(4, ItemType.PROGRESSIVE_ANCIENT, ItemClassification.progression | ItemClassification.useful),
    'One Gold': ItemData(5, ItemType.GOLD, ItemClassification.filler),
    'Five Gold': ItemData(6, ItemType.GOLD, ItemClassification.filler),
    'Combat Gold': ItemData(15, ItemType.GOLD, ItemClassification.useful),
    'Elite Gold': ItemData(16, ItemType.GOLD, ItemClassification.progression_deprioritized_skip_balancing),
    'Boss Gold': ItemData(17, ItemType.GOLD, ItemClassification.progression),
    'Progressive Rest': ItemData(7, ItemType.CAMPFIRE, ItemClassification.progression),
    'Progressive Smith': ItemData(8, ItemType.CAMPFIRE, ItemClassification.progression),
    'Shop Card Slot': ItemData(9, ItemType.SHOP_CARD, ItemClassification.progression_deprioritized_skip_balancing),
    'Shop Colorless Card Slot': ItemData(10, ItemType.SHOP_NEUTRAL, ItemClassification.progression_deprioritized_skip_balancing),
    'Shop Relic Slot': ItemData(11, ItemType.SHOP_RELIC, ItemClassification.progression_deprioritized_skip_balancing),
    'Shop Potion Slot': ItemData(12, ItemType.SHOP_POTION, ItemClassification.progression_deprioritized_skip_balancing),
    'Progressive Shop Remove': ItemData(13, ItemType.SHOP_REMOVE, ItemClassification.progression_deprioritized),
    'Unlock': ItemData(14, ItemType.CHAR_UNLOCK, ItemClassification.progression),
    'Potion': ItemData(18, ItemType.POTION, ItemClassification.useful),
    'Progressive Starter Card': ItemData(29, ItemType.PROGRESSIVE_STARTER_CARD, ItemClassification.progression | ItemClassification.useful),
    'Progressive Starter Relic': ItemData(30, ItemType.PROGRESSIVE_STARTER_RELIC, ItemClassification.progression | ItemClassification.useful),

    # Event Items
    'Victory': ItemData(None, ItemType.EVENT, ItemClassification.progression, True, True),
    'Beat Act 1 Boss': ItemData(None, ItemType.EVENT, ItemClassification.progression, True),
    'Beat Act 2 Boss': ItemData(None, ItemType.EVENT, ItemClassification.progression, True),
}
base_item_table.update({
    asc: ItemData(i + 19, ItemType.ASCENSION_DOWN, ItemClassification.useful)
    for i, asc in enumerate(ASCENSIONS.values())
})

# Items in this table are character-agnostic, and can be claimed by any of them
# 500-599 is Filler Items - Buffs
universal_items: Dict[str, ItemData] = {
    # Filler / Junk
    'Free Attack': ItemData(500, ItemType.BUFF, ItemClassification.filler),
    'Free Power': ItemData(501, ItemType.BUFF, ItemClassification.filler),
    'Free Skill': ItemData(502, ItemType.BUFF, ItemClassification.filler),
    'Dexterity': ItemData(503, ItemType.BUFF, ItemClassification.filler),
    'Strength': ItemData(504, ItemType.BUFF, ItemClassification.filler),
    'Plating': ItemData(505, ItemType.BUFF, ItemClassification.filler),
    'Friendship': ItemData(506, ItemType.BUFF, ItemClassification.filler),
    'Post-Combat Card Upgrade': ItemData(507, ItemType.BUFF, ItemClassification.filler),
    'Post-Combat Card Removal': ItemData(508, ItemType.BUFF, ItemClassification.filler),
    'Additional Card Reward': ItemData(509, ItemType.BUFF, ItemClassification.filler),
    'Buffer': ItemData(510, ItemType.BUFF, ItemClassification.filler),
    'Vigor': ItemData(511, ItemType.BUFF, ItemClassification.filler),
    'Thorns': ItemData(512, ItemType.BUFF, ItemClassification.filler),
    'Artifact': ItemData(513, ItemType.BUFF, ItemClassification.filler),
    #'Single Colorless Card': ItemData(508, ItemType.FILLER_CARD_REWARD, ItemClassification.filler),
}

# `bonus_item_table` is keyed by the configuration selector, such as "WAX_RELIC". This is what the
# YAML option uses. Bonus items take IDs 600-699; the client's ItemTable uses the same IDs.
bonus_item_table: Dict[str, BonusItemData] = {
    'WAX_RELIC': BonusItemData(
        'Bonus Wax Relic',
        ItemData(600, ItemType.BONUS_WAX_RELIC, ItemClassification.useful),
    ),
    'RELIC': BonusItemData(
        'Bonus Relic',
        ItemData(601, ItemType.BONUS_RELIC, ItemClassification.useful),
    ),
    'ENCHANTMENT': BonusItemData(
        'Bonus Enchantment',
        ItemData(602, ItemType.BONUS_ENCHANTMENT, ItemClassification.useful),
    ),
    'ENCHANTED_CARDS': BonusItemData(
        'Bonus Enchanted Cards',
        ItemData(603, ItemType.BONUS_ENCHANTED_CARDS, ItemClassification.useful),
    ),
    'MAX_HP': BonusItemData(
        'Bonus Max HP',
        ItemData(604, ItemType.BONUS_MAX_HP, ItemClassification.useful),
    ),
    'CARDS': BonusItemData(
        'Bonus Cards',
        ItemData(605, ItemType.BONUS_CARDS, ItemClassification.useful),
    ),
}

# Enchantments that ENCHANTMENT and ENCHANTED_CARDS bonus items can give, by the tier a YAML `Pools`
# entry names. Eager, Exposing and Checkbound are the mod's own enchantments. Random rolls draw only from these tiers. The client has the same table with each
# enchantment's amount (`BonusEnchantmentCatalog`), and the web builder has a copy for its picker.
BONUS_ENCHANTMENT_TIERS: Dict[str, tuple] = {
    'Common': ('Sharp', 'Nimble', 'Adroit', 'Steady', 'Swift', 'Vigorous'),
    'Uncommon': ('Inky', 'Momentum', 'RoyallyApproved', 'Goopy', 'Eager', 'Exposing'),
    'Rare': ('Sown', 'Glam', 'Imbued', 'SoulsPower', 'Instinct', 'Spiral', 'Checkbound'),
}
# Allowed only when a YAML entry names them as its `Value`: they have a drawback or need a rest site.
BONUS_ENCHANTMENTS_BY_VALUE_ONLY = ('Corrupted', 'Slither', 'SlumberingEssence', 'PerfectFit', 'Clone')

BONUS_MAX_HP_LIMIT = 50

# The words a CARDS bonus `Pools` entry is built from: at most one source, one card type and one
# rarity, plus Upgraded. With no source, the cards come from the run's own character. The client
# has the same words in `BonusCardPools`, and the web builder has a copy for its picker.
BONUS_CARD_CHARACTERS = ('Ironclad', 'Silent', 'Defect', 'Necrobinder', 'Regent', 'Fool')
BONUS_CARD_SOURCES = (*BONUS_CARD_CHARACTERS, 'Other', 'Colorless', 'Event', 'Ancient')
BONUS_CARD_TYPES = ('Attack', 'Skill', 'Power')
BONUS_CARD_RARITIES = ('Common', 'Uncommon', 'Rare')
BONUS_CARD_RANDOM = 'Random'
BONUS_CARD_UPGRADED = 'Upgraded'
# British spelling accepted for the source.
BONUS_CARD_ALIASES = {'colourless': 'Colorless'}


def parse_bonus_card_pool(entry: str) -> Dict[str, str]:
    """Splits a CARDS pool such as 'Regent Attack' or 'Colorless Rare Upgraded' into its parts.
    Raises ValueError with a player-facing reason when the entry can't be honoured."""
    categories = (
        ('source', BONUS_CARD_SOURCES),
        ('type', BONUS_CARD_TYPES),
        ('rarity', BONUS_CARD_RARITIES),
        ('random', (BONUS_CARD_RANDOM,)),
        ('upgraded', (BONUS_CARD_UPGRADED,)),
    )
    parts: Dict[str, str] = {}
    words = entry.split()
    if not words:
        raise ValueError("a pool can't be empty")
    for word in words:
        key = word.lower()
        name = BONUS_CARD_ALIASES.get(key)
        category = 'source' if name else None
        for candidate_category, names in categories:
            if category:
                break
            name = next((known for known in names if known.lower() == key), None)
            if name:
                category = candidate_category
        if not category:
            raise ValueError(f"'{word}' is not a card source, type, rarity, Random or Upgraded")
        if category in parts:
            raise ValueError(f"it names more than one {category}")
        parts[category] = name
    if 'random' in parts and set(parts) - {'random', 'upgraded'}:
        raise ValueError("Random can only be combined with Upgraded")
    if parts.get('source') in ('Event', 'Ancient') and 'rarity' in parts:
        raise ValueError(f"{parts['source']} cards have no Common, Uncommon or Rare")
    if parts.get('source') == 'Colorless' and parts.get('rarity') == 'Common':
        raise ValueError("there are no Common colorless cards")
    return parts


def normalize_bonus_enchantment(name: str) -> str:
    """Matches 'RoyallyApproved', 'royally approved' and 'ROYALLY_APPROVED' alike."""
    return "".join(ch for ch in name if ch.isalnum()).lower()


BONUS_ENCHANTMENT_VALUES: Dict[str, str] = {
    normalize_bonus_enchantment(name): name
    for name in (*[name for tier in BONUS_ENCHANTMENT_TIERS.values() for name in tier],
                 *BONUS_ENCHANTMENTS_BY_VALUE_ONLY)
}

# Universal trap IDs are shared with the client and never receive character offsets.
trap_item_table: Dict[str, ItemData] = {
    "Weak Trap": ItemData(700, ItemType.TRAP, ItemClassification.trap),
    "Frail Trap": ItemData(701, ItemType.TRAP, ItemClassification.trap),
    "Vulnerable Trap": ItemData(702, ItemType.TRAP, ItemClassification.trap),
    "No Draw Trap": ItemData(703, ItemType.TRAP, ItemClassification.trap),
    "Tangled Trap": ItemData(704, ItemType.TRAP, ItemClassification.trap),
    "Vakuu Trap": ItemData(705, ItemType.TRAP, ItemClassification.trap),
    "Confused Trap": ItemData(706, ItemType.TRAP, ItemClassification.trap),
    "Sloth Trap": ItemData(707, ItemType.TRAP, ItemClassification.trap),
    "Dazed Trap": ItemData(708, ItemType.TRAP, ItemClassification.trap),
}


# Universal items that undo a challenge option, one ID range for all of them: 800-899.
# Each is added to the pool only when its option is on. Logic counts only a second copy's Sight
# Bonus, and only in Act 3 (SIGHT_BONUS_POWER). Sights are progression so players see them as worth
# buying in other games' shops; deprioritized and skip_balancing keep generation placing them
# almost like useful items, so finding them stays a surprise.
# Each Hidden option sets how many copies of its Sight item are added.
# Any copy undoes the whole challenge. The client keeps a one-step-per-copy rule commented out in
# ChallengeSightItems.UnlockedSteps: the first Intent Sight showed intents without numbers, the
# first Health Sight showed only your own side's health.
challenge_item_table: Dict[str, ItemData] = {
    "Intent Sight": ItemData(800, ItemType.INTENT_SIGHT, ItemClassification.progression_deprioritized_skip_balancing),
    "Health Sight": ItemData(801, ItemType.HEALTH_SIGHT, ItemClassification.progression_deprioritized_skip_balancing),
    "Pile Sight": ItemData(802, ItemType.PILE_SIGHT, ItemClassification.progression_deprioritized_skip_balancing),
    "Map Sight": ItemData(803, ItemType.MAP_SIGHT, ItemClassification.progression_deprioritized_skip_balancing),
    "Card Sight": ItemData(804, ItemType.CARD_SIGHT, ItemClassification.progression_deprioritized_skip_balancing),
}

# `universal_bonus_items` is keyed by the actual Archipelago item name (like "Bonus Wax Relic").
# This matches item_table and create_item() lookups.
universal_bonus_items: Dict[str, ItemData] = {
    bonus.item_name: bonus.item_data for bonus in bonus_item_table.values()
}

base_event_item_pairs: Dict[str, str] = {
    "Act 1 Boss": "Beat Act 1 Boss",
    "Act 2 Boss": "Beat Act 2 Boss",
    "Act 3 Boss": "Victory",
}

def create_item_tables(registry: dict[int, CharacterDefinition]) -> tuple[
        dict[str, ItemData], dict[str, dict[str, ItemData]], dict[str, str]]:
    item_name_to_data = universal_items | universal_bonus_items | trap_item_table | challenge_item_table
    characters_to_items: dict[str, dict[str, ItemData]] = defaultdict(dict)
    character_event_pairs: dict[str, str] = {}
    for block, definition in registry.items():
        for key, base_data in base_item_table.items():
            name = f"{definition.name} {key}"
            data = ItemData.increment(base_data, block * CHAR_OFFSET)
            item_name_to_data[name] = data
            characters_to_items[definition.name][name] = data
        for key, val in base_event_item_pairs.items():
            character_event_pairs[f"{definition.name} {key}"] = f"{definition.name} {val}"
    return item_name_to_data, characters_to_items, character_event_pairs


item_table, chars_to_items, event_item_pairs = create_item_tables(character_registry)


# Groups span every character on purpose. Each character block adds to the data package that servers keep in
# memory, and per-character groups would add another copy of that character's names to it.
def create_item_groups(items: dict[str, ItemData]) -> dict[str, typing.Set[str]]:
    groups: dict[str, typing.Set[str]] = {
        "Gold": set(),
        "Campfire Upgrades": set(),
        "Shop Slots": set(),
        "Character Unlocks": set(),
        "Potions": set(),
        "Card Rewards": set(),
        "Rare Card Rewards": set(),
        "Relics": set(),
        "Ancients": set(),
        "Ascension Downs": set(),
        "Starter Upgrades": set(),
        "Buffs": set(),
        "Traps": set(),
        "Bonus Items": set(),
        "Sights": set(),
    }
    sight_types = {ItemType.INTENT_SIGHT, ItemType.HEALTH_SIGHT, ItemType.PILE_SIGHT, ItemType.MAP_SIGHT,
                   ItemType.CARD_SIGHT}

    for item_name, item_data in items.items():
        if item_data.code is None:
            continue

        if item_data.type == ItemType.GOLD:
            # One and Five Gold are filler: a "!hint Gold" should not spend a hint on them.
            if not item_name.endswith((" One Gold", " Five Gold")):
                groups["Gold"].add(item_name)
        elif item_data.type == ItemType.CAMPFIRE:
            groups["Campfire Upgrades"].add(item_name)
        elif item_data.type in {
            ItemType.SHOP_CARD,
            ItemType.SHOP_NEUTRAL,
            ItemType.SHOP_RELIC,
            ItemType.SHOP_POTION,
            ItemType.SHOP_REMOVE,
        }:
            groups["Shop Slots"].add(item_name)
        elif item_data.type == ItemType.CHAR_UNLOCK:
            groups["Character Unlocks"].add(item_name)
        elif item_data.type == ItemType.POTION:
            groups["Potions"].add(item_name)
        elif item_data.type in {ItemType.CARD_REWARD, ItemType.RARE_CARD_REWARD}:
            groups["Card Rewards"].add(item_name)
            if item_data.type == ItemType.RARE_CARD_REWARD:
                groups["Rare Card Rewards"].add(item_name)
        elif item_data.type == ItemType.RELIC:
            groups["Relics"].add(item_name)
        elif item_data.type == ItemType.PROGRESSIVE_ANCIENT:
            groups["Ancients"].add(item_name)
        elif item_data.type == ItemType.ASCENSION_DOWN:
            groups["Ascension Downs"].add(item_name)
        elif item_data.type in {ItemType.PROGRESSIVE_STARTER_CARD, ItemType.PROGRESSIVE_STARTER_RELIC}:
            groups["Starter Upgrades"].add(item_name)
        elif item_data.type == ItemType.TRAP:
            groups["Traps"].add(item_name)
        elif item_data.type == ItemType.BUFF:
            groups["Buffs"].add(item_name)
        elif item_name in universal_bonus_items:
            groups["Bonus Items"].add(item_name)
        elif item_data.type in sight_types:
            groups["Sights"].add(item_name)

    return groups


item_groups = create_item_groups(item_table)
