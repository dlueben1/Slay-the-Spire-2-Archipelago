import itertools
import unittest

from BaseClasses import CollectionState, ItemClassification
from test.general import setup_multiworld
from worlds.AutoWorld import call_all
from worlds.spire2 import SlayTheSpire2World
from worlds.spire2.logic_table import (BOSS_GOLD, ELITE_GOLD, SHOP_PAGE, shop_check_price, shop_gold_needed,
                                       shop_page)
from worlds.spire2.rules import SpireHasGold
from worlds.spire2.rules_new import shop_gold_by_act
from worlds.spire2.world import BOSS_GOLD_ITEM_COUNT, ELITE_GOLD_ITEM_COUNT

FIXED, SUPER_DISCOUNT, DISCOUNT, TIERED = range(4)


class ShopGoldTests(unittest.TestCase):
    options = {
        "characters": ["Silent"],
        "shop_sanity": True,
        "gold_sanity": True,
    }

    def make_world(self, **options) -> SlayTheSpire2World:
        world = setup_multiworld(SlayTheSpire2World, seed=42, options={**self.options, **options}).worlds[1]
        assert isinstance(world, SlayTheSpire2World)
        return world

    @staticmethod
    def shop_gold(world: SlayTheSpire2World) -> list[int]:
        """The gold each act's shop entrance asks for, read back from the rules."""
        gold = []
        for act in (1, 2, 3):
            rule = world.get_entrance(f"Silent Act {act} Shop").access_rule
            gold.append(rule.gold if isinstance(rule, SpireHasGold.Resolved) else 0)
        return gold

    def test_removal_checks_borrow_unused_page_positions_in_category_order(self):
        # Mirrors the client's GetApSlotCounts: removals fill cards, then neutral, relic and potion gaps.
        self.assertEqual(["card"] * 3, shop_page(0, 0, 0, 0, True))
        self.assertEqual(["card"] * 5 + ["neutral"] * 2 + ["relic"] * 3,
                         shop_page(4, 1, 2, 0, True))
        self.assertEqual(["card"] * 2 + ["neutral"] + ["relic"] * 2 + ["potion"] * 2,
                         shop_page(2, 1, 2, 2, False))
        # A full page has no room for removals; they are listed once other checks are bought.
        full = sum(limit for _, limit in SHOP_PAGE)
        self.assertEqual(full, len(shop_page(5, 2, 3, 3, True)))

    def test_gold_items_cover_every_shop_in_every_configuration(self):
        supply = ELITE_GOLD_ITEM_COUNT * ELITE_GOLD + BOSS_GOLD_ITEM_COUNT * BOSS_GOLD
        for slots in itertools.product(range(6), range(3), range(4), range(4), (False, True)):
            page = shop_page(*slots)
            for cost_mode, act in itertools.product(range(4), (1, 2, 3)):
                with self.subTest(slots=slots, cost_mode=cost_mode, act=act):
                    self.assertLessEqual(shop_gold_needed(page, act, cost_mode), supply)

    def test_cheap_cost_modes_need_no_gold_items(self):
        for cost_mode in (FIXED, SUPER_DISCOUNT):
            for slots in itertools.product(range(6), range(3), range(4), range(4), (False, True)):
                with self.subTest(cost_mode=cost_mode, slots=slots):
                    page = shop_page(*slots)
                    self.assertEqual([0, 0, 0], [shop_gold_needed(page, act, cost_mode) for act in (1, 2, 3)])

    def test_chest_gold_lowers_later_acts(self):
        # A Tiered page of relics alone lists the same dearest check in every act.
        page = shop_page(0, 0, 3, 0, False)
        needed = [shop_gold_needed(page, act, TIERED) for act in (1, 2, 3)]
        self.assertEqual(sorted(needed, reverse=True), needed)
        self.assertGreater(needed[0], needed[2])

    def test_discount_affords_two_checks_but_tiered_one(self):
        page = shop_page(5, 0, 0, 0, False)
        card_price = shop_check_price("card", TIERED)
        self.assertEqual(shop_check_price("card", DISCOUNT) * 2, card_price)
        self.assertEqual(shop_gold_needed(page, 1, DISCOUNT), shop_gold_needed(page, 1, TIERED))

    def test_shop_entrances_need_the_computed_gold(self):
        for cost_mode in range(4):
            world = self.make_world(shop_sanity_costs=cost_mode)
            needed = list(shop_gold_by_act(world.options))
            self.assertEqual(needed, self.shop_gold(world))
            for act, gold in enumerate(needed, start=1):
                with self.subTest(cost_mode=cost_mode, act=act):
                    rule = world.get_entrance(f"Silent Act {act} Shop").access_rule
                    state = CollectionState(world.multiworld)
                    received = 0
                    while received < gold:
                        self.assertFalse(rule(state))
                        state.collect(world.create_item("Silent Elite Gold"), prevent_sweep=True)
                        received += ELITE_GOLD
                    self.assertTrue(rule(state))

    def test_shops_need_no_gold_without_gold_sanity(self):
        world = self.make_world(gold_sanity=False, shop_sanity_costs=TIERED)
        self.assertEqual((0, 0, 0), shop_gold_by_act(world.options))
        self.assertEqual([0, 0, 0], self.shop_gold(world))

    def test_elite_and_boss_gold_are_progression_with_any_shop_prices(self):
        # Shop choices need gold with Gold Sanity, even when the checks themselves are cheap.
        for cost_mode in (FIXED, SUPER_DISCOUNT, TIERED):
            world = self.make_world(shop_sanity_costs=cost_mode)
            for name in ("Silent Elite Gold", "Silent Boss Gold"):
                with self.subTest(cost_mode=cost_mode, item=name):
                    items = [item for item in world.multiworld.itempool if item.name == name]
                    self.assertTrue(items)
                    self.assertTrue(all(item.advancement for item in items))
            combat = [item for item in world.multiworld.itempool if item.name == "Silent Combat Gold"]
            self.assertTrue(combat)
            self.assertTrue(all(item.classification == ItemClassification.useful for item in combat))

    def test_tracker_regeneration_keeps_shop_gold(self):
        for cost_mode in (DISCOUNT, TIERED):
            with self.subTest(cost_mode=cost_mode):
                world = self.make_world(shop_sanity_costs=cost_mode, shop_relic_slots=3, shop_remove_slots=True)
                regenerated = setup_multiworld(SlayTheSpire2World, seed=42, steps=())
                regenerated.re_gen_passthrough = {SlayTheSpire2World.game: world.fill_slot_data()}
                for step in ("generate_early", "create_regions", "create_items", "set_rules"):
                    call_all(regenerated, step)
                self.assertTrue(any(self.shop_gold(world)))
                self.assertEqual(self.shop_gold(world), self.shop_gold(regenerated.worlds[1]))

    def test_tracker_explains_shop_gold(self):
        world = self.make_world(shop_sanity_costs=TIERED)
        rule = world.get_entrance("Silent Act 1 Shop").access_rule
        state = CollectionState(world.multiworld)
        state.collect(world.create_item("Silent Boss Gold"), prevent_sweep=True)
        text = rule.explain_json(state)[0]["text"]
        self.assertIn(f"{BOSS_GOLD} of {rule.gold} needed", text)
        self.assertIn("Boss Gold (1 received)", text)
        self.assertIn(f"{rule.gold}", rule.explain_json()[0]["text"])


if __name__ == "__main__":
    unittest.main()
