import io
import unittest

from BaseClasses import CollectionState, ItemClassification
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

    def test_gold_classification_follows_effective_logic_and_shops(self):
        legacy_flags = {
            'Elite Gold': ItemClassification.progression_deprioritized_skip_balancing,
            'Boss Gold': ItemClassification.progression,
        }
        cases = [
            {'use_new_logic': use_new_logic, 'shop_sanity': shops, 'include_floor_checks': True, 'gold_sanity': True}
            for use_new_logic in (True, False) for shops in (False, True)
        ] + [{**self.sparse, 'use_new_logic': True}]
        for options in cases:
            with self.subTest(options=options):
                mw = setup_multiworld(SlayTheSpire2World, seed=42,
                                      options={'characters': ['Ironclad'], **options})
                world = mw.worlds[1]
                useful_gold = world.effective_logic == 'new' and not world.options.shop_sanity
                for suffix, legacy in legacy_flags.items():
                    expected = ItemClassification.useful if useful_gold else legacy
                    # Includes names used for modded characters and explicit item creation.
                    for character in ('Ironclad', 'Custom Character 1'):
                        self.assertEqual(expected, world.create_item(f'{character} {suffix}').classification)
                    if world.options.gold_sanity:
                        items = [item for item in mw.itempool if item.name == f'Ironclad {suffix}']
                        self.assertTrue(items)
                        self.assertTrue(all(item.classification == expected for item in items))
                distribute_items_restrictive(mw)
                self.assertTrue(mw.can_beat_game())
                self.assertTrue(mw.fulfills_accessibility())

    def test_default_new_logic_and_narrow_fallback(self):
        for timing in ('start_of_act', 'anytime'):
            for shops in (False, True):
                sparse = {**self.sparse, 'ancient_relic_location': timing, 'shop_sanity': shops}
                world = setup_multiworld(SlayTheSpire2World, options=sparse, seed=42).worlds[1]
                self.assertTrue(world.options.use_new_logic)
                self.assertEqual('old', world.effective_logic)
                self.assertTrue(world.logic_fallback_reason)
                self.assertTrue(world.multiworld.early_items[1])
                for setting in ('include_floor_checks', 'gold_sanity', 'potion_sanity'):
                    with self.subTest(timing=timing, shops=shops, setting=setting):
                        world = setup_multiworld(SlayTheSpire2World, seed=42,
                                                 options={**sparse, setting: True}).worlds[1]
                        self.assertEqual('new', world.effective_logic)
                        self.assertIsNone(world.logic_fallback_reason)
        world = setup_multiworld(SlayTheSpire2World, seed=42,
                                 options={'use_new_logic': False, 'include_floor_checks': True}).worlds[1]
        self.assertEqual('old', world.effective_logic)
        self.assertIsNone(world.logic_fallback_reason)

    def test_floorless_legacy_half_shuffle_opening_mix(self):
        for full_shuffle, cards, relics, power in [(False, 1, 1, 3.5), (True, 1, 2, 4.0)]:
            with self.subTest(full_shuffle=full_shuffle):
                world = setup_multiworld(SlayTheSpire2World, seed=42, options={
                    **self.sparse, 'shuffle_all_cards': full_shuffle,
                    'neow_sanity': True, 'campfire_sanity': True,
                }).worlds[1]
                early = world.multiworld.early_items[1]
                self.assertEqual(cards, early['Ironclad Card Reward'])
                self.assertEqual(relics, early['Ironclad Relic'])
                state = CollectionState(world.multiworld)
                for name, count in early.items():
                    for _ in range(count):
                        state.collect(world.create_item(name), prevent_sweep=True)
                self.assertEqual(power, state.power_level[1][world.characters[0].char_offset])
                self.assertTrue(world.get_entrance('Ironclad Mid Act 1').access_rule(state))
        # New logic still uses its own opening mix when a sanity keeps it out of fallback.
        world = setup_multiworld(SlayTheSpire2World, seed=42, options={
            **self.sparse, 'gold_sanity': True, 'shuffle_all_cards': False,
        }).worlds[1]
        self.assertEqual('new', world.effective_logic)
        self.assertEqual(0, world.multiworld.early_items[1].get('Ironclad Card Reward', 0))
        self.assertEqual(2, world.multiworld.early_items[1]['Ironclad Relic'])
        world = setup_multiworld(SlayTheSpire2World, seed=42, options={
            **self.sparse, 'use_new_logic': False, 'include_floor_checks': True, 'shuffle_all_cards': False,
        }).worlds[1]
        self.assertFalse(world.multiworld.early_items[1])

    def test_legacy_power_copy_remove_and_mixed_slots(self):
        mw = setup_multiworld([SlayTheSpire2World] * 2, seed=42, options=[
            {'use_new_logic': False, 'shuffle_all_cards': False, 'characters': ['Ironclad']},
            {'use_new_logic': True, 'include_floor_checks': True, 'characters': ['Ironclad']},
        ])
        old, new = mw.worlds[1], mw.worlds[2]
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
        state.collect(new.create_item('Ironclad Relic'), prevent_sweep=True)
        self.assertEqual(1, state.count('Ironclad Relic', 2))
        self.assertTrue(rule(state))
        distribute_items_restrictive(mw)
        self.assertTrue(mw.can_beat_game())
        self.assertTrue(mw.fulfills_accessibility())

    def test_tracker_preserves_generated_mode_and_power(self):
        for use_new_logic, floors, timing in [(True, True, 'start_of_act'), (True, False, 'start_of_act'),
                                              (True, True, 'anytime'), (True, False, 'anytime'),
                                              (False, True, 'anytime')]:
            world = setup_multiworld(SlayTheSpire2World, seed=42, options={
                **self.sparse, 'use_new_logic': use_new_logic, 'include_floor_checks': floors,
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
            self.assertEqual(world.options.use_new_logic.value, restored.options.use_new_logic.value)
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
