import copy
import unittest
from unittest.mock import patch

from BaseClasses import CollectionState
from Fill import distribute_items_restrictive
from Options import OptionError
from test.general import setup_multiworld, setup_solo_multiworld
from worlds.AutoWorld import call_all
from worlds.spire2 import SlayTheSpire2World
from worlds.spire2.characters import (
    CharacterDefinition, DEFAULT_STARTER_POWER, PREDEFINED_CHARACTERS, VANILLA_CHARACTERS,
    character_list, character_registry, create_character_registry,
)
from worlds.spire2.constants import CHAR_OFFSET, GENERIC_CHARACTER_BLOCKS, NUM_CUSTOM, PREDEFINED_CHARACTER_BLOCKS
from worlds.spire2.items import base_item_table, create_item_tables, item_table
from worlds.spire2.locations import create_location_data, create_location_tables, location_table
from worlds.spire2.options import GoalNumChar, ModdedCharacters, PickNumberCharacters, UnlockedCharacter
from worlds.spire2.rules_new import STARTER_CARD_POWER, STARTER_RELIC_POWER, SpireHasPower


class TestCharacterRegistry(unittest.TestCase):
    def test_vanilla_and_generic_ids_use_separate_reserved_ranges(self):
        # Freeze the intended ranges independently of the registry implementation.
        vanilla = ["Ironclad", "Silent", "Defect", "Regent", "Necrobinder"]
        generic = [f"Custom Character {i}" for i in range(1, 21)]
        for block, name in [*enumerate(vanilla, start=1), *enumerate(generic, start=11)]:
            for suffix, data in base_item_table.items():
                expected = None if data.code is None else block * CHAR_OFFSET + data.code
                self.assertEqual(expected, item_table[f"{name} {suffix}"].code)
            for data in create_location_data():
                expected = None if data.id is None else (block - 1) * CHAR_OFFSET + data.id
                self.assertEqual(expected, location_table[f"{name} {data.name}"])

    def test_named_blocks_and_names_match_the_published_registry(self):
        expected = [
            ("Tectonic", "INTOTHESPIREVERSE-SHADOW_IRONCLAD"),
            ("Captain", "INTOTHESPIREVERSE-SHADOW_REGENT"),
            ("Acolyte", "INTOTHESPIREVERSE-SHADOW_SILENT"),
            ("Slime Boss", "SLIMEBOSS-SLIME_BOSS"),
            ("Guardian", "GUARDIAN-GUARDIAN"),
            ("Hexaghost", "HEXAGHOST-HEXAGHOST"),
            ("Champ", "CHAMP-CHAMP"),
            ("Automaton", "AUTOMATON-AUTOMATON"),
            ("Snecko", "SNECKO-SNECKO"),
            ("Hermit", "HERMIT-HERMIT"),
            ("Awakened One", "AWAKENED-AWAKENED"),
            ("Watcher", "WATCHER-WATCHER"),
            ("Hornet", "HORNET_MOD_CHARACTER_HORNET_MOD_CHARACTER"),
            ("Slugcat Survivor", "SLUGCAT_THE_SPIRE_II_CHARACTER_RAINWORLD_LIVER"),
            ("Slugcat Hunter", "SLUGCAT_THE_SPIRE_II_CHARACTER_RAINWORLD_REDCAT"),
            ("Slugcat Rivulet", "SLUGCAT_THE_SPIRE_II_CHARACTER_RAINWORLD_RIVER"),
            ("Alchemist", "ALCHEMIST-ALCHEMIST"),
            ("Runesmith", "RUNESMITH2-RUNESMITH2"),
            ("Witch", "THEWITCH-WITCH"),
            ("Tailor", "THETAILOR-THE_TAILOR"),
            ("Engineer", "THEENGINEER-THE_ENGINEER"),
            ("Kakarot", "KAKAROTMOD-KAKAROT"),
            ("Fae Sorceress", "FAESORCERESS-FAE_SORCERESS"),
        ]
        for block, (name, internal_id) in enumerate(expected, start=200):
            self.assertEqual((name, internal_id),
                             (character_registry[block].name, character_registry[block].internal_id))
            self.assertEqual(block * CHAR_OFFSET + 4, item_table[f"{name} Progressive Ancient"].code)
            self.assertEqual((block - 1) * CHAR_OFFSET + 79, location_table[f"{name} Potion Drop 1"])

    def test_new_vanilla_character_does_not_renumber_existing_tables(self):
        future = CharacterDefinition("Future Vanilla", "FUTURE_VANILLA", DEFAULT_STARTER_POWER)
        registry = create_character_registry((*VANILLA_CHARACTERS, future))
        self.assertEqual(future, registry[6])
        items, _, _ = create_item_tables(registry)
        locations, _, _ = create_location_tables(registry)
        self.assertEqual(item_table, {name: items[name] for name in item_table})
        self.assertEqual(location_table, {name: locations[name] for name in location_table})
        self.assertEqual(60004, items["Future Vanilla Progressive Ancient"].code)

    def test_aliases_cannot_shadow_another_registered_character(self):
        with self.assertRaisesRegex(ValueError, "ambiguous alias"):
            create_character_registry(predefined=(
                CharacterDefinition("Example", "EXAMPLE-MOD"),
                CharacterDefinition("example-mod", "ANOTHER-MOD"),
            ))

    def test_ten_vanilla_characters_fit_without_overlapping_generic_blocks(self):
        future = tuple(CharacterDefinition(f"Future {i}", f"FUTURE_{i}", DEFAULT_STARTER_POWER)
                       for i in range(6, 11))
        registry = create_character_registry((*VANILLA_CHARACTERS, *future))
        self.assertEqual(future[-1], registry[10])
        self.assertEqual("Custom Character 1", registry[11].name)
        with self.assertRaisesRegex(ValueError, "reserved ID range"):
            create_character_registry((*VANILLA_CHARACTERS, *future,
                                       CharacterDefinition("Overflow", "OVERFLOW", DEFAULT_STARTER_POWER)))

    def test_raising_the_generic_cap_adds_blocks_without_moving_named_ids(self):
        self.assertEqual(f"Custom Character {NUM_CUSTOM}", character_registry[10 + NUM_CUSTOM].name)
        self.assertNotIn(11 + NUM_CUSTOM, character_registry)
        with patch("worlds.spire2.characters.NUM_CUSTOM", len(GENERIC_CHARACTER_BLOCKS)):
            registry = create_character_registry()
        self.assertEqual("Custom Character 189", registry[199].name)
        self.assertEqual(character_registry, {block: registry[block] for block in character_registry})
        with patch("worlds.spire2.characters.NUM_CUSTOM", len(GENERIC_CHARACTER_BLOCKS) + 1):
            with self.assertRaisesRegex(ValueError, "reserved ID range"):
                create_character_registry()

    def test_ids_are_unique_and_every_reserved_block_fits_a_signed_32_bit_id(self):
        for ids in ([data.code for data in item_table.values() if data.code is not None],
                    [code for code in location_table.values() if code is not None]):
            self.assertEqual(len(ids), len(set(ids)))
            self.assertTrue(all(0 < code for code in ids))
        # Item IDs use one-based blocks, so the last reserved block's items end here.
        self.assertLess((PREDEFINED_CHARACTER_BLOCKS[-1] + 1) * CHAR_OFFSET, 2**31)
        # Named characters sit above the old 1,000,000-ID co-op partition the client used to strip.
        self.assertGreater(item_table["Watcher Relic"].code, 1_000_000)

    def test_starter_profiles_cover_exactly_every_registered_vanilla_character(self):
        self.assertEqual(set(character_list), set(STARTER_CARD_POWER))
        self.assertEqual(set(character_list), set(STARTER_RELIC_POWER))
        self.assertTrue(all(definition.starter_power is not None for definition in VANILLA_CHARACTERS),
                        "Every vanilla character must choose reviewed starter power or DEFAULT_STARTER_POWER")
        self.assertTrue(all(definition.starter_power is None for definition in PREDEFINED_CHARACTERS))
        reviewed = [definition.starter_power for definition in VANILLA_CHARACTERS
                    if definition.starter_power is not DEFAULT_STARTER_POWER]
        for kind in ("card", "relic"):
            for index in range(2):
                average = sum(getattr(power, kind)[index] for power in reviewed) / len(reviewed)
                self.assertLessEqual(abs(average - getattr(DEFAULT_STARTER_POWER, kind)[index]), 0.5,
                                     f"DEFAULT_STARTER_POWER.{kind} must stay near the vanilla average")
        full_roster = len(character_list) + len(PREDEFINED_CHARACTERS) + NUM_CUSTOM
        self.assertEqual(full_roster, GoalNumChar.range_end)
        self.assertEqual(full_roster, PickNumberCharacters.range_end)
        for index, name in enumerate(character_list):
            self.assertEqual(index, UnlockedCharacter.from_any(name.lower()).value)


