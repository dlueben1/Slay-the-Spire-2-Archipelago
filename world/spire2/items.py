from BaseClasses import ItemClassification, Optional
import typing
from typing import Dict
from collections import defaultdict
from enum import auto, Enum

from worlds.spire2.characters import character_list
from worlds.spire2.constants import CHAR_OFFSET, NUM_CUSTOM, ASCENSIONS


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
    # TRAP = auto()
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
    'Rare Card Reward': ItemData(2, ItemType.RARE_CARD_REWARD, ItemClassification.progression_deprioritized),
    'Relic': ItemData(3, ItemType.RELIC, ItemClassification.progression),
    'Progressive Ancient': ItemData(4, ItemType.PROGRESSIVE_ANCIENT, ItemClassification.progression),
    'One Gold': ItemData(5, ItemType.GOLD, ItemClassification.filler),
    'Five Gold': ItemData(6, ItemType.GOLD, ItemClassification.filler),
    'Combat Gold': ItemData(15, ItemType.GOLD, ItemClassification.useful),
    'Elite Gold': ItemData(16, ItemType.GOLD, ItemClassification.progression_deprioritized_skip_balancing),
    'Boss Gold': ItemData(17, ItemType.GOLD, ItemClassification.progression),
    'Progressive Rest': ItemData(7, ItemType.CAMPFIRE, ItemClassification.progression),
    'Progressive Smith': ItemData(8, ItemType.CAMPFIRE, ItemClassification.progression),
    'Shop Card Slot': ItemData(9, ItemType.SHOP_CARD, ItemClassification.progression_deprioritized),
    'Neutral Shop Card Slot': ItemData(10, ItemType.SHOP_NEUTRAL, ItemClassification.progression_deprioritized),
    'Shop Relic Slot': ItemData(11, ItemType.SHOP_RELIC, ItemClassification.progression_deprioritized),
    'Shop Potion Slot': ItemData(12, ItemType.SHOP_POTION, ItemClassification.progression_deprioritized),
    'Progressive Shop Remove': ItemData(13, ItemType.SHOP_REMOVE, ItemClassification.progression_deprioritized),
    'Unlock': ItemData(14, ItemType.CHAR_UNLOCK, ItemClassification.progression),
    'Potion': ItemData(18, ItemType.POTION, ItemClassification.useful),
    'Progressive Starter Card': ItemData(29, ItemType.PROGRESSIVE_STARTER_CARD, ItemClassification.progression),
    'Progressive Starter Relic': ItemData(30, ItemType.PROGRESSIVE_STARTER_RELIC, ItemClassification.progression),

    # Event Items
    'Victory': ItemData(None, ItemType.EVENT, ItemClassification.progression, True, True),
    'Beat Act 1 Boss': ItemData(None, ItemType.EVENT, ItemClassification.progression, True),
    'Beat Act 2 Boss': ItemData(None, ItemType.EVENT, ItemClassification.progression, True),
    **{asc: ItemData(i + 19, ItemType.ASCENSION_DOWN, ItemClassification.useful) for i, asc in enumerate(ASCENSIONS.values()) }
}

# Items in this table are character-agnostic, and can be claimed by any of them
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

# `bonus_item_table` is keyed by the configuration selector: "WAX_RELIC". This is what the YAML option uses.
bonus_item_table: Dict[str, BonusItemData] = {
    'WAX_RELIC': BonusItemData(
        'Bonus Wax Relic',
        ItemData(600, ItemType.BONUS_WAX_RELIC, ItemClassification.useful),
    ),
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

def create_item_tables(vanilla_chars: typing.List[str], extras: int) -> typing.Tuple[dict[str, ItemData], dict[
    typing.Union[str, int],dict[str,ItemData]], dict[str,str]]:
    item_name_to_data = {
        **universal_items,
        **universal_bonus_items,
    }

    characters_to_items: dict[typing.Union[str, int],dict[str, ItemData]] = defaultdict(lambda: dict())
    event_item_pairs: dict[str, str] = dict()
    char_num = 1

    for char in vanilla_chars:
        for key, data in base_item_table.items():
            newkey = f"{char} {key}"
            newval = ItemData.increment(data, char_num*CHAR_OFFSET)
            item_name_to_data[newkey] = newval
            characters_to_items[char][newkey] = newval
        for key, val in base_event_item_pairs.items():
            event_item_pairs[f"{char} {key}"] = f"{char} {val}"
        char_num += 1

    for i in range(extras):
        for key, data in base_item_table.items():
            newkey = f"Custom Character {i+1} {key}"
            newval = ItemData.increment(data, char_num * CHAR_OFFSET)
            item_name_to_data[newkey] = newval
            characters_to_items[i+1][newkey] = newval
        for key, val in base_event_item_pairs.items():
            event_item_pairs[f"Custom Character {i+1} {key}"] = f"Custom Character {i+1} {val}"
        char_num += 1


    return item_name_to_data, characters_to_items, event_item_pairs

item_table, chars_to_items, event_item_pairs = create_item_tables(character_list, NUM_CUSTOM)


def create_item_groups(
        items: dict[str, ItemData],
        characters_to_items: dict[typing.Union[str, int], dict[str, ItemData]],
) -> dict[str, typing.Set[str]]:
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
        "Bonus Items": set(),
    }

    for item_name, item_data in items.items():
        if item_data.code is None:
            continue

        if item_data.type == ItemType.GOLD:
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
        elif item_data.type == ItemType.BUFF:
            groups["Buffs"].add(item_name)
        elif item_name in universal_bonus_items:
            groups["Bonus Items"].add(item_name)

    for character_key, character_items in characters_to_items.items():
        character_name = character_key if isinstance(character_key, str) else f"Custom Character {character_key}"
        character_groups = {
            ItemType.GOLD: groups.setdefault(f"{character_name} Gold", set()),
            ItemType.CAMPFIRE: groups.setdefault(f"{character_name} Campfire Upgrades", set()),
        }
        character_shop_group = groups.setdefault(f"{character_name} Shop Slots", set())
        character_starter_group = groups.setdefault(f"{character_name} Starter", set())

        for item_name, item_data in character_items.items():
            if item_data.type in character_groups:
                character_groups[item_data.type].add(item_name)
            elif item_data.type in {
                ItemType.SHOP_CARD,
                ItemType.SHOP_NEUTRAL,
                ItemType.SHOP_RELIC,
                ItemType.SHOP_POTION,
                ItemType.SHOP_REMOVE,
            }:
                character_shop_group.add(item_name)
            elif item_data.type in {
                ItemType.PROGRESSIVE_STARTER_CARD,
                ItemType.PROGRESSIVE_STARTER_RELIC
            }:
                character_starter_group.add(item_name)

    return groups


item_groups = create_item_groups(item_table, chars_to_items)
