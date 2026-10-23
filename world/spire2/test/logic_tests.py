import unittest

from BaseClasses import CollectionState
from test.general import setup_multiworld
from worlds.spire2 import SlayTheSpire2World
from worlds.spire2.characters import DEFAULT_STARTER_POWER, character_list
from worlds.spire2.rules import SpireHasPower
from worlds.spire2.test import path_power
from worlds.spire2.logic_table import (REGIONS, REST_POWER_BY_ACT, REST_SHARE_WHILE_SMITHING, SMITH_POWER_BY_TIER,
                                       VANILLA_CARD_POWER, VANILLA_CARD_REWARD_SHARE, card_reward_strength,
                                       card_reward_table)


class LogicTests(unittest.TestCase):
    # Test behaviour, not carefully balanced inventories that change whenever weights do.
    options = {
        "characters": ["Silent"],
        "shuffle_all_cards": True,
        "campfire_sanity": True,
        "neow_sanity": True,
        "shop_sanity": True,
        "shop_card_slots": 5,
        "shop_neutral_card_slots": 2,
        "shop_relic_slots": 3,
        "shop_potion_slots": 3,
        "shop_remove_slots": True,
    }
    shop_slots = (
        ("shop_card_slots", "Shop Card Slot", 5),
        ("shop_neutral_card_slots", "Neutral Shop Card Slot", 2),
        ("shop_relic_slots", "Shop Relic Slot", 3),
        ("shop_potion_slots", "Shop Potion Slot", 3),
    )

    def make_world(self, **options) -> SlayTheSpire2World:
        world = setup_multiworld(SlayTheSpire2World, seed=42,
                                 options={**self.options, **options}).worlds[1]
        assert isinstance(world, SlayTheSpire2World)
        return world

    @staticmethod
    def power_rule(world, act=1) -> SpireHasPower.Resolved:
        rule = SpireHasPower(world.characters[0].char_offset, 9, act=act,
                             rest=True, smith=1.0, remove=True, shop=True).resolve(world)
        assert isinstance(rule, SpireHasPower.Resolved)
        return rule

    def test_first_smith_costs_power_without_an_item_gate(self):
        world = self.make_world()
        state = CollectionState(world.multiworld)
        for item in world.multiworld.get_items():
            if item.name != "Silent Progressive Smith":
                state.collect(item, prevent_sweep=True)
        # Everything else outweighs the missing Smith: extra power replaces it, as for other support.
        self.assertTrue(world.get_entrance("Silent Act 1 Boss Arena").access_rule(state))
        rule = path_power(world.get_entrance("Silent Act 1 Boss Arena").access_rule)
        self.assertEqual(SMITH_POWER_BY_TIER[0], rule.power_adjustments(state)["Missing Progressive Smith tier 1"])

    def test_smith_tiers_count_the_rest_sites_passed_in_their_act(self):
        world = self.make_world()
        state = CollectionState(world.multiworld)
        for region, check in REGIONS.items():
            if check.smith is None:
                continue
            with self.subTest(region=region):
                rule = path_power(world.get_entrance(f"Silent {region}").access_rule)
                missing = sum(rule.power_adjustments(state)[label] for label in rule.power_adjustments(state)
                              if label.startswith("Missing Progressive Smith"))
                expected = sum(SMITH_POWER_BY_TIER[:check.act - 1]) + SMITH_POWER_BY_TIER[check.act - 1] * check.smith
                self.assertAlmostEqual(expected, missing)
        for act in (2, 3):
            early, boss = (path_power(world.get_entrance(f"Silent {region}").access_rule)
                           for region in (f"Early Act {act}", f"Act {act} Boss Arena"))
            self.assertLess(early.strength(state)[2] - early.power_level, boss.strength(state)[2] - boss.power_level)

    def test_sparse_opening_guarantees_meet_first_power_check(self):
        for full_shuffle in (False, True):
            for seeded in (False, True):
                with self.subTest(shuffle_all_cards=full_shuffle, seeded=seeded):
                    world = self.make_world(include_floor_checks=False, shuffle_all_cards=full_shuffle,
                                            seeded=seeded)
                    state = CollectionState(world.multiworld)
                    for item, count in world.multiworld.early_items[1].items():
                        for _ in range(count):
                            state.collect(world.create_item(item), prevent_sweep=True)
                    self.assertTrue(world.get_entrance('Silent Mid Act 1').access_rule(state))

    def test_gold_and_potion_sanity_change_power_only_through_shop_gold(self):
        for floors in (False, True):
            reference = self.make_world(include_floor_checks=floors,
                                        gold_sanity=True, potion_sanity=True)
            for gold in (False, True):
                for potions in (False, True):
                    world = self.make_world(include_floor_checks=floors,
                                            gold_sanity=gold, potion_sanity=potions)
                    if not floors and not gold and not potions:
                        self.assertEqual('old', world.effective_logic)
                        continue
                    self.assertEqual('new', world.effective_logic)
                    for act in (1, 2, 3):
                        for stocked in (False, True):
                            with self.subTest(floors=floors, gold=gold, potions=potions,
                                              act=act, stocked=stocked):
                                states = [CollectionState(w.multiworld) for w in (reference, world)]
                                if stocked:
                                    for w, state in zip((reference, world), states):
                                        for _, item, count in self.shop_slots:
                                            for _ in range(count):
                                                state.collect(w.create_item(f'Silent {item}'), prevent_sweep=True)
                                expected = self.without_shop_gold(self.power_rule(reference, act), states[0])
                                actual = self.without_shop_gold(self.power_rule(world, act), states[1])
                                for want, got in zip(expected, actual):
                                    self.assertAlmostEqual(want, got)

    @staticmethod
    def without_shop_gold(rule, state) -> tuple[float, float, float]:
        cards, power, required = rule.strength(state)
        gold = sum(amount for label, amount in rule.power_adjustments(state).items()
                   if label.startswith("Missing shop gold"))
        return cards, power, required - gold

    def test_gold_sanity_shops_need_elite_and_boss_gold(self):
        for shops in (False, True):
            with self.subTest(shop_sanity=shops):
                world = self.make_world(gold_sanity=True, shop_sanity=shops)
                state = CollectionState(world.multiworld)
                # Gold only buys the choices a shop offers, so restore every slot first.
                for _, item, count in self.shop_slots:
                    for _ in range(count if shops else 0):
                        state.collect(world.create_item(f"Silent {item}"), prevent_sweep=True)
                rules = [self.power_rule(world, act) for act in (1, 2, 3)]

                def shop_gold() -> list[float]:
                    return [sum(amount for label, amount in rule.power_adjustments(state).items()
                                if label.startswith("Missing shop gold")) for rule in rules]
                # Starting, chest and assumed Combat Gold cover Act 1; later acts need more.
                before = shop_gold()
                self.assertEqual(0, before[0])
                self.assertLess(0, before[1])
                self.assertLess(before[1], before[2])
                for _ in range(13):
                    state.collect(world.create_item("Silent Combat Gold"), prevent_sweep=True)
                self.assertEqual(before, shop_gold())
                for item, count in (("Elite Gold", 7), ("Boss Gold", 2)):
                    for _ in range(count):
                        state.collect(world.create_item(f"Silent {item}"), prevent_sweep=True)
                self.assertEqual([0, 0, 0], shop_gold())
        world = self.make_world(gold_sanity=False)
        state = CollectionState(world.multiworld)
        self.assertFalse(any(label.startswith("Missing shop gold")
                             for label in self.power_rule(world, 3).power_adjustments(state)))

    def test_extra_power_can_replace_optional_support(self):
        world = self.make_world()
        state = CollectionState(world.multiworld)
        state.collect(world.create_item("Silent Progressive Smith"), prevent_sweep=True)
        bosses = [world.get_entrance(f"Silent Act {act} Boss Arena") for act in (1, 2, 3)]
        for boss in bosses:
            self.assertFalse(boss.access_rule(state))
        # Deliberately ample power, including diminishing returns and every missing-support
        # penalty: this checks for hidden hard gates, not a particular balance threshold.
        for _ in range(200):
            state.collect(world.create_item("Silent Card Reward"), prevent_sweep=True)
        for boss in bosses:
            with self.subTest(boss=boss.name):
                self.assertTrue(boss.access_rule(state))

    def test_start_of_act_ancients_unlock_checkpoints_after_their_checks(self):
        for mode in ("start_of_act", "anytime"):
            for neow in (False, True):
                world = self.make_world(ancient_relic_location=mode, neow_sanity=neow)
                state = CollectionState(world.multiworld)
                state.collect(world.create_item("Silent Progressive Smith"), prevent_sweep=True)
                for _ in range(100):
                    state.collect(world.create_item("Silent Card Reward"), prevent_sweep=True)
                for received in range(4):
                    for act in (2, 3):
                        with self.subTest(mode=mode, neow=neow, received=received, act=act):
                            required = act - 1 + int(neow)
                            entrance = world.get_entrance(f"Silent Early Act {act}")
                            self.assertEqual(mode == "anytime" or received >= required,
                                             entrance.access_rule(state))
                            if mode == "start_of_act" and received == required - 1:
                                # The encounter check must stay reachable before its reward gate.
                                self.assertTrue(world.get_location(f"Silent Ancient Act {act}").can_reach(state))
                    state.collect(world.create_item("Silent Progressive Ancient"), prevent_sweep=True)

    def test_ordinary_card_rewards_follow_the_strength_table(self):
        for full_shuffle in (True, False):
            world = self.make_world(shuffle_all_cards=full_shuffle)
            table = card_reward_table(full_shuffle)
            rule: SpireHasPower.Resolved = SpireHasPower(
                world.characters[0].char_offset, 9, card_rewards=21).resolve(world)
            state = CollectionState(world.multiworld)
            before = rule.strength(state)
            vanilla = 0 if full_shuffle else 21 * VANILLA_CARD_REWARD_SHARE * VANILLA_CARD_POWER
            self.assertAlmostEqual(vanilla, before[0])
            # Run past the end of the table, where each reward adds the table's last step.
            for received in range(1, len(table) + 3):
                with self.subTest(full_shuffle=full_shuffle, received=received):
                    state.collect(world.create_item("Silent Card Reward"), prevent_sweep=True)
                    after = rule.strength(state)
                    expected = card_reward_strength(table, received) - card_reward_strength(table, received - 1)
                    self.assertAlmostEqual(table[-1] - table[-2] if received >= len(table)
                                           else table[received] - table[received - 1], expected)
                    self.assertAlmostEqual(expected, after[0] - before[0])
                    self.assertAlmostEqual(expected, after[1] - before[1])
                    self.assertEqual(before[2], after[2])
                    before = after
            state.collect(world.create_item("Silent Rare Card Reward"), prevent_sweep=True)
            self.assertEqual(1.5, rule.strength(state)[1] - before[1])

    def test_unshuffled_slots_count_as_available(self):
        world = self.make_world()
        state = CollectionState(world.multiworld)
        for _, item, _ in self.shop_slots:
            state.collect(world.create_item(f"Silent {item}"), prevent_sweep=True)
        partial_world = self.make_world(**{option: total - 1 for option, _, total in self.shop_slots})
        partial_state = CollectionState(partial_world.multiworld)
        self.assertGreater(self.power_rule(world).shop_penalty(state), 0)
        self.assertEqual(self.power_rule(world).shop_penalty(state),
                         self.power_rule(partial_world).shop_penalty(partial_state))

    def test_restoring_shop_slots_reduces_penalty_to_zero(self):
        world = self.make_world()
        state = CollectionState(world.multiworld)
        rules = [self.power_rule(world, act) for act in (1, 2, 3)]
        penalties = [rule.shop_penalty(state) for rule in rules]
        # Purchases stay for the run, so missing choices cost more in each later act.
        self.assertGreater(penalties[0], 0)
        self.assertLess(penalties[0], penalties[1])
        self.assertLess(penalties[1], penalties[2])
        for _, item, count in self.shop_slots:
            for tier in range(1, count + 1):
                with self.subTest(item=item, tier=tier):
                    state.collect(world.create_item(f"Silent {item}"), prevent_sweep=True)
                    updated = [rule.shop_penalty(state) for rule in rules]
                    for before, after in zip(penalties, updated):
                        self.assertLess(after, before)
                    penalties = updated
        self.assertEqual([0, 0, 0], penalties)

    def test_smith_and_removal_tiers_only_help_from_their_act(self):
        world = self.make_world()
        rules = [self.power_rule(world, act) for act in (1, 2, 3)]
        for item in ("Progressive Smith", "Progressive Shop Remove"):
            state = CollectionState(world.multiworld)
            for tier in (1, 2, 3):
                before = [rule.strength(state)[2] for rule in rules]
                state.collect(world.create_item(f"Silent {item}"), prevent_sweep=True)
                for act, rule in enumerate(rules, start=1):
                    with self.subTest(item=item, tier=tier, act=act):
                        after = rule.strength(state)[2]
                        if act < tier:
                            self.assertEqual(before[act - 1], after)
                        else:
                            self.assertLess(after, before[act - 1])

        for act in (1, 2, 3):
            state = CollectionState(world.multiworld)
            # Hold every rest tier so a smith tier cannot lower a missing rest's cost.
            for _ in range(3):
                state.collect(world.create_item("Silent Progressive Rest"), prevent_sweep=True)
            for _ in range(act - 1):
                state.collect(world.create_item("Silent Progressive Smith"), prevent_sweep=True)
            for stage in ("Mid", "Late"):
                with self.subTest(act=act, stage=stage):
                    rule = path_power(world.get_entrance(f"Silent {stage} Act {act}").access_rule)
                    with_smith = state.copy()
                    with_smith.collect(world.create_item("Silent Progressive Smith"), prevent_sweep=True)
                    self.assertLess(rule.strength(with_smith)[2], rule.strength(state)[2])

    def test_missing_rest_costs_less_while_smithing_works(self):
        world = self.make_world()
        for act in (1, 2, 3):
            with self.subTest(act=act):
                rule = path_power(world.get_entrance(f"Silent Mid Act {act}").access_rule)
                state = CollectionState(world.multiworld)
                for _ in range(act - 1):
                    state.collect(world.create_item("Silent Progressive Rest"), prevent_sweep=True)
                for _ in range(act - 1):
                    state.collect(world.create_item("Silent Progressive Smith"), prevent_sweep=True)
                full = REST_POWER_BY_ACT[act - 1]
                self.assertEqual(full, rule.power_adjustments(state)[f"Missing Rest tier {act}"])
                state.collect(world.create_item("Silent Progressive Smith"), prevent_sweep=True)
                self.assertEqual(full * REST_SHARE_WHILE_SMITHING,
                                 rule.power_adjustments(state)[f"Missing Rest tier {act} (smithing instead)"])
                state.collect(world.create_item("Silent Progressive Rest"), prevent_sweep=True)
                self.assertFalse(any(label.startswith("Missing Rest")
                                     for label in rule.power_adjustments(state)))

    def test_missing_tier_labels_name_a_single_tier_once(self):
        world = self.make_world()
        rule = self.power_rule(world, act=3)
        state = CollectionState(world.multiworld)
        self.assertIn("Missing Progressive Smith tiers 1-3", rule.power_adjustments(state))
        for _ in range(2):
            state.collect(world.create_item("Silent Progressive Smith"), prevent_sweep=True)
        self.assertIn("Missing Progressive Smith tier 3", rule.power_adjustments(state))

    def test_early_acts_ignore_rest_but_account_for_smith_support(self):
        world = self.make_world(ancient_relic_location="anytime")
        for act in (2, 3):
            with self.subTest(act=act):
                rule = path_power(world.get_entrance(f"Silent Early Act {act}").access_rule)
                state = CollectionState(world.multiworld)
                before = rule.strength(state)
                for _ in range(3):
                    state.collect(world.create_item("Silent Progressive Rest"), prevent_sweep=True)
                    self.assertEqual(before, rule.strength(state))
                for tier in range(1, 4):
                    before = rule.strength(state)
                    state.collect(world.create_item("Silent Progressive Smith"), prevent_sweep=True)
                    after = rule.strength(state)
                    # An Early region has passed none of its own act's rest sites.
                    if tier < act:
                        self.assertLess(after[2], before[2])
                    else:
                        self.assertEqual(before, after)

    def test_starters_restore_baseline_then_add_upgrade_power_without_hard_gates(self):
        world = self.make_world(characters=character_list,
                                progressive_starter_card=True, progressive_starter_relic=True)
        for config in world.characters:
            rule: SpireHasPower.Resolved = SpireHasPower(config.char_offset, 9).resolve(world)
            for kind in ("Card", "Relic"):
                state = CollectionState(world.multiworld)
                before = rule.strength(state)
                for tier in (1, 2, 3):
                    with self.subTest(character=config.name, kind=kind, tier=tier):
                        state.collect(world.create_item(f"{config.name} Progressive Starter {kind}"),
                                      prevent_sweep=True)
                        after = rule.strength(state)
                        if tier == 1:
                            self.assertEqual(after[:2], before[:2])
                            self.assertLess(after[2], before[2])
                        elif tier == 2:
                            self.assertGreater(after[1], before[1])
                            self.assertEqual(after[2], before[2])
                            if kind == "Card":
                                self.assertEqual(after[0] - before[0], after[1] - before[1])
                            else:
                                self.assertEqual(after[0], before[0])
                        else:
                            self.assertEqual(before, after)
                        before = after
            state = CollectionState(world.multiworld)
            for _ in range(100):
                state.collect(world.create_item(f"{config.name} Card Reward"), prevent_sweep=True)
            with self.subTest(character=config.name, gate="Late Act 1"):
                self.assertTrue(world.get_entrance(f"{config.name} Late Act 1").access_rule(state))

    def test_modded_starters_use_default_starter_power(self):
        world = self.make_world(characters=[], modded_characters=["TestCharacter"],
                                progressive_starter_card=True, progressive_starter_relic=True)
        prefix = world.characters[0].name
        state = CollectionState(world.multiworld)
        for _ in range(100):
            state.collect(world.create_item(f"{prefix} Card Reward"), prevent_sweep=True)
        # Enough power replaces missing starters: no item gate remains.
        self.assertTrue(world.get_entrance(f"{prefix} Late Act 1").access_rule(state))
        rule = self.power_rule(world)
        for kind, (charge, upgrade) in (("Card", DEFAULT_STARTER_POWER.card),
                                        ("Relic", DEFAULT_STARTER_POWER.relic)):
            with self.subTest(starter=kind):
                cards, power, required = rule.strength(state)
                state.collect(world.create_item(f"{prefix} Progressive Starter {kind}"), prevent_sweep=True)
                self.assertEqual((cards, power, required - charge), rule.strength(state))
                state.collect(world.create_item(f"{prefix} Progressive Starter {kind}"), prevent_sweep=True)
                card_upgrade = upgrade if kind == "Card" else 0
                self.assertEqual((cards + card_upgrade, power + upgrade, required - charge), rule.strength(state))

    def test_disabled_starter_options_give_no_power_credit(self):
        for kind in ("Card", "Relic"):
            with self.subTest(disabled_starter=kind):
                world = self.make_world(progressive_starter_card=kind != "Card",
                                        progressive_starter_relic=kind != "Relic")
                state = CollectionState(world.multiworld)
                rule = self.power_rule(world)
                before = rule.strength(state)
                for _ in range(2):
                    state.collect(world.create_item(f"Silent Progressive Starter {kind}"), prevent_sweep=True)
                self.assertEqual(before, rule.strength(state))

    def test_restored_starters_match_unshuffled_loadout(self):
        for character in ("Ironclad", "Silent", "Defect", "Necrobinder", "Regent"):
            baseline = self.make_world(characters=[character],
                                       progressive_starter_card=False, progressive_starter_relic=False)
            expected = self.power_rule(baseline).strength(CollectionState(baseline.multiworld))
            for card_enabled, relic_enabled in ((True, False), (False, True), (True, True)):
                with self.subTest(character=character, card=card_enabled, relic=relic_enabled):
                    world = self.make_world(characters=[character],
                                            progressive_starter_card=card_enabled,
                                            progressive_starter_relic=relic_enabled)
                    state = CollectionState(world.multiworld)
                    rule = self.power_rule(world)
                    self.assertGreater(rule.strength(state)[2], expected[2])
                    for kind, enabled in (("Card", card_enabled), ("Relic", relic_enabled)):
                        if enabled:
                            state.collect(world.create_item(f"{character} Progressive Starter {kind}"),
                                          prevent_sweep=True)
                    self.assertEqual(expected, rule.strength(state))

    def test_necrobinder_missing_synergy_is_charged_once_until_both_starters_return(self):
        world = self.make_world(characters=["Necrobinder"],
                                progressive_starter_card=True, progressive_starter_relic=True)
        rule = self.power_rule(world)
        for first in ("Card", "Relic"):
            with self.subTest(first=first):
                state = CollectionState(world.multiworld)
                self.assertEqual(0.5, rule.power_adjustments(state)["Missing starter card/relic synergy"])
                state.collect(world.create_item(f"Necrobinder Progressive Starter {first}"), prevent_sweep=True)
                self.assertEqual(0.5, rule.power_adjustments(state)["Missing starter card/relic synergy"])
                second = "Relic" if first == "Card" else "Card"
                state.collect(world.create_item(f"Necrobinder Progressive Starter {second}"), prevent_sweep=True)
                self.assertNotIn("Missing starter card/relic synergy", rule.power_adjustments(state))
