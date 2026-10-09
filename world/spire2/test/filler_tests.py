from collections import Counter
from random import Random
from unittest.mock import patch

from worlds.spire2.items import chars_to_items
from worlds.spire2.test import Spire2TestBase


class TestFillerWeights(Spire2TestBase):
    options = {"trap_chance": 0, "characters": ["ironclad", "silent"]}

    def disable_filler(self):
        for name in self.world.options_dataclass.type_hints:
            if name.endswith("_filler_weight"):
                getattr(self.world.options, name).value = 0

    def test_individual_weights_survive_unequal_group_sizes(self):
        self.world.build_filler_pools()
        # Defaults contain seven high, four medium, and four low items for a character.
        # Assert the actual draw's weights, rather than allowing a statistical test to flake.
        with patch.object(self.world.random, "choices", return_value=["Buffer"]) as draw:
            self.assertEqual("Buffer", self.world.get_filler_item("Ironclad"))
        names = draw.call_args.args[0]
        weights = dict(zip(names, draw.call_args.kwargs["weights"]))
        self.assertEqual(5, weights["Free Attack"])
        self.assertEqual(3, weights["Strength"])
        self.assertEqual(1, weights["Buffer"])
        self.assertEqual(5, weights["Ironclad Five Gold"])
        self.assertNotIn("Silent Five Gold", weights)
        self.assertNotIn("Ironclad One Gold", weights)
        self.assertEqual(Counter({5: 7, 3: 4, 1: 4}), Counter(weights.values()))

    def test_only_enabled_item_is_always_selected(self):
        self.disable_filler()
        self.world.options.buffer_filler_weight.value = 1
        self.world.build_filler_pools()
        self.assertEqual({"Buffer"}, {self.world.get_filler_item_name() for _ in range(30)})

    def test_disabling_other_items_keeps_relative_weights(self):
        self.disable_filler()
        self.world.options.strength_filler_weight.value = 5
        self.world.options.buffer_filler_weight.value = 1
        self.world.build_filler_pools()
        with patch.object(self.world.random, "choices", return_value=["Strength"]) as draw:
            self.world.get_filler_item("Silent")
        self.assertEqual(
            {"Strength": 5, "Buffer": 1},
            dict(zip(draw.call_args.args[0], draw.call_args.kwargs["weights"])),
        )

    def test_all_disabled_fallback_matches_requested_character(self):
        self.disable_filler()
        self.world.build_filler_pools()
        for character in ("Ironclad", "Silent"):
            self.assertEqual(f"{character} One Gold", self.world.get_filler_item(character))
        self.assertLessEqual(
            {self.world.get_filler_item_name() for _ in range(30)},
            {"Ironclad One Gold", "Silent One Gold"},
        )

    def test_framework_entry_point_builds_pools_lazily(self):
        self.disable_filler()
        self.world.options.artifact_filler_weight.value = 3
        del self.world.filler_universal
        self.assertEqual("Artifact", self.world.get_filler_item_name())

    def test_seeded_generation_is_repeatable(self):
        self.world.build_filler_pools()
        self.world.random = Random(123)
        first = [self.world.get_filler_item_name() for _ in range(100)]
        self.world.random = Random(123)
        self.assertEqual(first, [self.world.get_filler_item_name() for _ in range(100)])


class TestModdedFillerWeights(Spire2TestBase):
    options = {"trap_chance": 0, "characters": ["silent"], "modded_characters": ["WATCHER-WATCHER"]}

    def test_custom_character_gold_and_fallback_use_its_numbered_item_table(self):
        character = next(config for config in self.world.characters if config.mod_num)
        items = chars_to_items[character.mod_num]
        five_gold = next(name for name in items if "Five Gold" in name)
        one_gold = next(name for name in items if "One Gold" in name)
        self.world.build_filler_pools()
        with patch.object(self.world.random, "choices", return_value=[five_gold]) as draw:
            self.assertEqual(five_gold, self.world.get_filler_item(character.name))
        weights = dict(zip(draw.call_args.args[0], draw.call_args.kwargs["weights"]))
        self.assertEqual(5, weights[five_gold])
        self.assertNotIn("Silent Five Gold", weights)
        for name in self.world.options_dataclass.type_hints:
            if name.endswith("_filler_weight"):
                getattr(self.world.options, name).value = 0
        self.world.build_filler_pools()
        self.assertEqual(one_gold, self.world.get_filler_item(character.name))
