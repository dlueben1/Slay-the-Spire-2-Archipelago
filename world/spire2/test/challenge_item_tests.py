from worlds.spire2.items import challenge_item_table
from worlds.spire2.test import Spire2TestBase


class TestHiddenIntents(Spire2TestBase):
    options = {
        "characters": ["ironclad", "silent"],
        "hidden_intents": 1,
    }

    def test_one_universal_intent_sight_is_added(self):
        sights = [item for item in self.multiworld.itempool if item.name == "Progressive Intent Sight"]
        self.assertEqual(1, len(sights))
        self.assertEqual({800}, {item.code for item in sights})
        self.assertEqual(800, challenge_item_table["Progressive Intent Sight"].code)
        self.assertTrue(all(item.useful and not item.advancement for item in sights))

    def test_intent_sights_replace_filler_without_changing_pool_size(self):
        locations = [location for location in self.multiworld.get_locations()
                     if location.address is not None and location.item is None]
        self.assertEqual(len(locations), len(self.multiworld.itempool))

    def test_option_is_sent_in_slot_data(self):
        self.assertEqual(1, self.world.fill_slot_data()["hidden_intents"])


class TestChallengeItemsOff(Spire2TestBase):
    def test_no_sight_items_by_default(self):
        self.assertFalse(any(item.name.endswith(" Sight") for item in self.multiworld.itempool))
        slot_data = self.world.fill_slot_data()
        self.assertEqual(0, slot_data["hidden_intents"])
        self.assertEqual(0, slot_data["hidden_health"])


class TestHiddenHealth(Spire2TestBase):
    options = {
        "characters": ["ironclad", "silent"],
        "hidden_intents": 1,
        "hidden_health": 1,
    }

    def test_one_universal_health_sight_is_added(self):
        sights = [item for item in self.multiworld.itempool if item.name == "Progressive Health Sight"]
        self.assertEqual(1, len(sights))
        self.assertEqual({801}, {item.code for item in sights})
        self.assertTrue(all(item.useful and not item.advancement for item in sights))
        self.assertEqual(1, sum(item.name == "Progressive Intent Sight" for item in self.multiworld.itempool))

    def test_health_sights_replace_filler_without_changing_pool_size(self):
        locations = [location for location in self.multiworld.get_locations()
                     if location.address is not None and location.item is None]
        self.assertEqual(len(locations), len(self.multiworld.itempool))

    def test_option_is_sent_in_slot_data(self):
        self.assertEqual(1, self.world.fill_slot_data()["hidden_health"])
