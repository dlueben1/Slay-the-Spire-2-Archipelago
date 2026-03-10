from typing import TYPE_CHECKING, List

from BaseClasses import CollectionState
from .characters import CharacterConfig
from ..AutoWorld import LogicMixin

if TYPE_CHECKING:
    from .world import SlayTheSpire2World

# TODO: refactor with rule builder?
class SpireLogic(LogicMixin):

    def _spire_has_victories(self: CollectionState, player: int, configs: List['CharacterConfig'], world: 'SlayTheSpire2World'):
        num_chars_goal = world.options.num_chars_goal.value
        count = 0
        for config in configs:
            if self.has(f"{config.name} Victory", player):
                count += 1
        if num_chars_goal == 0:
            return count >= len(configs)
        else:
            return count >= num_chars_goal

def set_rules(world: 'SlayTheSpire2World', player: int):
    multiworld = world.multiworld
    # for config in world.characters:
    #     _set_rules(world, player, config)

    multiworld.completion_condition[player] = lambda state: state._spire_has_victories(player, world.characters, world)
