import unittest

from BaseClasses import CollectionState
from test.general import setup_multiworld
from worlds.AutoWorld import call_all
from worlds.spire2 import SlayTheSpire2World
from worlds.spire2.logic_table import REGIONS, RELIC_REGIONS
from worlds.spire2.regions import LEGACY_RELIC_REGIONS


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
            elite_regions = {region.name for region in world.get_regions() if region.name.endswith("Elites")}
            self.assertEqual(logic == "new", bool(elite_regions))

    def test_later_elites_need_the_previous_acts_elites(self):
        world = self.make_world()
        for act in (2, 3):
            with self.subTest(act=act):
                rule = world.get_entrance(f"Ironclad Act {act} Elites").access_rule
                self.assertIn(f"Ironclad Act {act - 1} Elites", rule.region_dependencies())

    def test_path_checks_do_not_wait_for_elite_power(self):
        world = self.make_world()
        state = CollectionState(world.multiworld)
        # Half shuffle's vanilla cards plus one Card Reward clear Mid Act 1, not Act 1 Elites.
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
                    rule = world.get_entrance(f"Ironclad {region}").access_rule
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
                self.assertEqual(world.get_entrance(f"Ironclad {region}").access_rule.power_level,
                                 restored.get_entrance(f"Ironclad {region}").access_rule.power_level)
