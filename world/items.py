from BaseClasses import ItemClassification, Optional
import typing
from typing import Dict
from collections import defaultdict
from enum import auto, Enum

START_ID = 900000

class ItemData(typing.NamedTuple):
    classification: ItemClassification
    amount: Optional[int] = 1

# Collection of possible items
item_table: Dict[str, ItemData] = {
    # Gold Rewards
    "2 Gold": ItemData(ItemClassification.filler, 2),
    "5 Gold": ItemData(ItemClassification.filler, 5),
    "25 Gold": ItemData(ItemClassification.useful, 25),
    # Card Rewards
    "Card Reward": ItemData(ItemClassification.useful),
    "Rare Card Reward": ItemData(ItemClassification.useful),
    # Relic Rewards
    "Relic Reward": ItemData(ItemClassification.useful),
    # Potion Rewards
    "Potion Filler": ItemData(ItemClassification.filler),
    "Potion Reward": ItemData(ItemClassification.useful),
    # Character Rewards
    "Ironclad": ItemData(ItemClassification.progression),
    "Silent": ItemData(ItemClassification.progression),
    "Defect": ItemData(ItemClassification.progression),
    "Regent": ItemData(ItemClassification.progression),
    "Necrobinder": ItemData(ItemClassification.progression),
    # Progression
    "Victory": ItemData(ItemClassification.progression)
}