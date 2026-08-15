from worlds.spire2.options import CharacterOptions
from worlds.spire2.test import Spire2TestBase


class TestMultiCharsValid(Spire2TestBase):

    options = {
        "characters": [
            "ironclad",
            "silent",
        ]
    }

    def test_valid(self):
        CharacterOptions.schema.validate(self.world.options.advanced_characters.value)

class Test49Floors(Spire2TestBase):
    options = {
        "characters": [
            "ironclad",
            "silent",
        ],
        "ascension": 10
    }

    def test_ensure_floor_49(self):
        self.assertIsNotNone(self.world.get_location("Ironclad Reached Floor 49"))

    def test_no_dupes(self):
        stuff = dict()

        for location in self.world.get_locations():
            if location.address is None:
                continue
            self.assertTrue(location.address not in stuff, f"location duplicated {location.name} {stuff.get(location.address, None)}")
            stuff[location.address] = location

class Test48Floors(Spire2TestBase):
    options = {
        "characters": [
            "ironclad",
            "silent",
        ],
        "ascension": [9]
    }

    def test_no_floor_49(self):
        self.assertFalse( "Ironclad Reached Floor 49" in self.world.get_locations())


class TestAncientRelicOptionsDefault(Spire2TestBase):
    def test_anytime_and_balanced_by_default(self):
        slot_data = self.world.fill_slot_data()
        self.assertEqual(1, self.world.options.ancient_relic_location.value)
        self.assertEqual(1, slot_data["ancient_relic_location"])
        self.assertEqual(0, self.world.options.ancient_relic_pool.value)
        self.assertEqual(0, slot_data["ancient_relic_pool"])


class TestAncientRelicOptionsAnytimeChaos(Spire2TestBase):
    options = {
        "ancient_relic_location": 1,
        "ancient_relic_pool": 1,
    }

    def test_anytime_and_chaos_values_are_sent_in_slot_data(self):
        slot_data = self.world.fill_slot_data()
        self.assertEqual(1, self.world.options.ancient_relic_location.value)
        self.assertEqual(1, slot_data["ancient_relic_location"])
        self.assertEqual(1, self.world.options.ancient_relic_pool.value)
        self.assertEqual(1, slot_data["ancient_relic_pool"])


class TestAncientRelicOptionsTrueChaos(Spire2TestBase):
    options = {
        "ancient_relic_pool": 2,
    }

    def test_true_chaos_value_is_sent_in_slot_data(self):
        self.assertEqual(2, self.world.options.ancient_relic_pool.value)
        self.assertEqual(2, self.world.fill_slot_data()["ancient_relic_pool"])


class TestProgressiveRelicOptionsDefault(Spire2TestBase):
    def test_defaults_are_sent_in_slot_data(self):
        slot_data = self.world.fill_slot_data()
        self.assertEqual(2, self.world.options.relic_rewards_available_anytime.value)
        self.assertEqual(2, slot_data["relic_rewards_available_anytime"])
        self.assertEqual(1, self.world.options.release_on_victory.value)
        self.assertEqual(1, slot_data["release_on_victory"])


class TestProgressiveRelicOptionsConfigured(Spire2TestBase):
    options = {
        "relic_rewards_available_anytime": 7,
        "release_on_victory": 0,
    }

    def test_configured_values_are_sent_in_slot_data(self):
        slot_data = self.world.fill_slot_data()
        self.assertEqual(7, self.world.options.relic_rewards_available_anytime.value)
        self.assertEqual(7, slot_data["relic_rewards_available_anytime"])
        self.assertEqual(0, self.world.options.release_on_victory.value)
        self.assertEqual(0, slot_data["release_on_victory"])


class TestAscensionDowns(Spire2TestBase):
    options = {
        "characters": [
            "silent"
        ],
        "ascension": [9],
        "ascension_down": [3],
    }

    def test_high_ascension_downs_shuffled(self):
        for item in self.world.multiworld.itempool:
            if item.name == "Silent Disable Scarcity":
                break
        else:
            raise Exception("Failed to find ascension down")

class TestAscensionDownNumbers(Spire2TestBase):
    options = {
        "characters": [
            "silent"
        ],
        "ascension": ["10"],
        "ascension_down": ["10","9", "8"],
    }

    def test_has_double_boss(self):
        for item in self.world.multiworld.itempool:
            if item.name == "Silent Disable Double Boss":
                break
        else:
            raise Exception("Failed to find Double Boss")

    def test_no_swarming_elites(self):
        for item in self.world.multiworld.itempool:
            if 'Swarming Elites' in item.name:
                raise Exception("Found Swarming Elites")


class TestBasicModdedChars(Spire2TestBase):
    options = {
        "characters": {
            "Ironclad",
            "Silent"
        },
        "modded_characters": ["WATCHER-WATCHER"]
    }


class TestProgressiveStartersDisabled(Spire2TestBase):
    def test_no_progressive_starters(self):
        names = [item.name for item in self.world.multiworld.itempool]
        self.assertNotIn("Ironclad Progressive Starter Card", names)
        self.assertNotIn("Ironclad Progressive Starter Relic", names)


class TestProgressiveStartersEnabled(Spire2TestBase):
    options = {
        "characters": ["ironclad", "silent"],
        "progressive_starter_card": True,
        "progressive_starter_relic": True,
    }

    def test_two_progressive_starters_per_character(self):
        names = [item.name for item in self.world.multiworld.itempool]
        for character in ("Ironclad", "Silent"):
            self.assertEqual(names.count(f"{character} Progressive Starter Card"), 2)
            self.assertEqual(names.count(f"{character} Progressive Starter Relic"), 2)

    def test_normal_reward_counts_are_preserved(self):
        names = [item.name for item in self.world.multiworld.itempool]
        for character in ("Ironclad", "Silent"):
            self.assertEqual(names.count(f"{character} Card Reward"), 10)
            self.assertEqual(names.count(f"{character} Relic"), 10)

    def test_progressive_starters_are_sent_to_the_client(self):
        slot_data = self.world.fill_slot_data()
        self.assertEqual(slot_data["progressive_starter_card"], 1)
        self.assertEqual(slot_data["progressive_starter_relic"], 1)


class TestProgressiveStartersRequireFloorChecks(Spire2TestBase):
    options = {
        "progressive_starter_card": True,
        "progressive_starter_relic": True,
        "include_floor_checks": False,
    }

    def test_progressive_starters_are_disabled(self):
        names = [item.name for item in self.world.multiworld.itempool]
        self.assertNotIn("Ironclad Progressive Starter Card", names)
        self.assertNotIn("Ironclad Progressive Starter Relic", names)

        slot_data = self.world.fill_slot_data()
        self.assertEqual(slot_data["progressive_starter_card"], 0)
        self.assertEqual(slot_data["progressive_starter_relic"], 0)


class TestEnsureShopWithNoGoldWorks(Spire2TestBase):
    options = {
        "shop_sanity": True,
        "gold_sanity": False,
    }