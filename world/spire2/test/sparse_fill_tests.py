import unittest

from BaseClasses import CollectionState
from Fill import distribute_items_restrictive
from test.general import setup_multiworld
from worlds.AutoWorld import call_all
from worlds.spire2 import SlayTheSpire2World
from worlds.spire2.world import sparse_placement_rank


class SparseFillTests(unittest.TestCase):
    # Floor, gold and potion checks all off: about one location per item.
    sparse = {
        'characters': ['Ironclad'], 'include_floor_checks': False,
        'gold_sanity': False, 'potion_sanity': False, 'campfire_sanity': True,
        'ancient_relic_location': 'start_of_act',
    }
    partner = {'characters': ['Silent'], 'include_floor_checks': True}

    def make_pair(self, seed=42, sparse=None):
        return setup_multiworld([SlayTheSpire2World] * 2, seed=seed, options=[sparse or self.sparse, self.partner])

    @staticmethod
    def shuffled_progression(multiworld) -> list:
        pool = sorted(item for item in multiworld.itempool if item.advancement)
        multiworld.random.shuffle(pool)
        return pool

    def test_sparse_worlds_use_new_logic_beside_other_players(self):
        alone = setup_multiworld(SlayTheSpire2World, seed=42, options=self.sparse).worlds[1]
        self.assertEqual('old', alone.effective_logic)
        self.assertTrue(alone.logic_fallback_reason)
        beside = self.make_pair().worlds[1]
        self.assertEqual('new', beside.effective_logic)
        self.assertIsNone(beside.logic_fallback_reason)

    def test_sparse_multiworlds_generate(self):
        for seed in range(1, 6):
            with self.subTest(seed=seed):
                multiworld = self.make_pair(seed)
                distribute_items_restrictive(multiworld)
                call_all(multiworld, 'post_fill')
                self.assertTrue(multiworld.can_beat_game(CollectionState(multiworld)))
                self.assertTrue(multiworld.fulfills_accessibility())

    def test_fill_hook_orders_only_this_worlds_items(self):
        multiworld = self.make_pair()
        world = multiworld.worlds[1]
        pool = self.shuffled_progression(multiworld)
        before = list(pool)
        world.fill_hook(pool, [], [], [])
        self.assertCountEqual(before, pool)
        # The other player's items keep their positions.
        for index, item in enumerate(before):
            if item.player != world.player:
                self.assertIs(item, pool[index])
        # Fill takes items from the end, so ranks must not rise towards the end of the pool.
        ours = [item for item in pool if item.player == world.player]
        ranks = [sparse_placement_rank(item) for item in ours]
        self.assertEqual(sorted(ranks, reverse=True), ranks)
        self.assertEqual(0, ranks[-1])
        # Items of one rank keep their shuffled order.
        for rank in set(ranks):
            self.assertEqual([item for item in before if item.player == world.player
                              and sparse_placement_rank(item) == rank],
                             [item for item in ours if sparse_placement_rank(item) == rank])

    def test_fill_hook_leaves_other_worlds_alone(self):
        multiworld = setup_multiworld([SlayTheSpire2World] * 2, seed=42, options=[
            {**self.sparse, 'include_floor_checks': True}, {**self.sparse, 'use_new_logic': False},
        ])
        pool = self.shuffled_progression(multiworld)
        before = list(pool)
        for world in multiworld.worlds.values():
            with self.subTest(player=world.player):
                world.fill_hook(pool, [], [], [])
                self.assertEqual(before, pool)

    def test_tracker_regenerating_alone_keeps_new_logic(self):
        world = self.make_pair().worlds[1]
        regenerated = setup_multiworld(SlayTheSpire2World, seed=42, steps=())
        regenerated.re_gen_passthrough = {SlayTheSpire2World.game: world.fill_slot_data()}
        for step in ('generate_early', 'create_regions', 'create_items', 'set_rules'):
            call_all(regenerated, step)
        restored = regenerated.worlds[1]
        self.assertEqual('new', restored.effective_logic)
        self.assertIsNone(restored.logic_fallback_reason)
        self.assertEqual(world.get_entrance('Ironclad Late Act 1').access_rule.power_level,
                         restored.get_entrance('Ironclad Late Act 1').access_rule.power_level)


if __name__ == '__main__':
    unittest.main()
