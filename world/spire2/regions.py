from math import ceil, floor
from typing import TYPE_CHECKING, List

from BaseClasses import Region
from .logic_table import ELITES_PER_ACT, RELIC_REGIONS

if TYPE_CHECKING:
    from worlds.spire2.world import SlayTheSpire2World
    from worlds.spire2.characters import CharacterConfig


def create_regions(world: 'SlayTheSpire2World', player: int):

    multiworld = world.multiworld

    neow = Region("Neow's Room", player, multiworld)
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
    ascension_mod = 1 if 'DoubleBoss'.lower() in config.ascension and 'DoubleBoss'.lower() not in config.ascension_down else 0
    # Old logic has no elite track, so it keeps the relic checks in their original path regions.
    two_tracks = world.effective_logic == "new"
    relic_regions = RELIC_REGIONS if two_tracks else LEGACY_RELIC_REGIONS

    def relics(region: str) -> List[str]:
        return [f"Relic {number}" for number, name in relic_regions.items() if name == region]

    def elite_track_entrances(act: int) -> List[str]:
        """Elite track regions entered from an act's Mid region: its chest and its first elite."""
        if not two_tracks:
            return []
        return ([f"Act {act} Chest"] if act > 1 else []) + [f"Act {act} Elite 1"]

    def create_elite_track(act: int) -> None:
        """One region per relic: the act's chest after the previous act's elites, then each elite in
        turn, entered from the one before."""
        if not two_tracks:
            return
        if act > 1:
            chest = f"Act {act} Chest"
            multiworld.regions.append(world.create_region(player, prefix, chest, config, relics(chest)))
        elites = ELITES_PER_ACT[act - 1]
        for number in range(1, elites + 1):
            elite = f"Act {act} Elite {number}"
            exits = [f"Act {act} Elite {number + 1}"] if number < elites else []
            multiworld.regions.append(world.create_region(player, prefix, elite, config, relics(elite), exits))

    # IMPORTANT: Keep these location ranges at the canonical singleplayer counts. Multiplayer
    # has one fewer physical floor per act and fewer associated reward opportunities; the client
    # compensates at deterministic boss-room boundaries. Reducing the APWorld counts here would
    # also reduce singleplayer and prevent one generated slot from supporting either play mode.
    # New logic ends Early Act 1 after the three weak hallway fights: the fourth fight's checks,
    # including the floor after it, are in the Act 1 Hallway.
    early_fights = 3 if two_tracks else 4
    first_char_region = world.create_region(player, prefix, 'Early Act 1', config,
                                            [
                                                "Press Start",
                                                "Potion Drop 1",
                                                "Act 1 Campfire 1",
                                                "Ancient Act 1",
                                                *_create_floor_check(1, early_fights + 2),
                                                *_create_combat_check(1, early_fights),
                                                *_create_card_rewards(1, early_fights, every_other)
                                            ],
                                            ["Act 1 Hallway" if two_tracks else "Mid Act 1", "Act 1 Shop"])

    neow.connect(first_char_region, first_char_region.name)
    multiworld.regions.append(first_char_region)

    multiworld.regions.append(world.create_region(player, prefix, "Act 1 Shop", config,
                                                  [f"Shop Slot {i}" for i in range(1,6)]))

    if two_tracks:
        multiworld.regions.append(world.create_region(player, prefix, "Act 1 Hallway", config,
                                                      [
                                                          *_create_floor_check(6, 6),
                                                          *_create_combat_check(4, 4),
                                                          *_create_card_rewards(4, 4, every_other),
                                                      ], ["Mid Act 1"]))

    multiworld.regions.append(world.create_region(player, prefix, 'Mid Act 1', config,
                                                  [
                                                      *relics('Mid Act 1'),
                                                      "Potion Drop 2",
                                                      "Act 1 Campfire 2",
                                                      *_create_floor_check(7, 11),
                                                      *_create_combat_check(5, 6),
                                                      *_create_card_rewards(5,6, every_other),
                                                  ],["Late Act 1", *elite_track_entrances(1)]))

    multiworld.regions.append(world.create_region(player, prefix, 'Late Act 1', config,
                                                  [
                                                      *relics('Late Act 1'),
                                                      "Potion Drop 3",
                                                      *_create_floor_check(12, 16),
                                                      *_create_combat_check(7, 8),
                                                      *_create_card_rewards(7,8, every_other)
                                                  ], ['Act 1 Boss Arena']))
    create_elite_track(1)

    multiworld.regions.append(world.create_region(player, prefix, 'Act 1 Boss Arena', config,
                                                  [
                                                      'Act 1 Boss',
                                                      'Rare Card Reward 1',
                                                      # 'Boss Relic 1',
                                                      'Boss Gold 1',
                                                      "Ancient Act 2",
                                                      * _create_floor_check(17, 17)
                                                  ], ['Early Act 2']))

    multiworld.regions.append(world.create_region(player, prefix, 'Early Act 2', config,
                                                  [
                                                      "Potion Drop 4",
                                                      *_create_floor_check(18, 22),
                                                      "Act 2 Campfire 1",
                                                      *_create_combat_check(9, 10),
                                                      *_create_card_rewards(9, 10, every_other),
                                                  ], ["Mid Act 2", "Act 2 Shop"]))

    multiworld.regions.append(world.create_region(player, prefix, "Act 2 Shop", config,
                                                  [f"Shop Slot {i}" for i in range(6,11)]))
    multiworld.regions.append(world.create_region(player, prefix, 'Mid Act 2', config,
                                                  [
                                                      *relics('Mid Act 2'),
                                                      "Potion Drop 5",
                                                      "Act 2 Campfire 2",
                                                      *_create_floor_check(23, 27),
                                                      *_create_combat_check(11, 12),
                                                      *_create_card_rewards(11, 12, every_other)
                                                  ], ["Late Act 2", *elite_track_entrances(2)]))

    multiworld.regions.append(world.create_region(player, prefix, 'Late Act 2', config,
                                                  [
                                                      *relics('Late Act 2'),
                                                      "Potion Drop 6",
                                                      *_create_floor_check(28, 32),
                                                      *_create_combat_check(13, 14),
                                                      *_create_card_rewards(13, 14, every_other)
                                                  ], ['Act 2 Boss Arena']))
    create_elite_track(2)

    multiworld.regions.append(world.create_region(player, prefix, 'Act 2 Boss Arena', config,
                                                  [
                                                      'Act 2 Boss',
                                                      'Rare Card Reward 2',
                                                      # 'Boss Relic 2',
                                                      'Boss Gold 2',
                                                      "Ancient Act 3",
                                                      *_create_floor_check(33, 33),
                                                  ], ['Early Act 3']))

    multiworld.regions.append(world.create_region(player, prefix, 'Early Act 3', config,
                                                  [
                                                      "Potion Drop 7",
                                                      "Act 3 Campfire 1",
                                                      *_create_floor_check(34, 38),
                                                      *_create_combat_check(15, 16),
                                                      *_create_card_rewards(15, 16, every_other)
                                                  ], ["Mid Act 3", "Act 3 Shop"]))

    multiworld.regions.append(world.create_region(player, prefix, "Act 3 Shop", config,
                                                  [f"Shop Slot {i}" for i in range(11,17)]))

    multiworld.regions.append(world.create_region(player, prefix, 'Mid Act 3', config,
                                                  [
                                                      *relics('Mid Act 3'),
                                                      "Potion Drop 8",
                                                      "Act 3 Campfire 2",
                                                      *_create_floor_check(39, 43),
                                                      *_create_combat_check(17, 18),
                                                      *_create_card_rewards(17, 18, every_other)
                                                  ], ["Late Act 3", *elite_track_entrances(3)]))


    multiworld.regions.append(world.create_region(player, prefix, 'Late Act 3', config,
                                                  [
                                                      *relics('Late Act 3'),
                                                      "Potion Drop 9",
                                                      *_create_floor_check(44, 47),
                                                      *_create_combat_check(19, 20),
                                                      *_create_card_rewards(19, 20, every_other)
                                                  ], ['Act 3 Boss Arena']))
    create_elite_track(3)


    multiworld.regions.append(world.create_region(player, prefix, 'Act 3 Boss Arena', config,
                                                  [
                                                      "Act 3 Boss",
                                                      *_create_floor_check(48, 48 + ascension_mod),
                                                  ]))

# Relic placement before the elite track. Old logic gates these path regions by power alone.
LEGACY_RELIC_REGIONS = {
    1: "Mid Act 1", 2: "Mid Act 1", 3: "Late Act 1",
    4: "Mid Act 2", 5: "Mid Act 2", 6: "Late Act 2",
    7: "Mid Act 3", 8: "Mid Act 3", 9: "Late Act 3", 10: "Late Act 3",
}


def _create_floor_check(start: int, end: int) -> List[str]:
    return [f"Reached Floor {i}" for i in range(start, end + 1)]

def _create_card_rewards(start: int, end: int, every_other: bool) -> List[str]:
    """The Card Reward checks of combat card rewards start to end. With half shuffle the client
    makes every odd one a check, so Card Reward N is the run's card reward 2N - 1."""
    if every_other:
        start = ceil((start + 1) / 2)
        end = floor((end + 1) / 2)
    return [f"Card Reward {i}" for i in range(start, end + 1)]

def _create_combat_check(start: int, end: int) -> List[str]:
    return [f"Combat Gold {i}" for i in range(start, end + 1)]
