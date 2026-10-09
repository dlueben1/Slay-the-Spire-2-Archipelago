from collections import Counter
from unittest.mock import patch

from BaseClasses import ItemClassification
from worlds.spire2.items import ItemType, chars_to_items, item_groups, item_table, trap_item_table
from worlds.spire2.test import Spire2TestBase


class TestTraps(Spire2TestBase):
    options = {
        "characters": ["ironclad", "silent"],
        "modded_characters": ["WATCHER-WATCHER"],
        "bonus_items": [{"WAX_RELIC": {"Value": "THE_BOOT"}}],
    }

    def disable_traps(self):
        for field in self.world.options_dataclass.type_hints:
            if field.endswith("_trap_weight"):
                getattr(self.world.options, field).value = 0

    def test_replacement_chance_and_framework_entry_point(self):
        # Exercise lazy setup as AP may request replacement filler before create_items.
        del self.world.filler_universal
        for chance, roll, expect_trap in [(0, 0, False), (20, 19, True),
                                          (20, 20, False), (100, 99, True)]:
            self.world.options.trap_chance.value = chance
            with self.subTest(chance=chance, roll=roll), \
                    patch.object(self.world.random, "randrange", return_value=roll):
                self.assertEqual(expect_trap, self.world.get_filler_item_name() in trap_item_table)

    def test_configured_weights_and_disabled_traps(self):
        self.disable_traps()
        self.world.options.frail_trap_weight.value = 1
        self.world.options.dazed_trap_weight.value = 5
        self.world.options.trap_chance.value = 100
        self.world.build_filler_pools()
        with patch.object(self.world.random, "choices", return_value=["Dazed Trap"]) as draw:
            self.assertEqual("Dazed Trap", self.world.get_filler_item_name())
        self.assertEqual({"Frail Trap": 1, "Dazed Trap": 5},
                         dict(zip(draw.call_args.args[0], draw.call_args.kwargs["weights"])))
        self.world.options.frail_trap_weight.value = 0
        self.world.build_filler_pools()
        self.assertEqual("Dazed Trap", self.world.get_filler_item_name())

    def test_empty_mix_requires_zero_chance(self):
        self.disable_traps()
        self.world.options.trap_chance.value = 20
        with self.assertRaisesRegex(ValueError, "all trap weights are None"):
            self.world.build_filler_pools()
        self.world.options.trap_chance.value = 0
        self.world.build_filler_pools()
        self.assertNotIn(self.world.get_filler_item_name(), trap_item_table)

    def test_traps_have_unique_universal_ids_and_group(self):
        self.assertEqual(set(range(700, 709)), {data.code for data in trap_item_table.values()})
        self.assertEqual(set(trap_item_table), item_groups["Traps"])
        for name, data in trap_item_table.items():
            self.assertEqual(ItemClassification.trap, data.classification)
            self.assertEqual(ItemType.TRAP, data.type)
            self.assertEqual(-1, data.char_offset)
            self.assertEqual([name], [key for key, other in item_table.items() if other.code == data.code])
            self.assertTrue(all(name not in items for items in chars_to_items.values()))

    def test_generation_preserves_required_items_and_is_repeatable(self):
        required_by_chance = {}
        pools_by_chance = {}
        for chance in (0, 20, 100, 20):
            # Generate fresh worlds rather than invoking create_items twice on a populated world.
            self.options = {**type(self).options, "trap_chance": chance}
            self.world_setup(seed=712)
            pool = [item.name for item in self.multiworld.itempool]
            locations = [loc for loc in self.multiworld.get_locations()
                         if loc.address is not None and loc.item is None]
            self.assertEqual(len(locations), len(pool))
            self.assertEqual(1, pool.count("Bonus Wax Relic"))
            required_by_chance[chance] = Counter(
                item.name for item in self.multiworld.itempool
                if item.classification not in (ItemClassification.filler, ItemClassification.trap))
            replacements = [item for item in self.multiworld.itempool
                            if item.classification in (ItemClassification.filler, ItemClassification.trap)]
            self.assertTrue(replacements)
            if chance in (0, 100):
                self.assertTrue(all((item.name in trap_item_table) == (chance == 100) for item in replacements))
            if chance in pools_by_chance:
                self.assertEqual(pools_by_chance[chance], pool)
            pools_by_chance[chance] = pool
        self.assertEqual(required_by_chance[0], required_by_chance[20])
        self.assertEqual(required_by_chance[0], required_by_chance[100])
