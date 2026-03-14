from math import ceil, floor
from typing import TYPE_CHECKING, List

from BaseClasses import Region

if TYPE_CHECKING:
    from worlds.spire2.world import SlayTheSpire2World
    from worlds.spire2.characters import CharacterConfig


def create_regions(world: 'SlayTheSpire2World', player: int):

    multiworld = world.multiworld

    neow = world.create_region(player, None, "Neow's Room", None)
    multiworld.regions.append(neow)

    for config in world.characters:
        _create_regions(world, player, config, neow)

    for region in multiworld.get_regions(player):
        if region.name == "Neow's Room":
            continue
        entrance = world.get_entrance(region.name)
        entrance.connect(region)


def _create_regions(world: 'SlayTheSpire2World', player: int, config: 'CharacterConfig', neow: Region) -> None:
    prefix = config.name
    multiworld = world.multiworld
    every_other = not world.options.shuffle_all_cards
    # TODO: update for ascension down?
    ascension_mod = 0 if config.ascension <= 9 else 1
    first_char_region = world.create_region(player, prefix, 'Early Act 1', config,
                                            [
                                                "Press Start",
                                                # "Card Reward 1",
                                                # "Card Reward 2",
                                                "Potion Drop 1",
                                                # "Ruby Key",
                                                # *_create_campfire_check(1),
                                                *_create_floor_check(1,6),
                                                *_create_combat_check(1,4),
                                                *_create_card_rewards(1, 4, every_other)
                                            ],
                                            ["Mid Act 1", "Act 1 Shop"])

    neow.connect(first_char_region, first_char_region.name)
    multiworld.regions.append(first_char_region)

    multiworld.regions.append(world.create_region(player, prefix, "Act 1 Shop", config,
                                                  [f"Shop Slot {i}" for i in range(1,6)]))

    multiworld.regions.append(world.create_region(player, prefix, 'Mid Act 1', config,
                                                  [
                                                      # 'Card Reward 3',
                                                      # 'Card Reward 4',
                                                      'Relic 1',
                                                      'Relic 2',
                                                      "Potion Drop 2",
                                                      # "Sapphire Key",
                                                      *_create_floor_check(7, 11),
                                                      *_create_combat_check(5, 6),
                                                      *_create_card_rewards(5,6, every_other)
                                                  ],["Late Act 1"]))

    multiworld.regions.append(world.create_region(player, prefix, 'Late Act 1', config,
                                                  [
                                                      'Relic 3',
                                                      "Potion Drop 3",
                                                      *_create_floor_check(12, 16),
                                                      *_create_combat_check(7, 8),
                                                      *_create_card_rewards(7,8, every_other)
                                                  ], ['Act 1 Boss Arena']))

    multiworld.regions.append(world.create_region(player, prefix, 'Act 1 Boss Arena', config,
                                                  [
                                                      'Act 1 Boss',
                                                      'Rare Card Reward 1',
                                                      # 'Boss Relic 1',
                                                      'Boss Gold 1',
                                                      * _create_floor_check(17, 17)
                                                  ], ['Early Act 2']))

    multiworld.regions.append(world.create_region(player, prefix, 'Early Act 2', config,
                                                  [
                                                      # "Card Reward 5",
                                                      # "Card Reward 6",
                                                      "Potion Drop 4",
                                                      # *_create_campfire_check(2),
                                                      *_create_floor_check(18, 22),
                                                      # *_create_combat_check(9, 11),
                                                      *_create_combat_check(9, 10),
                                                      *_create_card_rewards(9, 10, every_other),
                                                  ], ["Mid Act 2", "Act 2 Shop"]))

    multiworld.regions.append(world.create_region(player, prefix, "Act 2 Shop", config,
                                                  [f"Shop Slot {i}" for i in range(6,11)]))
    multiworld.regions.append(world.create_region(player, prefix, 'Mid Act 2', config,
                                                  [
                                                      # 'Card Reward 7',
                                                      'Relic 4',
                                                      'Relic 5',
                                                      "Potion Drop 5",
                                                      *_create_floor_check(23, 27),
                                                      *_create_combat_check(11, 12),
                                                      *_create_card_rewards(11, 12, every_other)
                                                  ], ["Late Act 2"]))

    multiworld.regions.append(world.create_region(player, prefix, 'Late Act 2', config,
                                                  [
                                                      # 'Card Reward 8',
                                                      'Relic 6',
                                                      "Potion Drop 6",
                                                      *_create_floor_check(28, 32),
                                                      *_create_combat_check(13, 14),
                                                      *_create_card_rewards(13, 14, every_other)
                                                  ], ['Act 2 Boss Arena']))

    multiworld.regions.append(world.create_region(player, prefix, 'Act 2 Boss Arena', config,
                                                  [
                                                      'Act 2 Boss',
                                                      'Rare Card Reward 2',
                                                      # 'Boss Relic 2',
                                                      'Boss Gold 2',
                                                      *_create_floor_check(33, 33),
                                                  ], ['Early Act 3']))

    multiworld.regions.append(world.create_region(player, prefix, 'Early Act 3', config,
                                                  [
                                                      # "Card Reward 9",
                                                      # "Card Reward 10",
                                                      "Potion Drop 7",
                                                      # *_create_campfire_check(3),
                                                      *_create_floor_check(34, 38),
                                                      *_create_combat_check(15, 16),
                                                      *_create_card_rewards(15, 16, every_other)
                                                  ], ["Mid Act 3", "Act 3 Shop"]))

    multiworld.regions.append(world.create_region(player, prefix, "Act 3 Shop", config,
                                                  [f"Shop Slot {i}" for i in range(11,17)]))

    multiworld.regions.append(world.create_region(player, prefix, 'Mid Act 3', config,
                                                  [
                                                      # "Card Reward 11",
                                                      "Relic 7",
                                                      "Relic 8",
                                                      "Potion Drop 8",
                                                      *_create_floor_check(39, 43),
                                                      *_create_combat_check(17, 18),
                                                      *_create_card_rewards(17, 18, every_other)
                                                  ], ["Late Act 3"]))


    multiworld.regions.append(world.create_region(player, prefix, 'Late Act 3', config,
                                                  [
                                                      # "Card Reward 12",
                                                      # "Card Reward 13",
                                                      "Relic 9",
                                                      "Relic 10",
                                                      "Potion Drop 9",
                                                      # "Emerald Key",
                                                      *_create_floor_check(44, 47),
                                                      *_create_combat_check(19, 20),
                                                      *_create_card_rewards(19, 20, every_other)
                                                  ], ['Act 3 Boss Arena']))


    multiworld.regions.append(world.create_region(player, prefix, 'Act 3 Boss Arena', config,
                                                  [
                                                      "Act 3 Boss",
                                                      *_create_floor_check(48, 48 + ascension_mod),
                                                  ]))

def _create_floor_check(start: int, end: int) -> List[str]:
    return [f"Reached Floor {i}" for i in range(start, end + 1)]

def _create_card_rewards(start: int, end: int, every_other: bool) -> List[str]:
    if every_other:
        start =  ceil(start/2)
        end =  floor(end/2)
    return [f"Card Reward {i}" for i in range(start, end + 1)]

def _create_combat_check(start: int, end: int) -> List[str]:
    return [f"Combat Gold {i}" for i in range(start, end + 1)]

