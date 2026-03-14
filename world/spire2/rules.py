import dataclasses
import typing
from collections import defaultdict
from typing import TYPE_CHECKING, List

from BaseClasses import CollectionState, MultiWorld, Item
from NetUtils import JSONMessagePart
from rule_builder.options import OptionFilter
from rule_builder.rules import HasFromList, Rule, TWorld, True_, Has, HasFromListUnique
from .characters import CharacterConfig, character_offset_map
from .items import ItemType
from .options import CampfireSanity, ShopSanity, GoldSanity
from ..AutoWorld import LogicMixin
from ..generic.Rules import set_rule

if TYPE_CHECKING:
    from .world import SlayTheSpire2World, SlayTheSpire2Item


class SpireLogic(LogicMixin):
    power_level: dict[int, dict[int, float]]
    item_levels: dict[int, dict[ItemType, float]]

    def init_mixin(self, multiworld: MultiWorld) -> None:
        slays = multiworld.get_game_worlds("Slay the Spire II")
        self.power_level = {
            slay.player: defaultdict(float) for slay in slays
        }
        self.item_levels = {
            slay.player: {
                ItemType.RELIC: 1.5,
                ItemType.CARD_REWARD: 1.0 if slay.options.shuffle_all_cards.value else 2.0,
                ItemType.RARE_CARD_REWARD: 1.5,
            } for slay in slays
        }

    def copy_mixin(self, new_state: CollectionState) -> CollectionState:
        for k,v in self.power_level.items():
            new_char_pl = defaultdict(float)
            new_state.power_level[k] = new_char_pl
            for ik, iv in v.items():
                new_char_pl[ik] = iv
        new_state.item_levels = {
            k: {inner: inner_v for inner, inner_v in v.items() } for k,v in self.item_levels.items()
        }
        return new_state


@dataclasses.dataclass()
class SpireHasPower(Rule['SlayTheSpire2World'], game="Slay the Spire II"):

    def __init__(self,
                 char_offset: int,
                 power_level: float,
                 options: typing.Iterable[OptionFilter] = (),
                 filtered_resolution: bool = False):
        super().__init__(options=options, filtered_resolution=filtered_resolution)
        self.power_level = power_level
        self.char_offset = char_offset

    @typing.override
    def _instantiate(self, world: 'SlayTheSpire2World') -> Rule.Resolved:
        if self.power_level <= 0.0:
            return True_().resolve(world)
        return self.Resolved(self.power_level, self.char_offset, player=world.player)


    class Resolved(Rule.Resolved):
        power_level: float
        char_offset: int

        @typing.override
        def _evaluate(self, state: CollectionState) -> bool:
            return state.power_level[self.player][self.char_offset] >= self.power_level

        @typing.override
        def explain_json(self, state: CollectionState | None = None) -> List[JSONMessagePart]:
            return [
                {
                    "type": "text", "text": f"{character_offset_map[self.char_offset]} has power level {self.power_level}"
                }
            ]

@dataclasses.dataclass()
class SpireHasGold(Rule['SlayTheSpire2World'], game="Slay the Spire II"):

    def __init__(self,
                 char: str,
                 gold: int,
                 options: typing.Iterable[OptionFilter] = (),
                 filtered_resolution: bool = False):
        super().__init__(options=options, filtered_resolution=filtered_resolution)
        self.gold = gold
        self.char = char

    @typing.override
    def _instantiate(self, world: 'SlayTheSpire2World') -> Rule.Resolved:
        if self.gold <= 0:
            return True_().resolve(world)
        return self.Resolved(self.gold, self.char, player=world.player)

    class Resolved(Rule.Resolved):
        gold: int
        char: str

        @typing.override
        def _evaluate(self, state: CollectionState) -> bool:
            return state.count(f"{self.char} 30 Gold", self.player) * 30 + state.count(f"{self.char} Boss Gold", self.player) * 75 >= self.gold

@dataclasses.dataclass()
class SpireHasShop(Rule['SlayTheSpire2World'], game="Slay the Spire II"):

    def __init__(self,
                 char: str,
                 shop: int,
                 options: typing.Iterable[OptionFilter] = (),
                 filtered_resolution: bool = False):
        super().__init__(options=options, filtered_resolution=filtered_resolution)
        self.shop = shop
        self.char = char

    @typing.override
    def _instantiate(self, world: 'SlayTheSpire2World') -> Rule.Resolved:
        if self.shop <= 0:
            return True_().resolve(world)
        return self.Resolved(self.shop, self.char, world.total_shop_items, player=world.player)

    class Resolved(Rule.Resolved):
        shop: int
        char: str
        max_shop: int

        @typing.override
        def _evaluate(self, state: CollectionState) -> bool:
            return (state.count(f"{self.char} Shop Card Slot", self.player) +
                    state.count(f"{self.char} Neutral Shop Card Slot", self.player) +
                    state.count(f"{self.char} Shop Relic Slot", self.player) +
                    state.count(f"{self.char} Shop Potion Slot", self.player) >= min(self.shop, self.max_shop))


