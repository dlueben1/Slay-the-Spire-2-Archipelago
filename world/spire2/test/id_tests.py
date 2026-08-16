from unittest import TestCase

from worlds.spire2 import SlayTheSpire2World


# These are published network IDs. Add new entries when items are introduced, but never change an existing value.
UNIVERSAL_ITEM_IDS = {
    "Free Attack": 500,
    "Free Power": 501,
    "Free Skill": 502,
    "Dexterity": 503,
    "Strength": 504,
    "Plating": 505,
    "Friendship": 506,
    "Post-Combat Card Upgrade": 507,
    "Post-Combat Card Removal": 508,
    "Additional Card Reward": 509,
    "Buffer": 510,
    "Vigor": 511,
    "Thorns": 512,
    "Artifact": 513,
}

CHARACTER_ITEM_BASE_IDS = {
    "Card Reward": 1,
    "Rare Card Reward": 2,
    "Relic": 3,
    "Progressive Ancient": 4,
    "One Gold": 5,
    "Five Gold": 6,
    "Progressive Rest": 7,
    "Progressive Smith": 8,
    "Shop Card Slot": 9,
    "Neutral Shop Card Slot": 10,
    "Shop Relic Slot": 11,
    "Shop Potion Slot": 12,
    "Progressive Shop Remove": 13,
    "Unlock": 14,
    "Combat Gold": 15,
    "Elite Gold": 16,
    "Boss Gold": 17,
    "Potion": 18,
    "Disable Swarming Elites": 19,
    "Disable Weary Traveler": 20,
    "Disable Poverty": 21,
    "Disable Tight Belt": 22,
    "Disable Ascender's Bane": 23,
    "Disable Inflation": 24,
    "Disable Scarcity": 25,
    "Disable Tough Enemies": 26,
    "Disable Deadly Enemies": 27,
    "Disable Double Boss": 28,
    "Progressive Starter Card": 29,
    "Progressive Starter Relic": 30,
}

CHARACTER_ITEM_OFFSETS = {
    "Ironclad": 10000,
    "Silent": 20000,
    "Defect": 30000,
    "Regent": 40000,
    "Necrobinder": 50000,
    "Custom Character 1": 60000,
    "Custom Character 2": 70000,
    "Custom Character 3": 80000,
    "Custom Character 4": 90000,
    "Custom Character 5": 100000,
}


def build_published_item_ids() -> dict[str, int]:
    published_ids = dict(UNIVERSAL_ITEM_IDS)
    for character_name, character_offset in CHARACTER_ITEM_OFFSETS.items():
        for item_name, base_id in CHARACTER_ITEM_BASE_IDS.items():
            published_ids[f"{character_name} {item_name}"] = character_offset + base_id
    return published_ids


class TestItemIdStability(TestCase):
    def test_item_ids_match_published_mapping(self):
        self.assertEqual(build_published_item_ids(), SlayTheSpire2World.item_name_to_id)
