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


class TestRelicChoiceCountDefault(Spire2TestBase):
    def test_one_choice_by_default(self):
        self.assertEqual(1, self.world.options.relic_choice_count.value)
        self.assertEqual(1, self.world.fill_slot_data()["relic_choice_count"])


class TestRelicChoiceCountConfigured(Spire2TestBase):
    options = {
        "relic_choice_count": 5,
    }

    def test_configured_count_is_sent_in_slot_data(self):
        self.assertEqual(5, self.world.options.relic_choice_count.value)
        self.assertEqual(5, self.world.fill_slot_data()["relic_choice_count"])


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
