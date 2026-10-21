import unittest

from BaseClasses import CollectionState
from test.general import setup_multiworld
from worlds.AutoWorld import call_all
from worlds.spire2 import SlayTheSpire2World
from worlds.spire2.logic_table import ELITES_PER_ACT, REGIONS, RELIC_REGIONS, last_elite
from worlds.spire2.regions import LEGACY_RELIC_REGIONS
from worlds.spire2.test import path_power


class TwoTrackTests(unittest.TestCase):
    # Gold sanity keeps floorless seeds on new logic; without it they fall back to old logic.
    options = {"characters": ["Ironclad"], "gold_sanity": True}

    def make_world(self, **options) -> SlayTheSpire2World:
        world = setup_multiworld(SlayTheSpire2World, seed=42,
                                 options={**self.options, **options}).worlds[1]
        assert isinstance(world, SlayTheSpire2World)
        return world

    def test_relic_checks_follow_each_logic_track(self):
        for floors, logic, placement in ((True, "new", RELIC_REGIONS),
                                         (False, "old", LEGACY_RELIC_REGIONS)):
            world = self.make_world(include_floor_checks=floors, gold_sanity=floors)
            self.assertEqual(logic, world.effective_logic)
            for number, region in placement.items():
                with self.subTest(logic=logic, relic=number):
                    location = world.get_location(f"Ironclad Relic {number}")
                    self.assertEqual(f"Ironclad {region}", location.parent_region.name)
            elite_track = {f"Ironclad {region}" for region in RELIC_REGIONS.values() if region != "Mid Act 1"}
            self.assertEqual(elite_track if logic == "new" else set(),
                             elite_track & {region.name for region in world.get_regions()})

    def test_elite_track_follows_every_earlier_elite(self):
        world = self.make_world()
        for act in (2, 3):
            for region in (f"Act {act} Chest", f"Act {act} Elite 1"):
                with self.subTest(region=region):
                    entrance = world.get_entrance(f"Ironclad {region}")
                    self.assertEqual(f"Ironclad Mid Act {act}", entrance.parent_region.name)
                    self.assertIn(f"Ironclad {last_elite(act - 1)}", entrance.access_rule.region_dependencies())
        for act, elites in enumerate(ELITES_PER_ACT, start=1):
            for number in range(2, elites + 1):
                with self.subTest(act=act, elite=number):
                    entrance = world.get_entrance(f"Ironclad Act {act} Elite {number}")
                    self.assertEqual(f"Ironclad Act {act} Elite {number - 1}", entrance.parent_region.name)

    def test_path_checks_do_not_wait_for_elite_power(self):
        world = self.make_world()
        state = CollectionState(world.multiworld)
        # Half shuffle's vanilla cards plus one Card Reward clear Mid Act 1, not the first Act 1 elite.
        state.collect(world.create_item("Ironclad Card Reward"), prevent_sweep=True)
        self.assertTrue(world.get_location("Ironclad Reached Floor 11").can_reach(state))
        self.assertTrue(world.get_location("Ironclad Relic 1").can_reach(state))
        self.assertFalse(world.get_location("Ironclad Relic 2").can_reach(state))
        for _ in range(3):
            state.collect(world.create_item("Ironclad Relic"), prevent_sweep=True)
        self.assertTrue(world.get_location("Ironclad Relic 2").can_reach(state))

    def test_seeded_runs_keep_elite_inclusive_act_1_power(self):
        for seeded in (False, True):
            world = self.make_world(seeded=seeded)
            for region in ("Mid Act 1", "Late Act 1"):
                with self.subTest(seeded=seeded, region=region):
                    rule = path_power(world.get_entrance(f"Ironclad {region}").access_rule)
                    self.assertEqual(REGIONS[region].power_for(seeded), rule.power_level)
                    self.assertEqual(seeded, rule.power_level == REGIONS[region].seeded_power)

    def test_tracker_regeneration_keeps_seeded_power(self):
        world = self.make_world(seeded=True)
        regenerated = setup_multiworld(SlayTheSpire2World, seed=42, steps=())
        regenerated.re_gen_passthrough = {SlayTheSpire2World.game: world.fill_slot_data()}
        for step in ("generate_early", "create_regions", "create_items", "set_rules"):
            call_all(regenerated, step)
        restored = regenerated.worlds[1]
        for region in ("Mid Act 1", "Late Act 1"):
            with self.subTest(region=region):
                self.assertEqual(path_power(world.get_entrance(f"Ironclad {region}").access_rule).power_level,
                                 path_power(restored.get_entrance(f"Ironclad {region}").access_rule).power_level)
