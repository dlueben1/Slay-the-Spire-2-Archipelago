from unittest import TestCase

from worlds.spire2 import SlayTheSpire2World
from worlds.spire2.items import chars_to_items, item_groups, item_table, universal_items
from worlds.spire2.locations import LocationType, characters_to_locs, location_groups, location_table


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
        self.assertEqual(
            {"Ironclad Progressive Rest", "Ironclad Progressive Smith"},
            item_groups["Ironclad Campfire Upgrades"],
        )
        self.assertEqual(
            {
                "Ironclad Shop Card Slot",
                "Ironclad Neutral Shop Card Slot",
                "Ironclad Shop Relic Slot",
                "Ironclad Shop Potion Slot",
                "Ironclad Progressive Shop Remove",
            },
            item_groups["Ironclad Shop Slots"],
        )

    def test_custom_character_group_names_match_item_names(self):
        custom_items = set(chars_to_items[1])
        for suffix in ("Gold", "Campfire Upgrades", "Shop Slots"):
            group_name = f"Custom Character 1 {suffix}"
            self.assertTrue(item_groups[group_name])
            self.assertTrue(all(name.startswith("Custom Character 1 ") for name in item_groups[group_name]))
            self.assertLessEqual(item_groups[group_name], custom_items)


class TestLocationGroups(TestCase):
    def test_world_exposes_location_groups(self):
        for group_name, locations in location_groups.items():
            self.assertEqual(SlayTheSpire2World.location_name_groups[group_name], locations)

    def test_location_groups_only_contain_network_locations(self):
        for group_name, locations in location_groups.items():
            with self.subTest(group=group_name):
                self.assertTrue(locations)
                self.assertLessEqual(locations, location_table.keys())
                self.assertTrue(all(location_table[location] is not None for location in locations))

    def test_act_groups_exclude_option_dependent_card_rewards(self):
        all_act_locations = location_groups["Act 1"] | location_groups["Act 2"] | location_groups["Act 3"]
        numbered_card_rewards = {
            name for character_locations in characters_to_locs.values()
            for name, data in character_locations.items() if data.type == LocationType.Card_Reward
        }
        unambiguous_locations = {
            name for name, location_id in location_table.items()
            if location_id is not None and name not in numbered_card_rewards
        }
        self.assertEqual(unambiguous_locations, all_act_locations)
        self.assertNotIn("Ironclad Card Reward 1", all_act_locations)
        self.assertIn("Ironclad Ancient Act 2", location_groups["Act 1"])
        self.assertIn("Ironclad Ancient Act 3", location_groups["Act 2"])
        self.assertNotIn("Ironclad Ancient Act 3", location_groups["Act 3"])

    def test_boss_groups_match_transition_rewards(self):
        self.assertEqual(
            {
                "Ironclad Rare Card Reward 1",
                "Ironclad Boss Gold 1",
                "Ironclad Ancient Act 2",
            },
            {name for name in location_groups["Ironclad Act 1 Boss"]},
        )
        self.assertEqual(
            {
                "Ironclad Rare Card Reward 2",
                "Ironclad Boss Gold 2",
                "Ironclad Ancient Act 3",
            },
            {name for name in location_groups["Ironclad Act 2 Boss"]},
        )

    def test_custom_character_group_names_match_location_names(self):
        custom_locations = {
            name for name, data in characters_to_locs[1].items() if data.id is not None
        }
        for suffix in ("Act 1", "Act 2", "Act 3", "Act 1 Boss", "Act 2 Boss"):
            group_name = f"Custom Character 1 {suffix}"
            self.assertTrue(location_groups[group_name])
            self.assertTrue(all(name.startswith("Custom Character 1 ") for name in location_groups[group_name]))
            self.assertLessEqual(location_groups[group_name], custom_locations)