class TestCharacterRosters(unittest.TestCase):
    @staticmethod
    def generate(**options):
        return setup_multiworld(SlayTheSpire2World, seed=42, options={
            "characters": [], "lock_characters": 0, "potion_sanity": True, **options,
        }).worlds[1]

    def test_ordered_option_deduplicates_case_insensitively(self):
        self.assertEqual(["Z-Mod", "watcher-watcher", "A-Mod"],
                         ModdedCharacters.from_any([" Z-Mod ", "watcher-watcher", "z-MOD",
                                                    "A-Mod", "WATCHER-WATCHER"]).value)

    def test_aliases_and_internal_ids_share_one_basic_roster_entry(self):
        world = self.generate(modded_characters=[" watcher ", "WATCHER-WATCHER", "ModA",
                                                "guardian-guardian", "GUARDIAN"])
        self.assertEqual(["Watcher", "Custom Character 1", "Guardian"],
                         [c.name for c in world.characters])
        self.assertEqual(["WATCHER-WATCHER", "ModA", "GUARDIAN-GUARDIAN"],
                         [c.official_name for c in world.characters])
        self.assertEqual([1, 2, 3], [c.mod_num for c in world.characters])

    def test_every_predefined_alias_matches_its_internal_id_in_both_modes(self):
        # A single roster is capped at NUM_CUSTOM modded characters, so check the registry in batches.
        batches = [PREDEFINED_CHARACTERS[start:start + NUM_CUSTOM]
                   for start in range(0, len(PREDEFINED_CHARACTERS), NUM_CUSTOM)]
        for advanced in (False, True):
            for batch in batches:
                aliases = [definition.name.swapcase() for definition in batch]
                ids = [definition.internal_id for definition in batch]
                def generate(roster):
                    return self.generate(modded_characters=roster, use_advanced_characters=advanced,
                                         advanced_characters={name: {"progressive_starter_card": True}
                                                              for name in roster})
                by_alias, by_id = generate(aliases), generate(ids)
                self.assertEqual([(c.name, c.official_name, c.char_offset, c.mod_num) for c in by_id.characters],
                                 [(c.name, c.official_name, c.char_offset, c.mod_num) for c in by_alias.characters])
                self.assertEqual([(loc.name, loc.address) for loc in by_id.get_locations()],
                                 [(loc.name, loc.address) for loc in by_alias.get_locations()])
                if advanced:
                    self.assertTrue(all(c.progressive_starter_card for c in by_alias.characters))

    def test_fixed_start_accepts_alias_or_internal_id_with_either_roster_spelling(self):
        for advanced in (False, True):
            for roster_name, unlocked in (("Watcher", "WATCHER-WATCHER"),
                                          ("WATCHER-WATCHER", "watcher")):
                roster = ["Guardian", roster_name, "ModA"]
                world = self.generate(modded_characters=roster, use_advanced_characters=advanced,
                                      advanced_characters={name: {} for name in roster},
                                      lock_characters=2, unlocked_character=unlocked, pick_num_characters=1)
                self.assertEqual([("Watcher", "WATCHER-WATCHER", False)],
                                 [(c.name, c.official_name, c.locked) for c in world.characters])

    def test_advanced_alias_and_internal_id_duplicates_are_rejected(self):
        with self.assertRaisesRegex(OptionError, "duplicate character IDs or aliases"):
            self.generate(use_advanced_characters=True,
                          advanced_characters={"Watcher": {}, "WATCHER-WATCHER": {}})

    def test_basic_and_advanced_preserve_order_and_number_only_unknown_characters(self):
        roster = ["WATCHER-WATCHER", "Z-Mod", "GUARDIAN-GUARDIAN", "A-Mod"]
        for advanced in (False, True):
            world = self.generate(modded_characters=roster, use_advanced_characters=advanced,
                                  advanced_characters={name: {} for name in roster})
            self.assertEqual(["Watcher", "Custom Character 1", "Guardian", "Custom Character 2"],
                             [config.name for config in world.characters])
            self.assertEqual([211, 11, 204, 12], [config.char_offset for config in world.characters])
            self.assertEqual(roster, [config.official_name for config in world.characters])

    def test_advanced_case_collisions_are_rejected(self):
        with self.assertRaisesRegex(OptionError, "duplicate character IDs"):
            self.generate(use_advanced_characters=True,
                          advanced_characters={"WATCHER-WATCHER": {}, "watcher-watcher": {}})

    def test_random_subset_has_contiguous_generic_numbers_in_original_relative_order(self):
        roster = [f"Mod-{i:02}" for i in reversed(range(25))]
        world = self.generate(modded_characters=roster, pick_num_characters=4)
        self.assertEqual([f"Custom Character {i}" for i in range(1, 5)],
                         [config.name for config in world.characters])
        selected = {config.option_name for config in world.characters}
        self.assertEqual([name for name in roster if name in selected],
                         [config.option_name for config in world.characters])
        fixed = self.generate(modded_characters=roster, pick_num_characters=1,
                              lock_characters=2, unlocked_character=roster[-1])
        self.assertEqual(("Custom Character 1", roster[-1]),
                         (fixed.characters[0].name, fixed.characters[0].official_name))

    def test_every_predefined_character_and_all_generic_slots_can_generate_and_fill(self):
        # Predefined characters have their own blocks, so they do not count against NUM_CUSTOM.
        predefined = [definition.internal_id for definition in PREDEFINED_CHARACTERS]
        roster = predefined + [f"Mod-{i}" for i in range(NUM_CUSTOM)]
        self.assertGreater(len(roster), NUM_CUSTOM)
        for new_logic in (False, True):
            with self.subTest(new_logic=new_logic):
                world = self.generate(characters=character_list, modded_characters=roster,
                                      use_new_logic=new_logic)
                self.assertEqual(len(character_list) + len(roster), len(world.characters))
                self.assertEqual(len(roster), world.modded_num)
                self.assertEqual(f"Custom Character {NUM_CUSTOM}", world.characters[-1].name)
                distribute_items_restrictive(world.multiworld, panic_method="raise")
                self.assertFalse(world.multiworld.get_unfilled_locations())
                self.assertTrue(world.multiworld.can_beat_game())

    def test_predefined_characters_beyond_the_generic_limit_are_accepted(self):
        # Shrink the limit below the number of predefined characters.
        predefined = [definition.name for definition in PREDEFINED_CHARACTERS]
        with patch("worlds.spire2.world.NUM_CUSTOM", 1):
            world = self.generate(modded_characters=predefined + ["Unknown-A"])
            self.assertEqual(len(predefined) + 1, world.modded_num)
            self.assertEqual(["Custom Character 1"],
                             [c.name for c in world.characters if c.char_offset in GENERIC_CHARACTER_BLOCKS])
            with self.assertRaisesRegex(OptionError, "at most 1 selected characters without a predefined name"):
                self.generate(modded_characters=predefined + ["Unknown-A", "Unknown-B"])

    def test_all_generic_slots_and_overflow(self):
        roster = [f"Unknown-{i}" for i in range(NUM_CUSTOM + 1)]
        predefined = [definition.internal_id for definition in PREDEFINED_CHARACTERS]
        overflow = f"at most {NUM_CUSTOM} selected characters without a predefined name"
        for advanced in (False, True):
            with self.subTest(advanced=advanced):
                def generate(names, **options):
                    if advanced:
                        return self.generate(use_advanced_characters=True,
                                             advanced_characters={name: {} for name in names}, **options)
                    return self.generate(modded_characters=names, **options)

                world = generate(roster[:NUM_CUSTOM])
                self.assertEqual(NUM_CUSTOM, world.modded_num)
                self.assertEqual(GENERIC_CHARACTER_BLOCKS[NUM_CUSTOM - 1], world.characters[-1].char_offset)
                self.assertEqual(f"Custom Character {NUM_CUSTOM}", world.characters[-1].name)
                with self.assertRaisesRegex(OptionError, overflow):
                    generate(roster)
                with self.assertRaisesRegex(OptionError, overflow):
                    generate(roster, pick_num_characters=NUM_CUSTOM + 1)
                # Predefined characters neither use up nor free a generic slot.
                with self.assertRaisesRegex(OptionError, overflow):
                    generate(predefined + roster)

    def test_random_subset_replaces_excess_generic_characters_with_predefined_ones(self):
        predefined = [definition.internal_id for definition in PREDEFINED_CHARACTERS]
        unknown = [f"Unknown-{i}" for i in range(NUM_CUSTOM + 10)]
        # Interleave so the YAML order check covers both kinds of character.
        roster = [name for pair in zip(unknown, predefined) for name in pair] + unknown[len(predefined):]
        pick = len(predefined) + NUM_CUSTOM
        for advanced in (False, True):
            with self.subTest(advanced=advanced):
                def generate(**options):
                    if advanced:
                        return self.generate(use_advanced_characters=True,
                                             advanced_characters={name: {} for name in roster}, **options)
                    return self.generate(modded_characters=roster, **options)

                world = generate(pick_num_characters=pick, lock_characters=2, unlocked_character=unknown[-1])
                self.assertEqual(pick, len(world.characters))
                # Filling every generic slot leaves exactly enough room for every predefined character.
                self.assertEqual(set(predefined), {c.option_name for c in world.characters} & set(predefined))
                generic = [c for c in world.characters if c.char_offset in GENERIC_CHARACTER_BLOCKS]
                self.assertEqual([f"Custom Character {i}" for i in range(1, NUM_CUSTOM + 1)],
                                 [c.name for c in generic])
                self.assertEqual([unknown[-1]], [c.option_name for c in world.characters if not c.locked])
                selected = {c.option_name for c in world.characters}
                self.assertEqual([name for name in roster if name in selected],
                                 [c.option_name for c in world.characters])
                with self.assertRaisesRegex(OptionError, "without a predefined name"):
                    generate(pick_num_characters=pick + 1)

    def test_random_selection_replaces_excess_mods_without_shrinking_or_losing_fixed_start(self):
        roster = [f"Unknown-{i}" for i in range(30)]
        world = self.generate(characters=character_list, modded_characters=roster,
                              pick_num_characters=25, lock_characters=2, unlocked_character=roster[-1])
        self.assertEqual(25, len(world.characters))
        self.assertEqual(20, world.modded_num)
        self.assertEqual([roster[-1]], [c.option_name for c in world.characters if not c.locked])

    def test_two_players_can_use_different_order_without_changing_named_ids(self):
        mw = setup_multiworld([SlayTheSpire2World] * 2, seed=42, options=[
            {"characters": [], "modded_characters": roster, "lock_characters": 0, "potion_sanity": True}
            for roster in (["WATCHER-WATCHER", "ModA"], ["GUARDIAN-GUARDIAN", "ModB", "WATCHER-WATCHER"])
        ])
        self.assertEqual(mw.worlds[1].get_location("Watcher Potion Drop 1").address,
                         mw.worlds[2].get_location("Watcher Potion Drop 1").address)
        distribute_items_restrictive(mw, panic_method="raise")
        self.assertTrue(mw.can_beat_game())

    def test_tracker_restores_named_and_generic_assignments(self):
        for roster in (["WATCHER-WATCHER", "ModA"], ["ModA", "WATCHER-WATCHER"]):
            original = self.generate(modded_characters=roster)
            data = copy.deepcopy(original.fill_slot_data())
            regenerated = setup_solo_multiworld(SlayTheSpire2World, steps=())
            regenerated.re_gen_passthrough = {SlayTheSpire2World.game: data}
            for step in ("generate_early", "create_regions", "create_items", "set_rules"):
                call_all(regenerated, step)
            restored = regenerated.worlds[1]
            self.assertEqual(data["characters"], restored.fill_slot_data()["characters"])
            self.assertEqual({(loc.name, loc.address) for loc in original.get_locations()},
                             {(loc.name, loc.address) for loc in restored.get_locations()})
            self.assertEqual([(item.name, item.code) for item in original.multiworld.itempool
                              if item.advancement],
                             [(item.name, item.code) for item in regenerated.itempool if item.advancement])
            first = restored.characters[0]
            location = restored.get_location(f"{first.name} Potion Drop 1")
            if first.name == "Custom Character 1":
                self.assertEqual("ModA Potion Drop 1", restored.location_id_to_alias[location.address])
                self.assertEqual(11, first.char_offset)
            else:
                self.assertNotIn(location.address, restored.location_id_to_alias)
                self.assertEqual("Watcher", first.name)

    def test_default_starter_profile_restores_baseline_then_adds_upgrade_power(self):
        # Missing reviewed data uses the explicit fallback; coverage tests above still fail
        # if a real registered vanilla character has not chosen a profile.
        with patch.dict(STARTER_CARD_POWER, {}, clear=True), patch.dict(STARTER_RELIC_POWER, {}, clear=True):
            world = self.generate(characters=["Ironclad"], progressive_starter_card=True,
                                  progressive_starter_relic=True)
            rule = SpireHasPower(world.characters[0].char_offset, 9).resolve(world)
        for kind, (penalty, upgrade) in (("Card", DEFAULT_STARTER_POWER.card),
                                         ("Relic", DEFAULT_STARTER_POWER.relic)):
            state = CollectionState(world.multiworld)
            before = rule.strength(state)
            state.collect(world.create_item(f"Ironclad Progressive Starter {kind}"), prevent_sweep=True)
            first = rule.strength(state)
            self.assertAlmostEqual(penalty, before[2] - first[2])
            self.assertEqual(before[:2], first[:2])
            state.collect(world.create_item(f"Ironclad Progressive Starter {kind}"), prevent_sweep=True)
            second = rule.strength(state)
            self.assertAlmostEqual(upgrade, second[1] - first[1])
            self.assertEqual(first[2], second[2])
            state.collect(world.create_item(f"Ironclad Progressive Starter {kind}"), prevent_sweep=True)
            self.assertEqual(second, rule.strength(state))
