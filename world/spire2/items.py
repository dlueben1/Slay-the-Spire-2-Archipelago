from BaseClasses import ItemClassification, Optional
import typing
from typing import Dict
from collections import defaultdict
from enum import auto, Enum

from worlds.spire2.characters import character_list
from worlds.spire2.constants import CHAR_OFFSET, NUM_CUSTOM


class ItemType(Enum):
    CARD_REWARD = auto()
    RARE_CARD_REWARD = auto()
    RELIC = auto()
    BOSS_RELIC = auto()
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
    # TRAP = auto()
    CAW_CAW = auto()
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

base_item_table: Dict[str, ItemData] = {
    'Card Reward': ItemData(1, ItemType.CARD_REWARD, ItemClassification.progression_deprioritized),
    'Rare Card Reward': ItemData(2, ItemType.RARE_CARD_REWARD, ItemClassification.progression_deprioritized),
    'Relic': ItemData(3, ItemType.RELIC, ItemClassification.progression),
    'Boss Relic': ItemData(4, ItemType.BOSS_RELIC, ItemClassification.progression),
    'One Gold': ItemData(5, ItemType.GOLD, ItemClassification.filler),
    'Five Gold': ItemData(6, ItemType.GOLD, ItemClassification.filler),
    '15 Gold': ItemData(15, ItemType.GOLD, ItemClassification.useful),
    '30 Gold': ItemData(16, ItemType.GOLD, ItemClassification.progression_deprioritized_skip_balancing),
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
    'Ascension Down': ItemData(19, ItemType.ASCENSION_DOWN, ItemClassification.useful),

    # Event Items
    'Victory': ItemData(None, ItemType.EVENT, ItemClassification.progression, True, True),
    'Beat Act 1 Boss': ItemData(None, ItemType.EVENT, ItemClassification.progression, True),
    'Beat Act 2 Boss': ItemData(None, ItemType.EVENT, ItemClassification.progression, True),
}

universal_items: Dict[str, ItemData] = {
    'CAW CAW': ItemData(1, ItemType.CAW_CAW, ItemClassification.filler)
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
