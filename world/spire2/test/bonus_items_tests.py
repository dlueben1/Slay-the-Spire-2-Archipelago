import unittest

from Options import OptionError

from worlds.spire2.items import universal_bonus_items, parse_bonus_card_pool
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

    def test_slot_data_keeps_every_entry(self):
        self.assertCountEqual(self.options["bonus_items"], self.world.fill_slot_data()["bonus_items"])

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
        self.assertCountEqual(self.options["bonus_items"], self.world.fill_slot_data()["bonus_items"])


class TestBonusItemShuffle(Spire2TestBase):
    relics = ["Anchor", "Vajra", "Orichalcum", "Lantern", "Akabeko", "Pantograph"]
    options = {
        "characters": ["ironclad"],
        "bonus_items": [
            *({"WAX_RELIC": {"Value": relic}} for relic in relics[:3]),
            {"MAX_HP": {"Value": 6}},
            *({"WAX_RELIC": {"Value": relic}} for relic in relics[3:]),
            {"MAX_HP": {"Value": 9}},
        ],
    }

    def test_each_kind_keeps_its_places_and_entries(self):
        shuffled = self.world.fill_slot_data()["bonus_items"]
        self.assertEqual([next(iter(entry)) for entry in self.options["bonus_items"]],
                         [next(iter(entry)) for entry in shuffled])
        self.assertCountEqual(self.options["bonus_items"], shuffled)

    def test_order_changes_between_seeds(self):
        orders = set()
        for seed in range(5):
            self.world.random.seed(seed)
            self.world.shuffle_bonus_items()
            orders.add(tuple(entry["WAX_RELIC"]["Value"]
                             for entry in self.world.options.bonus_items.value if "WAX_RELIC" in entry))
        self.assertGreater(len(orders), 1)


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


class TestBonusCards(Spire2TestBase):
    options = {
        "characters": ["ironclad"],
        "bonus_items": [
            {"CARDS": {"Pools": ["Regent Attack", "Colorless Rare", "Random"]}},
            {"CARDS": {"Value": "Apotheosis"}},
        ],
    }

    def test_each_entry_adds_a_bonus_cards_item(self):
        codes = [item.code for item in self.multiworld.itempool if item.name == "Bonus Cards"]
        self.assertEqual([605, 605], codes)
        self.assertCountEqual(self.options["bonus_items"], self.world.fill_slot_data()["bonus_items"])


class TestBonusCardPools(unittest.TestCase):
    def test_reads_each_part_in_any_order_and_case(self):
        self.assertEqual({"source": "Regent", "type": "Attack"}, parse_bonus_card_pool("Regent Attack"))
        self.assertEqual({"rarity": "Rare", "upgraded": "Upgraded", "type": "Power"},
                         parse_bonus_card_pool("rare UPGRADED power"))
        self.assertEqual({"source": "Colorless"}, parse_bonus_card_pool("Colourless"))
        self.assertEqual({"random": "Random", "upgraded": "Upgraded"}, parse_bonus_card_pool("Random Upgraded"))

    def test_rejects_pools_the_client_cannot_honour(self):
        for pool in ("", "Legendary", "Regent Silent", "Attack Skill", "Random Rare", "Event Rare",
                     "Ancient Common", "Colorless Common"):
            with self.subTest(pool=pool):
                with self.assertRaises(ValueError):
                    parse_bonus_card_pool(pool)
                with self.assertRaises(OptionError):
                    validate_bonus_entry("CARDS", {"Pools": [pool]})

    def test_value_must_be_a_card_name(self):
        validate_bonus_entry("CARDS", {"Value": "Apotheosis"})
        with self.assertRaises(OptionError):
            validate_bonus_entry("CARDS", {"Value": 3})
