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
    # Progression
    "Victory": ItemData(ItemClassification.progression)
}