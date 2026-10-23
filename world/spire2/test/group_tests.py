from unittest import TestCase

from worlds.spire2 import SlayTheSpire2World
from worlds.spire2.items import item_groups, item_table, universal_items


class TestItemGroups(TestCase):
    def test_world_exposes_item_groups(self):
        for group_name, items in item_groups.items():
            self.assertEqual(SlayTheSpire2World.item_name_groups[group_name], items)

    def test_item_groups_only_contain_network_items(self):
        for group_name, items in item_groups.items():
            with self.subTest(group=group_name):
                self.assertTrue(items)
                self.assertLessEqual(items, item_table.keys())
                self.assertTrue(all(item_table[item].code is not None for item in items))

    def test_umbrella_groups_include_specialized_items(self):
        self.assertIn("Ironclad Card Reward", item_groups["Card Rewards"])
        self.assertIn("Ironclad Rare Card Reward", item_groups["Card Rewards"])
        self.assertEqual(
            item_groups["Rare Card Rewards"],
            {name for name in item_table if name.endswith(" Rare Card Reward")},
        )
        self.assertEqual(item_groups["Buffs"], set(universal_items))
        self.assertLessEqual(
            {"Ironclad Progressive Rest", "Ironclad Progressive Smith"},
            item_groups["Campfire Upgrades"],
        )
        self.assertLessEqual(
            {
                "Ironclad Shop Card Slot",
                "Ironclad Neutral Shop Card Slot",
                "Ironclad Shop Relic Slot",
                "Ironclad Shop Potion Slot",
                "Ironclad Progressive Shop Remove",
            },
            item_groups["Shop Slots"],
        )
        self.assertIn("Custom Character 1 Progressive Rest", item_groups["Campfire Upgrades"])
