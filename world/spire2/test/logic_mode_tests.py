import io
import unittest

from BaseClasses import CollectionState
from Fill import distribute_items_restrictive
from test.general import setup_multiworld
from worlds.AutoWorld import call_all
from worlds.spire2 import SlayTheSpire2World
from worlds.spire2.rules_old import LegacySpireHasPower


class LogicModeTests(unittest.TestCase):
    sparse = {
        'characters': ['Ironclad'], 'include_floor_checks': False,
        'gold_sanity': False, 'potion_sanity': False, 'shop_sanity': False,
        'ancient_relic_location': 'start_of_act',
    }

    def test_gold_is_progression_exactly_when_logic_counts_it(self):
        # Only whether gold is progression is checked: the other flags are tuning and change often.
        cases = [
            {'shop_sanity': shops, 'include_floor_checks': True, 'gold_sanity': True}
            for shops in (False, True)
        ] + [self.sparse]
        for options in cases:
            with self.subTest(options=options):
                mw = setup_multiworld(SlayTheSpire2World, seed=42,
                                      options={'characters': ['Ironclad'], **options})
                world = mw.worlds[1]
                # New logic counts Elite and Boss Gold toward shop power whenever Gold Sanity is on,
                # and Combat Gold toward each act's shop gold with Shop Sanity too. Old logic always
                # counts Elite and Boss Gold.
                new_logic = world.effective_logic == 'new'
                counted = {
                    'Elite Gold': not new_logic or bool(world.options.gold_sanity),
                    'Boss Gold': not new_logic or bool(world.options.gold_sanity),
                    'Combat Gold': new_logic and bool(world.options.gold_sanity and world.options.shop_sanity),
                }
                for suffix, expected in counted.items():
                    # Includes names used for modded characters and explicit item creation.
                    for character in ('Ironclad', 'Custom Character 1'):
                        self.assertEqual(expected, world.create_item(f'{character} {suffix}').advancement)
                    if world.options.gold_sanity:
                        items = [item for item in mw.itempool if item.name == f'Ironclad {suffix}']
                        self.assertTrue(items)
                        self.assertTrue(all(item.advancement == expected for item in items))
                distribute_items_restrictive(mw)
                self.assertTrue(mw.can_beat_game())
                self.assertTrue(mw.fulfills_accessibility())

    def test_default_new_logic_and_narrow_fallback(self):
        for timing in ('start_of_act', 'anytime'):
            for shops in (False, True):
                sparse = {**self.sparse, 'ancient_relic_location': timing, 'shop_sanity': shops}
                world = setup_multiworld(SlayTheSpire2World, options=sparse, seed=42).worlds[1]
                self.assertEqual('old', world.effective_logic)
                self.assertTrue(world.logic_fallback_reason)
                self.assertTrue(world.multiworld.early_items[1])
                for setting in ('include_floor_checks', 'gold_sanity', 'potion_sanity'):
                    with self.subTest(timing=timing, shops=shops, setting=setting):
                        world = setup_multiworld(SlayTheSpire2World, seed=42,
                                                 options={**sparse, setting: True}).worlds[1]
                        self.assertEqual('new', world.effective_logic)
                        self.assertIsNone(world.logic_fallback_reason)

    def test_legacy_power_copy_and_remove(self):
        mw = setup_multiworld(SlayTheSpire2World, seed=42, options={**self.sparse, 'shuffle_all_cards': False})
        old = mw.worlds[1]
        self.assertEqual('old', old.effective_logic)
        state = CollectionState(mw)
        for suffix in ('Card Reward', 'Card Reward', 'Rare Card Reward', 'Relic'):
            state.collect(old.create_item('Ironclad ' + suffix), prevent_sweep=True)
        rule = LegacySpireHasPower(old.characters[0].char_offset, 7).resolve(old)
        self.assertTrue(rule(state))
        self.assertIn('Ironclad', str(rule.explain_json(state)))
        copied = state.copy()
        copied.remove(old.create_item('Ironclad Card Reward'))
        self.assertFalse(rule(copied))
        self.assertTrue(rule(state))
        distribute_items_restrictive(mw)
        self.assertTrue(mw.can_beat_game())
        self.assertTrue(mw.fulfills_accessibility())

    def test_tracker_preserves_generated_mode_and_power(self):
        for floors, timing in [(True, 'start_of_act'), (False, 'start_of_act'),
                               (True, 'anytime'), (False, 'anytime')]:
            world = setup_multiworld(SlayTheSpire2World, seed=42, options={
                **self.sparse, 'include_floor_checks': floors,
                'shuffle_all_cards': True, 'ancient_relic_location': timing,
            }).worlds[1]
            slot_data = world.fill_slot_data()
            self.assertNotIn('logic_fallback_reason', slot_data)
            # Older clients call ToString() on every top-level slot value during login.
            self.assertTrue(all(value is not None for value in slot_data.values()))
            regenerated = setup_multiworld(SlayTheSpire2World, seed=42, steps=())
            regenerated.re_gen_passthrough = {world.game: slot_data}
            for step in ('generate_early', 'create_regions', 'create_items', 'set_rules'):
                call_all(regenerated, step)
            restored = regenerated.worlds[1]
            self.assertEqual(world.effective_logic, restored.effective_logic)
            self.assertEqual(world.logic_fallback_reason, restored.logic_fallback_reason)
            output = io.StringIO()
            restored.write_spoiler_header(output)
            self.assertIn('Effective logic: ' + world.effective_logic, output.getvalue())
            if restored.effective_logic == 'old':
                # Exercise the pre-existing state as well as states created after UT setup.
                for state in (regenerated.state, CollectionState(regenerated)):
                    state.collect(restored.create_item('Ironclad Card Reward'), prevent_sweep=True)
                    rule = LegacySpireHasPower(restored.characters[0].char_offset, 1.5).resolve(restored)
                    self.assertFalse(rule(state))
