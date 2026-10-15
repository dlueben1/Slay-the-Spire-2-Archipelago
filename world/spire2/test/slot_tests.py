import unittest

from BaseClasses import CollectionState
from Fill import distribute_items_restrictive
from Generate import roll_settings
from test.general import setup_multiworld, setup_solo_multiworld
from worlds.AutoWorld import call_all
from worlds.spire2 import SlayTheSpire2World


class TestSlotGeneration(unittest.TestCase):
    roster = ["Ironclad", "Silent", "Defect", "Regent"]

    def generate(self, **options):
        return setup_multiworld(SlayTheSpire2World, seed=42, options={
            "characters": self.roster, **options})

    def test_obsolete_player_count_cannot_change_generation(self):
        self.assertNotIn("player_count", SlayTheSpire2World.options_dataclass.type_hints)
        baseline = self.generate()
        for count in (1, 2, 4, 100):
            with self.subTest(count=count):
                # Exercise actual YAML option rolling as well as world setup.
                rolled = roll_settings({"game": SlayTheSpire2World.game,
                                        SlayTheSpire2World.game: {"player_count": count}})
                self.assertFalse(hasattr(rolled, "player_count"))
                mw = self.generate(player_count=count)
                self.assertEqual([(loc.name, loc.address) for loc in baseline.get_locations()],
                                 [(loc.name, loc.address) for loc in mw.get_locations()])
                self.assertEqual([(item.name, item.code) for item in baseline.itempool],
                                 [(item.name, item.code) for item in mw.itempool])
                data = mw.worlds[1].fill_slot_data()
                self.assertNotIn("player_count", data)
                self.assertNotIn("players", data)
                self.assertTrue(all("player_number" not in char for char in data["characters"]))
        for catalog in (SlayTheSpire2World.item_name_to_id, SlayTheSpire2World.location_name_to_id):
            self.assertFalse(any(name.startswith(("P2 ", "P3 ", "P4 ")) for name in catalog))

    def test_roster_selection_and_starting_unlocks(self):
        for advanced in (False, True):
            for locks in (0, 1, 2):
                with self.subTest(advanced=advanced, locks=locks):
                    options = dict(use_advanced_characters=advanced,
                                   advanced_characters={name: {"ascension": [1]} for name in self.roster},
                                   pick_num_characters=2, lock_characters=locks, unlocked_character="Silent")
                    mw = self.generate(**options)
                    world = mw.worlds[1]
                    self.assertEqual(2, len(world.characters))
                    self.assertEqual(world.fill_slot_data(), self.generate(**options).worlds[1].fill_slot_data())
                    unlocked = [c.name for c in world.characters if not c.locked]
                    self.assertEqual(2 if locks == 0 else 1, len(unlocked))
                    if locks == 2:
                        self.assertEqual(["Silent"], unlocked)
                    for config in world.characters:
                        self.assertEqual(int(not config.locked), sum(
                            item.name == f"{config.name} Unlock" for item in mw.precollected_items[1]))

    def test_tracker_regeneration_preserves_roster_checks_and_modded_aliases(self):
        mw = self.generate(modded_characters=["ModA", "ModB"], pick_num_characters=0)
        data = mw.worlds[1].fill_slot_data()
        regenerated = setup_solo_multiworld(SlayTheSpire2World, steps=())
        # Universal Tracker attaches this extension to MultiWorld dynamically.
        # noinspection PyUnresolvedReferences
        regenerated.re_gen_passthrough = {SlayTheSpire2World.game: data}
        for step in ("generate_early", "create_regions", "create_items", "set_rules"):
            call_all(regenerated, step)
        world = regenerated.worlds[1]
        assert isinstance(world, SlayTheSpire2World)
        self.assertEqual(data["characters"], world.fill_slot_data()["characters"])
        self.assertEqual({(loc.name, loc.address) for loc in mw.get_locations()},
                         {(loc.name, loc.address) for loc in regenerated.get_locations()})
        for config in world.modded_chars:
            location = world.get_location(f"{config.name} Reached Floor 1")
            assert location.address is not None
            self.assertEqual(f"{config.official_name} Reached Floor 1",
                             world.location_id_to_alias[location.address])

        # Sort the same labels UT builds, including its appended modded-character aliases.
        labels = []
        for config in world.characters:
            for suffix in ("Reached Floor 1", "Reached Floor 7", "Reached Floor 9",
                           "Reached Floor 10", "Reached Floor 12", "Act 1 Boss", "Reached Floor 18"):
                location = world.get_location(f"{config.name} {suffix}")
                label = location.name
                if location.address in world.location_id_to_alias:
                    label += f" ({world.location_id_to_alias[location.address]})"
                labels.append((location.parent_region.name, label))
        self.assertEqual(labels, sorted(reversed(labels), key=lambda pair: world.custom_ut_sort(*pair)))

    def test_separate_ap_slots_keep_independent_progress_and_fill(self):
        options = {"characters": ["Ironclad", "Silent"], "num_chars_goal": 1,
                   "lock_characters": 2, "unlocked_character": "Ironclad",
                   "shop_sanity": 1, "campfire_sanity": 1, "gold_sanity": 1,
                   "potion_sanity": 1, "neow_sanity": 1}
        mw = setup_multiworld([SlayTheSpire2World] * 2, seed=42, options=options)
        state = CollectionState(mw)
        state.collect(mw.worlds[1].create_item("Ironclad Relic"), prevent_sweep=True)
        self.assertEqual(1, state.count("Ironclad Relic", 1))
        self.assertEqual(0, state.count("Ironclad Relic", 2))
        state.collect(mw.worlds[1].create_item("Ironclad Victory"), prevent_sweep=True)
        self.assertTrue(mw.completion_condition[1](state))
        self.assertFalse(mw.completion_condition[2](state))
        full_state = mw.get_all_state(False)
        self.assertTrue(all(loc.can_reach(full_state) for loc in mw.get_locations()))
        distribute_items_restrictive(mw, panic_method="raise")
        self.assertFalse(mw.get_unfilled_locations())
        self.assertTrue(mw.can_beat_game())

    def test_floor_checks_do_not_request_opening_items(self):
        mw = self.generate(include_floor_checks=True, campfire_sanity=True, neow_sanity=True)
        self.assertFalse(mw.early_items[1])
