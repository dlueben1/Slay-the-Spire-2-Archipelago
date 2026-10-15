# START_ID = 900000
#
# # Build the "Reach Floor X" locations for each Act
# def build_act_locations(act_number: int, floor_count: int, start_id: int) -> dict[str, int]:
#     locations: dict[str, int] = {}
#
#     next_id = start_id
#     for floor in range(1, floor_count + 1):
#         locations[f"Act {act_number} - Reach Floor {floor}"] = next_id
#         next_id += 1
#
#     locations[f"Act {act_number} - Defeat Boss"] = next_id
#     return locations
#
# # Temporary for testing, will be replaced later
# ACT_1_FLOOR_COUNT = 15
#
# # Build the final location table
# location_table = build_act_locations(1, ACT_1_FLOOR_COUNT, START_ID)
import typing
from collections import defaultdict
from enum import Enum, auto

from worlds.spire2.characters import CharacterDefinition, character_registry
from worlds.spire2.constants import CHAR_OFFSET

MAX_CARD_REWARDS = 20
# 17 floors act 1
# 16 floors act 2
# 15 floors act 3
# for each act, 4 floors are accounted for
# so that leaves 13, 12, 11 = 36 floors
# 7 7 6

class LocationType(Enum):
    Card_Reward = auto()
    Rare_Card_Reward = auto()
    Relic = auto()
    Ancient = auto()
    Floor = auto()
    Campfire = auto()
    Event = auto()
    Shop = auto()
    Start = auto()
    Gold = auto()
    Potion = auto()
    Key = auto()


class LocationData(typing.NamedTuple):
    name: str
    id: typing.Optional[int]
    type: LocationType
    act: int
    boss: bool = False


def _act_for_index(index: int, act_one_end: int, act_two_end: int) -> int:
    if index <= act_one_end:
        return 1
    if index <= act_two_end:
        return 2
    return 3

def create_location_data() -> typing.List[LocationData]:
    return (

            [LocationData(f"Card Reward {j}", j, LocationType.Card_Reward, _act_for_index(j, 8, 14))
             for j in range(1, MAX_CARD_REWARDS + 1)] +
            [LocationData(f"Relic {j}", j + 26, LocationType.Relic, _act_for_index(j, 3, 6))
             for j in range(1, 11)] +
            [LocationData(f"Shop Slot {j}", j + 36, LocationType.Shop, _act_for_index(j, 5, 10))
             for j in range(1, 17)] +
            [LocationData(f"Combat Gold {j}", j + 53, LocationType.Gold, _act_for_index(j, 8, 14))
             for j in range(1, 21)] +
            [LocationData(f"Potion Drop {j}", j + 78, LocationType.Potion, _act_for_index(j, 3, 6))
             for j in range(1, 10)] +
            [LocationData('Press Start', 88, LocationType.Start, 1),
             LocationData('Act 1 Campfire 1', 89, LocationType.Campfire, 1),
             LocationData('Act 1 Campfire 2', 90, LocationType.Campfire, 1),
             LocationData('Act 2 Campfire 1', 91, LocationType.Campfire, 2),
             LocationData('Act 2 Campfire 2', 92, LocationType.Campfire, 2),
             LocationData('Act 3 Campfire 1', 93, LocationType.Campfire, 3),
             LocationData('Act 3 Campfire 2', 94, LocationType.Campfire, 3),
             LocationData('Rare Card Reward 1', 95, LocationType.Rare_Card_Reward, 1, True),
             LocationData('Rare Card Reward 2', 96, LocationType.Rare_Card_Reward, 2, True),
             LocationData('Boss Gold 1', 99, LocationType.Gold, 1, True),
             LocationData('Boss Gold 2', 100, LocationType.Gold, 2, True),
             LocationData('Ancient Act 1', 151, LocationType.Ancient, 1),
             LocationData('Ancient Act 2', 152, LocationType.Ancient, 1, True),
             LocationData('Ancient Act 3', 153, LocationType.Ancient, 2, True),
             LocationData('Act 1 Boss', None, LocationType.Event, 1, True),
             LocationData('Act 2 Boss', None, LocationType.Event, 2, True),
             LocationData('Act 3 Boss', None, LocationType.Event, 3, True),
             ] +
            [LocationData(f"Reached Floor {j}", 100 + j, LocationType.Floor, _act_for_index(j, 17, 33))
             for j in range(1, 50)]
    )

def create_location_tables(registry: dict[int, CharacterDefinition]) -> tuple[
        dict[str, typing.Optional[int]], dict[str, dict[str, LocationData]], dict[int, LocationData]]:
    loc_name_to_id = {}
    character_locations: dict[str, dict[str, LocationData]] = defaultdict(dict)
    ids_to_data = {}
    base_location_data = create_location_data()
    ids = {data.id for data in base_location_data if data.id is not None}
    assert len(ids) == sum(data.id is not None for data in base_location_data)
    for block, definition in registry.items():
        for data in base_location_data:
            name = f"{definition.name} {data.name}"
            location_id = data.id + (block - 1) * CHAR_OFFSET if data.id is not None else None
            loc_name_to_id[name] = location_id
            character_locations[definition.name][name] = data
            if location_id is not None:
                ids_to_data[location_id] = data
    return loc_name_to_id, character_locations, ids_to_data


location_table, characters_to_locs, loc_ids_to_data = create_location_tables(character_registry)


def create_location_groups(
        characters_to_locations: dict[str, dict[str, LocationData]],
) -> dict[str, typing.Set[str]]:
    groups: dict[str, typing.Set[str]] = {
        "Act 1": set(),
        "Act 2": set(),
        "Act 3": set(),
        "Act 1 Boss": set(),
        "Act 2 Boss": set(),
    }

    for character_name, character_locations in characters_to_locations.items():
        character_act_groups = {
            act: groups.setdefault(f"{character_name} Act {act}", set()) for act in range(1, 4)
        }
        character_boss_groups = {
            act: groups.setdefault(f"{character_name} Act {act} Boss", set()) for act in range(1, 3)
        }

        for location_name, location_data in character_locations.items():
            if location_data.id is None:
                continue

            # shuffle_all_cards changes which act uses Card Reward 5 through 10. Name groups are static,
            # so numbered card rewards cannot be assigned to an act accurately for every option value.
            if location_data.type == LocationType.Card_Reward:
                continue

            groups[f"Act {location_data.act}"].add(location_name)
            character_act_groups[location_data.act].add(location_name)
            if location_data.boss and location_data.act in character_boss_groups:
                groups[f"Act {location_data.act} Boss"].add(location_name)
                character_boss_groups[location_data.act].add(location_name)

    return groups


location_groups = create_location_groups(characters_to_locs)
