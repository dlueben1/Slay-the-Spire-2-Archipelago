import copy
import unittest

from Fill import distribute_items_restrictive
from schema import SchemaError
from test.general import setup_multiworld, setup_solo_multiworld
from worlds.AutoWorld import call_all
from worlds.spire2 import SlayTheSpire2World
from worlds.spire2.options import CharacterOptions
from worlds.spire2.rules import SpireHasPower


class TestAdvancedStarters(unittest.TestCase):
    @staticmethod
    def generate(**options):
        return setup_multiworld(SlayTheSpire2World, seed=42, options={
            "use_advanced_characters": True,
            "advanced_characters": {
                "ironclad": {"progressive_starter_card": False, "progressive_starter_relic": True},
                "silent": {"progressive_starter_card": True, "progressive_starter_relic": False},
                "TestModCharacter": {},
            },
            **options,
        }).worlds[1]

    def test_overrides_and_inheritance_preserve_item_and_location_budget(self):
        baseline = self.generate(advanced_characters={
            "ironclad": {}, "silent": {}, "TestModCharacter": {},
        })
        locations = {(loc.name, loc.address) for loc in baseline.multiworld.get_locations()}
        for card in (False, True):
            for relic in (False, True):
                with self.subTest(card=card, relic=relic):
                    world = self.generate(progressive_starter_card=card, progressive_starter_relic=relic)
                    names = [item.name for item in world.multiworld.itempool]
                    expected = {"Ironclad": (False, True), "Silent": (True, False),
                                "Custom Character 1": (card, relic)}
                    for config in world.characters:
                        enabled = expected[config.name]
                        self.assertEqual(enabled, (config.progressive_starter_card, config.progressive_starter_relic))
                        for kind, active in zip(("Card", "Relic"), enabled):
                            self.assertEqual(2 if active else 0, names.count(f"{config.name} Progressive Starter {kind}"))
                        wire = next(c for c in world.fill_slot_data()["characters"] if c["name"] == config.name)
                        self.assertEqual(enabled, (wire["progressive_starter_card"], wire["progressive_starter_relic"]))
                    self.assertEqual(len(baseline.multiworld.itempool), len(names))
                    self.assertEqual(locations, {(loc.name, loc.address) for loc in world.multiworld.get_locations()})

    def test_floor_checks_disable_explicit_overrides_too(self):
        world = self.generate(include_floor_checks=False,
                              progressive_starter_card=True, progressive_starter_relic=True)
        for config in world.characters:
            self.assertFalse(config.progressive_starter_card)
            self.assertFalse(config.progressive_starter_relic)
        self.assertFalse(any("Progressive Starter" in item.name for item in world.multiworld.itempool))
        data = world.fill_slot_data()
        self.assertEqual(0, data["progressive_starter_card"])
        self.assertEqual(0, data["progressive_starter_relic"])

    def test_vanilla_power_rules_use_each_characters_options(self):
        world = self.generate(progressive_starter_card=True, progressive_starter_relic=True)
        for config in world.characters:
            if config.mod_num:
                continue
            rule = SpireHasPower(config.char_offset, 9).resolve(world)
            self.assertEqual(config.progressive_starter_card, bool(rule.starter_card_power[0]))
            self.assertEqual(config.progressive_starter_relic, bool(rule.starter_relic_power[0]))

    def test_modded_and_legacy_gates_use_overrides_instead_of_global_options(self):
        for new_logic in (False, True):
            with self.subTest(new_logic=new_logic):
                world = self.generate(use_new_logic=new_logic, progressive_starter_card=False,
                                      progressive_starter_relic=True, advanced_characters={
                    "ModCard": {"progressive_starter_card": True, "progressive_starter_relic": False},
                    "ModNeither": {"progressive_starter_card": False, "progressive_starter_relic": False},
                })
                state = world.multiworld.get_all_state(False)
                gated = next(c for c in world.characters if c.option_name == "ModCard")
                ungated = next(c for c in world.characters if c.option_name == "ModNeither")
                gate = world.get_entrance(f"{gated.name} Late Act 1").access_rule
                self.assertTrue(gate(state))
                for _ in range(2):
                    state.remove(world.create_item(f"{gated.name} Progressive Starter Card"))
                self.assertFalse(gate(state))
                self.assertTrue(world.get_entrance(f"{ungated.name} Late Act 1").access_rule(state))
                state.collect(world.create_item(f"{gated.name} Progressive Starter Card"), prevent_sweep=True)
                self.assertTrue(gate(state))

    def test_tracker_round_trip_and_global_only_slot_data(self):
        original = self.generate(progressive_starter_card=True, progressive_starter_relic=False)
        for legacy in (False, True):
            data = copy.deepcopy(original.fill_slot_data())
            if legacy:
                for char in data["characters"]:
                    char.pop("progressive_starter_card")
                    char.pop("progressive_starter_relic")
            regenerated = setup_solo_multiworld(SlayTheSpire2World, steps=())
            regenerated.re_gen_passthrough = {SlayTheSpire2World.game: data}
            for step in ("generate_early", "create_regions", "create_items", "set_rules"):
                call_all(regenerated, step)
            restored = regenerated.worlds[1]
            if legacy:
                for config in restored.characters:
                    self.assertTrue(config.progressive_starter_card)
                    self.assertFalse(config.progressive_starter_relic)
            else:
                self.assertEqual(data["characters"], restored.fill_slot_data()["characters"])
                self.assertEqual(len(original.multiworld.itempool), len(restored.multiworld.itempool))
                # Tracker regeneration need not reroll the same random filler.
                self.assertEqual(sorted(item.name for item in original.multiworld.itempool
                                        if "Progressive Starter" in item.name),
                                 sorted(item.name for item in restored.multiworld.itempool
                                        if "Progressive Starter" in item.name))

    def test_mixed_roster_can_fill_and_be_completed(self):
        world = self.generate()
        distribute_items_restrictive(world.multiworld, panic_method="raise")
        self.assertFalse(world.multiworld.get_unfilled_locations())
        self.assertTrue(world.multiworld.can_beat_game())

    def test_override_schema_rejects_strings_and_numbers(self):
        for value in ("false", "yes", 1, None):
            for key in ("progressive_starter_card", "progressive_starter_relic"):
                with self.subTest(key=key, value=value), self.assertRaises(SchemaError):
                    CharacterOptions.schema.validate({"ironclad": {key: value}})
