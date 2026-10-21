import unittest

from BaseClasses import CollectionState
from test.general import setup_multiworld
from worlds.AutoWorld import call_all
from worlds.spire2 import SlayTheSpire2World
from worlds.spire2.logic_table import ELITE_RELIC_REWARDS, PATH_RELIC_REWARDS, RELIC_POWER
from worlds.spire2.test import path_power, power_checks


class ClaimableRelicTests(unittest.TestCase):
    # All card rewards shuffled and no other support shuffled, so power is Card Rewards and relics.
    options = {"characters": ["Ironclad"], "shuffle_all_cards": True}

    def make_world(self, **options) -> SlayTheSpire2World:
        world = setup_multiworld(SlayTheSpire2World, seed=42,
                                 options={**self.options, **options}).worlds[1]
        assert isinstance(world, SlayTheSpire2World)
        return world

    @staticmethod
    def collect(world: SlayTheSpire2World, state: CollectionState, item: str, count: int) -> None:
        for _ in range(count):
            state.collect(world.create_item(f"Ironclad {item}"), prevent_sweep=True)

    def test_power_counts_only_relics_the_run_can_claim(self):
        for anytime in (0, 2):
            world = self.make_world(relic_rewards_available_anytime=anytime)
            state = CollectionState(world.multiworld)
            self.collect(world, state, "Relic", 10)
            for region, rewards in PATH_RELIC_REWARDS.items():
                with self.subTest(anytime=anytime, region=region):
                    check = path_power(world.get_entrance(f"Ironclad {region}").access_rule)
                    relics = check.relic_entries(state)[0]
                    self.assertEqual(RELIC_POWER * min(10, anytime + rewards), relics.amount)
                    self.assertEqual(anytime + rewards < 10, "claimable" in relics.label)

    def test_reachable_elites_let_later_regions_count_their_rewards(self):
        world = self.make_world(relic_rewards_available_anytime=0)
        for region, alternatives in ELITE_RELIC_REWARDS.items():
            with self.subTest(region=region):
                checks = power_checks(world.get_entrance(f"Ironclad {region}").access_rule)
                self.assertEqual([PATH_RELIC_REWARDS[region], *alternatives.values()],
                                 [check.relic_cap for check in checks])

        # Eight Card Rewards and eight relics clear Early Act 2 only after the Act 1 elites:
        # walking around them claims one relic (the chest), fighting them claims three.
        state = CollectionState(world.multiworld)
        self.collect(world, state, "Card Reward", 8)
        self.collect(world, state, "Relic", 8)
        self.collect(world, state, "Progressive Ancient", 1)
        rule = world.get_entrance("Ironclad Early Act 2").access_rule
        self.assertTrue(state.can_reach_region("Ironclad Act 1 Elites", world.player))
        self.assertFalse(path_power(rule)(state))
        self.assertTrue(rule(state))

    def test_tracker_regeneration_keeps_the_anytime_count(self):
        world = self.make_world(relic_rewards_available_anytime=0)
        regenerated = setup_multiworld(SlayTheSpire2World, seed=42, steps=())
        regenerated.re_gen_passthrough = {SlayTheSpire2World.game: world.fill_slot_data()}
        for step in ("generate_early", "create_regions", "create_items", "set_rules"):
            call_all(regenerated, step)
        restored = regenerated.worlds[1]
        for region in PATH_RELIC_REWARDS:
            with self.subTest(region=region):
                self.assertEqual(
                    [check.relic_cap for check in power_checks(world.get_entrance(f"Ironclad {region}").access_rule)],
                    [check.relic_cap for check in power_checks(restored.get_entrance(f"Ironclad {region}").access_rule)])

    def test_sparse_opening_items_meet_mid_act_1_with_claimable_relics(self):
        for anytime in (0, 1, 2):
            for full_shuffle in (False, True):
                for seeded in (False, True):
                    with self.subTest(anytime=anytime, shuffle_all_cards=full_shuffle, seeded=seeded):
                        # Gold Sanity keeps a floorless solo world on new logic.
                        world = self.make_world(include_floor_checks=False, gold_sanity=True, seeded=seeded,
                                                shuffle_all_cards=full_shuffle,
                                                relic_rewards_available_anytime=anytime)
                        self.assertEqual("new", world.effective_logic)
                        early_items = world.multiworld.early_items[world.player]
                        self.assertLessEqual(early_items["Ironclad Relic"], anytime + PATH_RELIC_REWARDS["Mid Act 1"])
                        state = CollectionState(world.multiworld)
                        for item, count in early_items.items():
                            for _ in range(count):
                                state.collect(world.create_item(item), prevent_sweep=True)
                        self.assertTrue(world.get_entrance("Ironclad Mid Act 1").access_rule(state))


if __name__ == "__main__":
    unittest.main()
