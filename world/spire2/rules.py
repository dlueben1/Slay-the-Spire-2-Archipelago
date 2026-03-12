import dataclasses
import typing
from collections import defaultdict
from typing import TYPE_CHECKING, List

from BaseClasses import CollectionState, MultiWorld, Item
from NetUtils import JSONMessagePart
from rule_builder.options import OptionFilter
from rule_builder.rules import HasFromList, Rule, TWorld, True_, Has, HasFromListUnique
from .characters import CharacterConfig
from .items import ItemType
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
        # print("copy")
        # print(self.power_level)
        for k,v in self.power_level.items():
            new_char_pl = defaultdict(float)
            new_state.power_level[k] = new_char_pl
            for ik, iv in v.items():
                new_char_pl[ik] = iv
            # new_state.power_level[k]
        # new_state.power_level = {
        # k: defaultdict(int,{inner: inner_v for inner, inner_v in v.items() }) for k,v in self.power_level.items()
        # }
        new_state.item_levels = {
            k: {inner: inner_v for inner, inner_v in v.items() } for k,v in self.item_levels.items()
        }
        # print(self.power_level)
        # print(new_state.power_level)
        # print(self.item_levels)
        # print(new_state.item_levels)
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
                    "type": "text", "text": f"Has power level {self.power_level}"
                }
            ]


def set_rules(world: 'SlayTheSpire2World') -> None:
    for config in world.characters:
        _set_rules(world, config)

    world.set_completion_rule(HasFromListUnique(*[f"{config.name} Victory" for config in world.characters],
                                          count=world.options.num_chars_goal.value))

def _set_rules(world: 'SlayTheSpire2World', config: CharacterConfig) -> None:
    prefix = config.name
    offset = config.char_offset
    world.set_rule(world.get_entrance(f"{prefix} Early Act 1"), Has(f"{prefix} Unlock"))
    world.set_rule(world.get_entrance(f"{prefix} Mid Act 1"), SpireHasPower(offset,3)) # 2 CR
    world.set_rule(world.get_entrance(f"{prefix} Late Act 1"),SpireHasPower(offset,6)) # 3 CR 2 R
    world.set_rule(world.get_entrance(f"{prefix} Act 1 Boss Arena"), SpireHasPower(offset,9)) # 4 CR 3 R
    world.set_rule(world.get_entrance(f"{prefix} Early Act 2"), SpireHasPower(offset,10)) # 4 CR 3 R 1 RR
    world.set_rule(world.get_entrance(f"{prefix} Mid Act 2"), SpireHasPower(offset,12))
    world.set_rule(world.get_entrance(f"{prefix} Late Act 2"),SpireHasPower(offset,15))
    world.set_rule(world.get_entrance(f"{prefix} Act 2 Boss Arena"), SpireHasPower(offset,18))
    world.set_rule(world.get_entrance(f"{prefix} Early Act 3"), SpireHasPower(offset,19))
    world.set_rule(world.get_entrance(f"{prefix} Mid Act 3"), SpireHasPower(offset,21))
    world.set_rule(world.get_entrance(f"{prefix} Late Act 3"),SpireHasPower(offset,24))
    world.set_rule(world.get_entrance(f"{prefix} Act 3 Boss Arena"), SpireHasPower(offset,27))