def set_rules(world: 'SlayTheSpire2World') -> None:
    for config in world.characters:
        _set_rules(world, config)

    num_goals = len(world.characters) if world.options.num_chars_goal.value == 0 else world.options.num_chars_goal.value
    assert num_goals > 0
    world.set_completion_rule(HasFromListUnique(*[f"{config.name} Victory" for config in world.characters],
                                          count=num_goals))

def _set_rules(world: 'SlayTheSpire2World', config: CharacterConfig) -> None:
    prefix = config.name
    offset = config.char_offset
    world.set_rule(world.get_entrance(f"{prefix} Early Act 1"), Has(f"{prefix} Unlock"))
    world.set_rule(world.get_entrance(f"{prefix} Mid Act 1"), SpireHasPower(offset,3) &
                   Has(f"{prefix} Progressive Rest", options=[OptionFilter(CampfireSanity, 1)], filtered_resolution=True)
                   )
    world.set_rule(world.get_entrance(f"{prefix} Late Act 1"),SpireHasPower(offset,6) &
                   SpireHasShop(prefix, 2, options=[OptionFilter(ShopSanity,1)], filtered_resolution=True)
                   )
    world.set_rule(world.get_entrance(f"{prefix} Act 1 Boss Arena"), SpireHasPower(offset,9) &
                    Has(f"{prefix} Progressive Smith", options=[OptionFilter(CampfireSanity, 1)], filtered_resolution=True) &
                    SpireHasShop(prefix, 3, options=[OptionFilter(ShopSanity, 1)], filtered_resolution=True) &
                    Has(f"{prefix} Progressive Shop Remove", 1, options=[OptionFilter(ShopSanity, 1)], filtered_resolution=True) &
                    SpireHasGold(prefix, 50, options=[OptionFilter(GoldSanity, 1)], filtered_resolution=True)
    )
    world.set_rule(world.get_entrance(f"{prefix} Early Act 2"), SpireHasPower(offset,10))
    world.set_rule(world.get_entrance(f"{prefix} Mid Act 2"), SpireHasPower(offset,12) &
                    Has(f"{prefix} Progressive Rest", count=2, options=[OptionFilter(CampfireSanity, 1)], filtered_resolution=True) &
                    SpireHasShop(prefix, 4, options=[OptionFilter(ShopSanity, 1)], filtered_resolution=True)
    )
    world.set_rule(world.get_entrance(f"{prefix} Late Act 2"),SpireHasPower(offset,15) &
                   SpireHasShop(prefix, 5, options=[OptionFilter(ShopSanity, 1)], filtered_resolution=True)
                   )
    world.set_rule(world.get_entrance(f"{prefix} Act 2 Boss Arena"), SpireHasPower(offset,18) &
                    Has(f"{prefix} Progressive Smith", count=2, options=[OptionFilter(CampfireSanity, 1)], filtered_resolution=True) &
                    SpireHasShop(prefix, 6, options=[OptionFilter(ShopSanity, 1)], filtered_resolution=True) &
                    Has(f"{prefix} Progressive Shop Remove", 2, options=[OptionFilter(ShopSanity, 1)], filtered_resolution=True) &
                    SpireHasGold(prefix, 150, options=[OptionFilter(GoldSanity, 1)], filtered_resolution=True)
    )
    world.set_rule(world.get_entrance(f"{prefix} Early Act 3"), SpireHasPower(offset,19))
    world.set_rule(world.get_entrance(f"{prefix} Mid Act 3"), SpireHasPower(offset,21) &
                   Has(f"{prefix} Progressive Rest", count=3, options=[OptionFilter(CampfireSanity, 1)], filtered_resolution=True) &
                   SpireHasShop(prefix, 8, options=[OptionFilter(ShopSanity, 1)], filtered_resolution=True)
    )
    world.set_rule(world.get_entrance(f"{prefix} Late Act 3"),SpireHasPower(offset,24) &
                   SpireHasShop(prefix, 10, options=[OptionFilter(ShopSanity, 1)], filtered_resolution=True)
                   )
    world.set_rule(world.get_entrance(f"{prefix} Act 3 Boss Arena"), SpireHasPower(offset,27) &
                   Has(f"{prefix} Progressive Smith", count=3, options=[OptionFilter(CampfireSanity, 1)], filtered_resolution=True) &
                   SpireHasShop(prefix, 10, options=[OptionFilter(ShopSanity, 1)], filtered_resolution=True) &
                   Has(f"{prefix} Progressive Shop Remove", 3, options=[OptionFilter(ShopSanity, 1)], filtered_resolution=True) &
                   SpireHasGold(prefix, 270, options=[OptionFilter(GoldSanity, 1)], filtered_resolution=True)
    )

    if world.options.shop_sanity:
        world.set_rule(world.get_entrance(f"{prefix} Act 1 Shop"),
                 SpireHasGold(prefix, 50))

        world.set_rule(world.get_entrance(f"{prefix} Act 2 Shop"),
                       SpireHasGold(prefix, 150))

        world.set_rule(world.get_entrance(f"{prefix} Act 2 Shop"),
                       SpireHasGold(prefix, 270))

