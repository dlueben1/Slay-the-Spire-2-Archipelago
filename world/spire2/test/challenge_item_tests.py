from test.general import setup_solo_multiworld
from worlds.AutoWorld import call_all
from worlds.spire2 import SlayTheSpire2World
from worlds.spire2.items import challenge_item_table
from worlds.spire2.test import Spire2TestBase


class TestHiddenIntents(Spire2TestBase):
    options = {
        "characters": ["ironclad", "silent"],
        "hidden_intents": 1,
    }

    def test_one_universal_intent_sight_is_added(self):
        sights = [item for item in self.multiworld.itempool if item.name == "Intent Sight"]
        self.assertEqual(1, len(sights))
        self.assertEqual({800}, {item.code for item in sights})
        self.assertEqual(800, challenge_item_table["Intent Sight"].code)
        self.assertTrue(all(item.useful and not item.advancement for item in sights))

    def test_intent_sights_replace_filler_without_changing_pool_size(self):
        locations = [location for location in self.multiworld.get_locations()
                     if location.address is not None and location.item is None]
        self.assertEqual(len(locations), len(self.multiworld.itempool))

    def test_option_is_sent_in_slot_data(self):
        self.assertEqual(1, self.world.fill_slot_data()["hidden_intents"])


class TestHiddenOptionsNeedFloorChecks(Spire2TestBase):
    options = {
        "characters": ["ironclad", "silent"],
        "include_floor_checks": False,
        "hidden_intents": 2,
        "hidden_health": 1,
        "hidden_piles": 2,
        "hidden_map": 1,
        "hidden_cards": 2,
    }

    def test_no_sight_items_are_added(self):
        self.assertFalse(any(item.name.endswith(" Sight") for item in self.multiworld.itempool))

    def test_challenges_are_off_in_slot_data(self):
        slot_data = self.world.fill_slot_data()
        for option in ("hidden_intents", "hidden_health", "hidden_piles", "hidden_map", "hidden_cards"):
            self.assertEqual(0, slot_data[option])

    def test_tracker_ignores_hidden_values_from_the_yaml(self):
        regenerated = setup_solo_multiworld(SlayTheSpire2World, steps=())
        # Universal Tracker attaches this extension to MultiWorld dynamically.
        # noinspection PyUnresolvedReferences
        regenerated.re_gen_passthrough = {self.game: self.world.fill_slot_data()}
        tracker_options = regenerated.worlds[1].options
        tracker_options.hidden_intents.value = 2
        tracker_options.hidden_cards.value = 2
        for step in ("generate_early", "create_regions", "create_items", "set_rules"):
            call_all(regenerated, step)
        self.assertFalse(any(item.name.endswith(" Sight") for item in regenerated.itempool))


class TestChallengeItemsOff(Spire2TestBase):
    def test_no_sight_items_by_default(self):
        self.assertFalse(any(item.name.endswith(" Sight") for item in self.multiworld.itempool))
        slot_data = self.world.fill_slot_data()
        self.assertEqual(0, slot_data["hidden_intents"])
        self.assertEqual(0, slot_data["hidden_health"])
        self.assertEqual(0, slot_data["hidden_piles"])
        self.assertEqual(0, slot_data["hidden_map"])
        self.assertEqual(0, slot_data["hidden_cards"])


class TestHiddenHealth(Spire2TestBase):
    options = {
        "characters": ["ironclad", "silent"],
        "hidden_intents": 1,
        "hidden_health": 1,
    }

    def test_one_universal_health_sight_is_added(self):
        sights = [item for item in self.multiworld.itempool if item.name == "Health Sight"]
        self.assertEqual(1, len(sights))
        self.assertEqual({801}, {item.code for item in sights})
        self.assertTrue(all(item.useful and not item.advancement for item in sights))
        self.assertEqual(1, sum(item.name == "Intent Sight" for item in self.multiworld.itempool))

    def test_health_sights_replace_filler_without_changing_pool_size(self):
        locations = [location for location in self.multiworld.get_locations()
                     if location.address is not None and location.item is None]
        self.assertEqual(len(locations), len(self.multiworld.itempool))

    def test_option_is_sent_in_slot_data(self):
        self.assertEqual(1, self.world.fill_slot_data()["hidden_health"])


class TestHiddenPiles(Spire2TestBase):
    options = {
        "characters": ["ironclad", "silent"],
        "hidden_piles": 1,
    }

    def test_one_universal_pile_sight_is_added(self):
        sights = [item for item in self.multiworld.itempool if item.name == "Pile Sight"]
        self.assertEqual(1, len(sights))
        self.assertEqual({802}, {item.code for item in sights})
        self.assertTrue(all(item.useful and not item.advancement for item in sights))

    def test_pile_sights_replace_filler_without_changing_pool_size(self):
        locations = [location for location in self.multiworld.get_locations()
                     if location.address is not None and location.item is None]
        self.assertEqual(len(locations), len(self.multiworld.itempool))

    def test_option_is_sent_in_slot_data(self):
        self.assertEqual(1, self.world.fill_slot_data()["hidden_piles"])


class TestHiddenMap(Spire2TestBase):
    options = {
        "characters": ["ironclad", "silent"],
        "hidden_map": 1,
    }

    def test_one_universal_map_sight_is_added(self):
        sights = [item for item in self.multiworld.itempool if item.name == "Map Sight"]
        self.assertEqual(1, len(sights))
        self.assertEqual({803}, {item.code for item in sights})
        self.assertTrue(all(item.useful and not item.advancement for item in sights))

    def test_map_sights_replace_filler_without_changing_pool_size(self):
        locations = [location for location in self.multiworld.get_locations()
                     if location.address is not None and location.item is None]
        self.assertEqual(len(locations), len(self.multiworld.itempool))

    def test_option_is_sent_in_slot_data(self):
        self.assertEqual(1, self.world.fill_slot_data()["hidden_map"])


class TestHiddenCards(Spire2TestBase):
    options = {
        "characters": ["ironclad", "silent"],
        "hidden_cards": 1,
    }

    def test_one_universal_card_sight_is_added(self):
        sights = [item for item in self.multiworld.itempool if item.name == "Card Sight"]
        self.assertEqual(1, len(sights))
        self.assertEqual({804}, {item.code for item in sights})
        self.assertTrue(all(item.useful and not item.advancement for item in sights))

    def test_card_sights_replace_filler_without_changing_pool_size(self):
        locations = [location for location in self.multiworld.get_locations()
                     if location.address is not None and location.item is None]
        self.assertEqual(len(locations), len(self.multiworld.itempool))

    def test_option_is_sent_in_slot_data(self):
        self.assertEqual(1, self.world.fill_slot_data()["hidden_cards"])


class TestTwoSightCopies(Spire2TestBase):
    options = {
        "characters": ["ironclad", "silent"],
        "hidden_intents": 2,
        "hidden_health": 2,
        "hidden_piles": 2,
        "hidden_map": 2,
        "hidden_cards": 2,
    }

    def test_two_copies_of_each_sight_are_added(self):
        for name in ("Intent Sight", "Health Sight", "Pile Sight", "Map Sight", "Card Sight"):
            self.assertEqual(2, sum(item.name == name for item in self.multiworld.itempool))

    def test_copy_counts_are_sent_in_slot_data(self):
        slot_data = self.world.fill_slot_data()
        self.assertEqual(2, slot_data["hidden_intents"])
        self.assertEqual(2, slot_data["hidden_health"])
        self.assertEqual(2, slot_data["hidden_piles"])
        self.assertEqual(2, slot_data["hidden_map"])
        self.assertEqual(2, slot_data["hidden_cards"])
