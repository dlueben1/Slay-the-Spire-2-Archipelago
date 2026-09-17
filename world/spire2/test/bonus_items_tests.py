from worlds.spire2.items import universal_bonus_items
from worlds.spire2.test import Spire2TestBase


class TestBonusWaxItems(Spire2TestBase):
    options = {
        "characters": ["ironclad", "silent"],
        "bonus_items": [
            {"WAX_RELIC": {"Value": "THE_BOOT"}},
            {"WAX_RELIC": {"Pools": ["Common", "Uncommon"]}},
        ],
    }

    def test_bonus_receipts_are_universal_and_not_duplicated_per_character(self):
        bonus = [item for item in self.multiworld.itempool if item.name == "Bonus Wax Relic"]
        self.assertEqual(2, len(bonus))
        self.assertEqual({600}, {item.code for item in bonus})
        self.assertEqual(600, universal_bonus_items["Bonus Wax Relic"].code)

    def test_bonus_entries_preserve_slot_order(self):
        self.assertEqual(self.options["bonus_items"], self.world.fill_slot_data()["bonus_items"])

    def test_bonus_items_replace_filler_without_changing_pool_size(self):
        locations = [location for location in self.multiworld.get_locations()
                     if location.address is not None and location.item is None]
        self.assertEqual(len(locations), len(self.multiworld.itempool))
