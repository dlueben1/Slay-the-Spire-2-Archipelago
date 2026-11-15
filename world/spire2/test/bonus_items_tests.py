import unittest

from Options import OptionError

from worlds.spire2.items import universal_bonus_items
from worlds.spire2.world import validate_bonus_entry
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


class TestNewBonusItems(Spire2TestBase):
    options = {
        "characters": ["ironclad"],
        "bonus_items": [
            {"RELIC": {"Value": "Orichalcum"}},
            {"ENCHANTMENT": {"Pools": ["Common", "Rare"]}},
            {"ENCHANTMENT": {"Value": "royally approved"}},
            {"ENCHANTED_CARDS": {"Value": "Swift"}},
            {"MAX_HP": {"Value": 6}},
        ],
    }

    def test_each_entry_adds_its_item(self):
        names = [item.name for item in self.multiworld.itempool if item.code in range(600, 700)]
        self.assertEqual(
            sorted(["Bonus Relic", "Bonus Enchantment", "Bonus Enchantment", "Bonus Enchanted Cards",
                    "Bonus Max HP"]),
            sorted(names),
        )
        self.assertEqual(self.options["bonus_items"], self.world.fill_slot_data()["bonus_items"])


class TestBonusEntryValidation(unittest.TestCase):
    def test_rejects_entries_the_client_cannot_honour(self):
        for bonus_type, selector in (
            ("MAX_HP", {"Value": 0}),
            ("MAX_HP", {"Value": "lots"}),
            ("MAX_HP", {"Pools": ["Common"]}),
            ("ENCHANTMENT", {"Pools": ["Legendary"]}),
            ("ENCHANTMENT", {"Value": "TezcatarasEmber"}),
            ("ENCHANTED_CARDS", {"Value": "NotAnEnchantment"}),
            ("RELIC", {"Value": 3}),
        ):
            with self.subTest(bonus_type=bonus_type, selector=selector):
                with self.assertRaises(OptionError):
                    validate_bonus_entry(bonus_type, selector)

    def test_accepts_names_in_any_case_and_spacing(self):
        validate_bonus_entry("ENCHANTMENT", {"Value": "ROYALLY_APPROVED"})
        validate_bonus_entry("ENCHANTED_CARDS", {"Pools": ["Uncommon"]})
        validate_bonus_entry("MAX_HP", {"Value": "6"})
        for name in ("Checkbound", "Eager", "Exposing"):
            validate_bonus_entry("ENCHANTMENT", {"Value": name})
