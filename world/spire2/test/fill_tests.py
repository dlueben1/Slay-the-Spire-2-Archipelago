import unittest
from unittest.mock import patch

from BaseClasses import Location, LocationProgressType
from Fill import distribute_items_restrictive
from test.general import setup_multiworld

from worlds.spire2 import SlayTheSpire2World
from worlds.spire2.fill import fill_shared_slots


class TestSharedSlotFill(unittest.TestCase):
    def generate(self, **options):
        return setup_multiworld(SlayTheSpire2World, seed=101, options={
            "characters": ["Ironclad", "Silent", "Defect", "Regent", "Necrobinder"],
            "player_count": 4, "accessibility": "full", "include_floor_checks": 1,
            "shop_sanity": 1, "campfire_sanity": 1, "gold_sanity": 1,
            "potion_sanity": 1, "neow_sanity": 1, **options})

    def pools(self, mw):
        progression = sorted(item for item in mw.itempool if item.advancement)
        locations = sorted(mw.get_unfilled_locations())
        mw.random.shuffle(progression)
        mw.random.shuffle(locations)
        return progression, locations

    def test_batch_prefill_preserves_ownership_and_completes(self):
        mw = self.generate()
        progression, locations = self.pools(mw)
        original = [(item, item.player, item.code, item.name) for item in progression]
        with self.assertLogs(level="INFO") as logs:
            self.assertTrue(fill_shared_slots(mw, [mw.worlds[1]], progression, locations))
        self.assertFalse(progression)
        self.assertTrue(any("Batched" in line for line in logs.output))
        for item, player, code, name in original:
            self.assertEqual((player, code, name), (item.player, item.code, item.name))
            self.assertIs(item.location.item, item)
        self.assertTrue(mw.can_beat_game())
        self.assertTrue(mw.fulfills_accessibility())

    def test_restricted_settings_use_original_filler(self):
        for options in ({"accessibility": "minimal"}, {"include_floor_checks": 0},
                        {"player_count": 1}):
            with self.subTest(options=options):
                mw = self.generate(**options)
                progression, locations = self.pools(mw)
                with patch("worlds.spire2.fill.sweep_from_pool", side_effect=AssertionError("unexpected batch")):
                    self.assertFalse(fill_shared_slots(mw, [mw.worlds[1]], progression, locations))
                distribute_items_restrictive(mw)
                self.assertFalse(mw.get_unfilled_locations())
                self.assertTrue(mw.can_beat_game())

    def test_priority_and_excluded_locations_use_original_filler(self):
        for progress_type in (LocationProgressType.PRIORITY, LocationProgressType.EXCLUDED):
            with self.subTest(progress_type=progress_type):
                mw = self.generate()
                progression, locations = self.pools(mw)
                locations[0].progress_type = progress_type
                with patch("worlds.spire2.fill.sweep_from_pool", side_effect=AssertionError("unexpected batch")):
                    self.assertFalse(fill_shared_slots(mw, [mw.worlds[1]], progression, locations))

    def test_mixed_shared_and_single_slots_complete_with_private_items(self):
        options = {"characters": ["Ironclad", "Silent", "Defect", "Regent"],
                   "accessibility": "full", "lock_characters": 0, "include_floor_checks": 1}
        mw = setup_multiworld([SlayTheSpire2World, SlayTheSpire2World], seed=101,
                              options=[dict(options, player_count=4), dict(options, player_count=1)])
        identities = {id(item): (item.player, item.code, item.name) for item in mw.itempool}
        with self.assertLogs(level="INFO") as logs:
            distribute_items_restrictive(mw)
        self.assertTrue(any("Batched" in line for line in logs.output))
        self.assertFalse(mw.get_unfilled_locations())
        self.assertTrue(mw.can_beat_game())
        self.assertTrue(mw.fulfills_accessibility())
        for item in mw.itempool:
            self.assertEqual(identities[id(item)], (item.player, item.code, item.name))
            self.assertIs(item.location.item, item)

    def test_item_link_groups_use_original_filler(self):
        mw = self.generate()
        progression, locations = self.pools(mw)
        # Selection must stop before inspecting or modifying a linked group's state.
        mw.groups[2] = {}
        with patch("worlds.spire2.fill.sweep_from_pool", side_effect=AssertionError("unexpected batch")):
            self.assertFalse(fill_shared_slots(mw, [mw.worlds[1]], progression, locations))

    def test_stalled_attempt_restores_placements_pool_order_and_random_state(self):
        mw = self.generate()
        progression, locations = self.pools(mw)
        original_items = list(progression)
        original_locations = list(locations)
        preplaced = [(loc, loc.item) for loc in mw.get_filled_locations()]
        rng = mw.random.getstate()
        actual_can_fill = Location.can_fill
        accepted = 0

        def reject_after_four(location, state, item, check_access=True):
            nonlocal accepted
            if accepted == 4:
                # Check rollback even if a third-party rule touched the RNG.
                mw.random.random()
                return False
            result = actual_can_fill(location, state, item, check_access)
            accepted += bool(result)
            return result

        with patch.object(Location, "can_fill", reject_after_four):
            self.assertFalse(fill_shared_slots(mw, [mw.worlds[1]], progression, locations))
        self.assertEqual(4, accepted)
        self.assertEqual(list(map(id, original_items)), list(map(id, progression)))
        self.assertEqual(list(map(id, original_locations)), list(map(id, locations)))
        self.assertEqual(rng, mw.random.getstate())
        self.assertTrue(all(item.location is None for item in progression))
        self.assertTrue(all(loc.item is None for loc in locations))
        self.assertTrue(all(loc.item is item and item.location is loc for loc, item in preplaced))
        distribute_items_restrictive(mw)
        self.assertFalse(mw.get_unfilled_locations())
        self.assertTrue(mw.can_beat_game())
