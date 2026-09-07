import unittest

from BaseClasses import CollectionState
from Fill import distribute_items_restrictive
from Options import OptionError
from test.general import setup_solo_multiworld, setup_multiworld
from worlds.AutoWorld import call_all
from worlds.spire2 import SlayTheSpire2World
from worlds.spire2.coop import PLAYER_OFFSET, player_name


class TestCoopGeneration(unittest.TestCase):
    roster = ["Ironclad", "Silent", "Defect", "Regent"]

    def generate(self, count=4, seed=42, **options):
        return setup_multiworld(SlayTheSpire2World, seed=seed, options={
            "characters": self.roster, "player_count": count, **options})

    def test_player_counts_multiply_checks_and_items(self):
        for locks in (0, 1, 2):
            baseline = self.generate(1, lock_characters=locks)
            baseline_checks = len([loc for loc in baseline.get_locations(1) if loc.address is not None])
            for count in (1, 2, 3, 4):
                with self.subTest(count=count, locks=locks):
                    mw = self.generate(count, lock_characters=locks)
                    checks = [loc for loc in mw.get_locations(1) if loc.address is not None]
                    self.assertEqual(baseline_checks * count, len(checks))
                    self.assertEqual(len(checks), len(mw.itempool))
                    self.assertEqual(len(checks), len({loc.address for loc in checks}))
                    for number in range(1, count + 1):
                        self.assertEqual(baseline_checks, sum(loc.address // PLAYER_OFFSET + 1 == number for loc in checks))

    def test_starts_are_seeded_distinct_and_fixed_start_is_preserved(self):
        for mode in (1, 2):
            worlds = [self.generate(lock_characters=mode, unlocked_character="Silent").worlds[1] for _ in range(2)]
            starts = [[next(c.name for c in configs if not c.locked)
                       for configs in world.player_characters.values()] for world in worlds]
            self.assertEqual(starts[0], starts[1])
            self.assertEqual(4, len(set(starts[0])))
            if mode == 2:
                self.assertEqual("Silent", starts[0][0])

    def test_unlocked_and_advanced_rosters(self):
        mw = self.generate(lock_characters=0, use_advanced_characters=1,
                           advanced_characters={name: {"ascension": [1]} for name in self.roster})
        self.assertTrue(all(not c.locked for c in mw.worlds[1].all_player_characters))
        self.assertFalse(any("Press Start" in loc.name for loc in mw.get_locations(1)))

    def test_insufficient_roster_or_pick_count_fails(self):
        for options in ({"characters": ["Ironclad"]}, {"pick_num_characters": 3}):
            with self.subTest(options=options), self.assertRaises(OptionError):
                self.generate(**options)

    def test_logic_is_independent_and_all_players_must_goal(self):
        mw = self.generate(num_chars_goal=1, lock_characters=2, unlocked_character="Ironclad")
        world = mw.worlds[1]
        state = CollectionState(mw)
        self.assertTrue(world.get_entrance("Ironclad Early Act 1").can_reach(state))
        self.assertFalse(world.get_entrance("P2 Ironclad Early Act 1").can_reach(state))
        state.collect(world.create_item("P2 Ironclad Unlock"), prevent_sweep=True)
        self.assertTrue(world.get_entrance("P2 Ironclad Early Act 1").can_reach(state))
        for _ in range(3):
            state.collect(world.create_item("Ironclad Relic"), prevent_sweep=True)
        self.assertEqual(4.5, state.power_level[1][1])
        self.assertEqual(0, state.power_level[1][101])
        state.collect(world.create_item("P2 Ironclad Relic"), prevent_sweep=True)
        self.assertEqual(1.5, state.power_level[1][101])
        state.remove(world.create_item("P2 Ironclad Relic"))
        self.assertEqual(0, state.power_level[1][101])
        for number in range(1, 5):
            self.assertFalse(mw.completion_condition[1](state))
            state.collect(world.create_item(player_name("Ironclad Victory", number)), prevent_sweep=True)
        self.assertTrue(mw.completion_condition[1](state))

    def test_tracker_regeneration_keeps_player_rosters_and_checks(self):
        mw = self.generate(pick_num_characters=4)
        slot_data = mw.worlds[1].fill_slot_data()
        regenerated = setup_solo_multiworld(SlayTheSpire2World, steps=())
        regenerated.re_gen_passthrough = {SlayTheSpire2World.game: slot_data}
        for step in ("generate_early", "create_regions", "create_items", "set_rules"):
            call_all(regenerated, step)
        self.assertEqual({(loc.name, loc.address) for loc in mw.get_locations(1)},
                         {(loc.name, loc.address) for loc in regenerated.get_locations(1)})
        self.assertEqual(slot_data['players'], regenerated.worlds[1].fill_slot_data()['players'])

    def test_full_inventory_reaches_every_player(self):
        mw = self.generate(shop_sanity=1, campfire_sanity=1, gold_sanity=1, potion_sanity=1,
                           neow_sanity=1, progressive_starter_card=1, progressive_starter_relic=1)
        state = mw.get_all_state(False)
        self.assertTrue(mw.completion_condition[1](state))
        self.assertTrue(all(loc.can_reach(state) for loc in mw.get_locations(1)))

    def test_modded_roster_uses_the_same_character_offsets_for_every_player(self):
        mw = self.generate(characters=["Ironclad", "Silent"], modded_characters=["ModA", "ModB"])
        world = mw.worlds[1]
        expected = [(c.name, c.char_offset, c.mod_num) for c in world.characters]
        for configs in world.player_characters.values():
            self.assertEqual(expected, [(c.name, c.char_offset, c.mod_num) for c in configs])
        self.assertEqual(3_060_003, world.create_item("P4 Custom Character 1 Relic").code)

    def test_restrictive_fill_is_beatable_for_four_independent_players(self):
        for seed in (42, 84):
            with self.subTest(seed=seed):
                mw = self.generate(seed=seed, shop_sanity=1, campfire_sanity=1,
                                   gold_sanity=1, potion_sanity=1, neow_sanity=1)
                distribute_items_restrictive(mw, panic_method="raise")
                self.assertFalse(mw.get_unfilled_locations())
                self.assertTrue(mw.can_beat_game())
