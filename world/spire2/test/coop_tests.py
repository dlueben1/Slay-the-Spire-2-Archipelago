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

    def test_each_player_gets_bonus_receipts_and_keeps_its_own_filler_budget(self):
        from collections import Counter
        for count in (1, 2, 3, 4):
            with self.subTest(count=count):
                baseline = self.generate(count)
                bonuses = [{"WAX_RELIC": {"Value": "THE_BOOT"}},
                           {"WAX_RELIC": {"Pools": ["Common"]}}]
                mw = self.generate(count, bonus_items=bonuses)
                self.assertEqual(bonuses, mw.worlds[1].fill_slot_data()["bonus_items"])
                for number in range(1, count + 1):
                    owned = [item for item in mw.itempool if item.code // PLAYER_OFFSET + 1 == number]
                    expected_count = sum(item.code // PLAYER_OFFSET + 1 == number for item in baseline.itempool)
                    self.assertEqual(expected_count, len(owned))
                    bonus = [item for item in owned if item.name == player_name("Bonus Wax Relic", number)]
                    self.assertEqual(2, len(bonus))
                    self.assertEqual({600 + (number - 1) * PLAYER_OFFSET}, {item.code for item in bonus})
                    # Every player's receipts must still match their own locations.
                    checks = [loc for loc in mw.get_locations(1) if loc.address is not None
                              and loc.address // PLAYER_OFFSET + 1 == number]
                    self.assertEqual(len(checks), len(owned))
                self.assertEqual(Counter(item.name for item in mw.itempool),
                                 Counter(item.name for item in self.generate(count, bonus_items=bonuses).itempool))

    def test_bonus_budget_is_checked_per_player(self):
        bonuses = [{"WAX_RELIC": {"Value": "THE_BOOT"}}] * 500
        with self.assertRaisesRegex(OptionError, "Player 1"):
            self.generate(4, bonus_items=bonuses)

    def test_starts_are_seeded_per_player_and_fixed_start_is_preserved_for_player_one(self):
        for mode in (1, 2):
            worlds = [self.generate(characters=[*self.roster, "Necrobinder"], pick_num_characters=2,
                                    lock_characters=mode, unlocked_character="Silent").worlds[1]
                      for _ in range(2)]
            starts = [[next(c.name for c in configs if not c.locked)
                       for configs in world.player_characters.values()] for world in worlds]
            self.assertEqual(starts[0], starts[1])
            self.assertTrue(all(sum(not config.locked for config in configs) == 1
                                for configs in worlds[0].player_characters.values()))
            if mode == 2:
                self.assertEqual("Silent", starts[0][0])
                self.assertIn("Silent", [config.name for config in worlds[0].player_characters[1]])

    def test_each_player_rolls_an_independent_roster(self):
        options = {
            "characters": [*self.roster, "Necrobinder"],
            "pick_num_characters": 2,
            "lock_characters": 1,
        }
        worlds = [self.generate(seed=42, **options).worlds[1] for _ in range(2)]
        rosters = [[tuple(config.option_name for config in configs)
                    for configs in world.player_characters.values()] for world in worlds]
        self.assertEqual(rosters[0], rosters[1])
        self.assertTrue(all(len(roster) == 2 for roster in rosters[0]))
        self.assertGreater(len(set(rosters[0])), 1)
        self.assertTrue(all(sum(not config.locked for config in configs) == 1
                            for configs in worlds[0].player_characters.values()))

    def test_unlocked_and_advanced_rosters(self):
        mw = self.generate(lock_characters=0, use_advanced_characters=1,
                           advanced_characters={name: {"ascension": [1]} for name in self.roster},
                           pick_num_characters=2)
        self.assertTrue(all(not c.locked for c in mw.worlds[1].all_player_characters))
        self.assertFalse(any("Press Start" in loc.name for loc in mw.get_locations(1)))
        rosters = [tuple(config.option_name for config in configs)
                   for configs in mw.worlds[1].player_characters.values()]
        self.assertTrue(all(len(roster) == 2 for roster in rosters))
        self.assertGreater(len(set(rosters)), 1)

    def test_roster_size_does_not_limit_player_count(self):
        for mode in (0, 1, 2):
            with self.subTest(mode=mode):
                mw = self.generate(characters=["Ironclad"], pick_num_characters=1,
                                   lock_characters=mode, unlocked_character="Ironclad")
                self.assertEqual(4, len(mw.worlds[1].player_characters))
                for configs in mw.worlds[1].player_characters.values():
                    self.assertEqual(["Ironclad"], [config.name for config in configs])
                    self.assertFalse(configs[0].locked)

    def test_logic_is_independent_and_all_players_must_goal(self):
        mw = self.generate(num_chars_goal=1, lock_characters=2, unlocked_character="Ironclad")
        world = mw.worlds[1]
        state = CollectionState(mw)
        self.assertTrue(world.get_entrance("Ironclad Early Act 1").can_reach(state))
        p2_locked = next(config for config in world.player_characters[2] if config.locked)
        self.assertFalse(world.get_entrance(f"{p2_locked.ap_name} Early Act 1").can_reach(state))
        state.collect(world.create_item(f"{p2_locked.ap_name} Unlock"), prevent_sweep=True)
        self.assertTrue(world.get_entrance(f"{p2_locked.ap_name} Early Act 1").can_reach(state))
        for _ in range(3):
            state.collect(world.create_item("Ironclad Relic"), prevent_sweep=True)
        self.assertEqual(4.5, state.power_level[1][1])
        self.assertEqual(0, state.power_level[1][p2_locked.power_key])
        state.collect(world.create_item(f"{p2_locked.ap_name} Relic"), prevent_sweep=True)
        self.assertEqual(1.5, state.power_level[1][p2_locked.power_key])
        state.remove(world.create_item(f"{p2_locked.ap_name} Relic"))
        self.assertEqual(0, state.power_level[1][p2_locked.power_key])
        for number in range(1, 5):
            self.assertFalse(mw.completion_condition[1](state))
            state.collect(world.create_item(player_name("Ironclad Victory", number)), prevent_sweep=True)
        self.assertTrue(mw.completion_condition[1](state))

    def test_tracker_regeneration_keeps_player_rosters_and_checks(self):
        mw = self.generate(pick_num_characters=2)
        slot_data = mw.worlds[1].fill_slot_data()
        self.assertEqual(slot_data['characters'], slot_data['players']['1'])
        regenerated = setup_solo_multiworld(SlayTheSpire2World, steps=())
        regenerated.re_gen_passthrough = {SlayTheSpire2World.game: slot_data}
        for step in ("generate_early", "create_regions", "create_items", "set_rules"):
            call_all(regenerated, step)
        self.assertEqual({(loc.name, loc.address) for loc in mw.get_locations(1)},
                         {(loc.name, loc.address) for loc in regenerated.get_locations(1)})
        self.assertEqual(slot_data['players'], regenerated.worlds[1].fill_slot_data()['players'])

    def test_tracker_regeneration_uses_each_players_modded_character_aliases(self):
        mw = self.generate(characters=["Ironclad", "Silent"],
                           modded_characters=["ModA", "ModB", "ModC"],
                           pick_num_characters=2)
        slot_data = mw.worlds[1].fill_slot_data()
        regenerated = setup_solo_multiworld(SlayTheSpire2World, steps=())
        regenerated.re_gen_passthrough = {SlayTheSpire2World.game: slot_data}
        for step in ("generate_early", "create_regions"):
            call_all(regenerated, step)
        world = regenerated.worlds[1]
        for configs in world.player_characters.values():
            for config in configs:
                if config.mod_num == 0:
                    continue
                location = world.get_location(f"{config.ap_name} Reached Floor 1")
                expected = player_name(f"{config.official_name} Reached Floor 1", config.player_number)
                self.assertEqual(expected, world.location_id_to_alias[location.address])

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
