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

from worlds.spire2.characters import character_list
from worlds.spire2.constants import CHAR_OFFSET, NUM_CUSTOM

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
    Boss_Relic = auto()
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
    boss: bool = False

def create_location_data() -> typing.List[LocationData]:
    return (

            [LocationData(f"Card Reward {j}", j, LocationType.Card_Reward) for j in range(1, MAX_CARD_REWARDS + 1)] +
            [LocationData(f"Relic {j}", j + 26, LocationType.Relic) for j in range(1, 11)] +
            [LocationData(f"Shop Slot {j}", j + 36, LocationType.Shop)  for j in range(1,17)] +
            # [LocationData(f"Combat Gold {j}", j + 52, LocationType.Gold) for j in range(1,27)] +
            [LocationData(f"Potion Drop {j}", j + 78, LocationType.Potion) for j in range(1,10)] +
            [LocationData('Press Start', 88, LocationType.Start),
             # LocationData('Act 1 Campfire 1', 89, LocationType.Campfire),
             # LocationData('Act 1 Campfire 2', 90, LocationType.Campfire),
             # LocationData('Act 2 Campfire 1', 91, LocationType.Campfire),
             # LocationData('Act 2 Campfire 2', 92, LocationType.Campfire),
             # LocationData('Act 3 Campfire 1', 93, LocationType.Campfire),
             # LocationData('Act 3 Campfire 2', 94, LocationType.Campfire),
             LocationData('Rare Card Reward 1', 95, LocationType.Rare_Card_Reward, True),
             LocationData('Rare Card Reward 2', 96, LocationType.Rare_Card_Reward, True),
             # LocationData('Boss Relic 1', 97, LocationType.Boss_Relic, True),
             # LocationData('Boss Relic 2', 98, LocationType.Boss_Relic, True),
             # LocationData('Boss Gold 1', 99, LocationType.Gold, True),
             # LocationData('Boss Gold 2', 100, LocationType.Gold, True),
             LocationData('Act 1 Boss', None, LocationType.Event),
             LocationData('Act 2 Boss', None, LocationType.Event),
             LocationData('Act 3 Boss', None, LocationType.Event),
             ] +
            [LocationData(f"Reached Floor {j}", 100 + j, LocationType.Floor) for j in range(1, 48)]
    )

def create_location_tables(vanilla_chars: typing.List[str], extras: int) -> typing.Tuple[dict[str, int], dict[
    typing.Union[str, int],dict[str,LocationData]],dict[int,LocationData]]:
    loc_name_to_id = dict()
    characters_to_locs: dict[typing.Union[str, int],dict[str, LocationData]] = defaultdict(lambda: dict())
    ids_to_data: dict[int, LocationData] = dict()
    char_num = 0

    base_location_data = create_location_data()

    ids = { x.id for x in base_location_data if x.id is not None}
    assert len(ids) == (len(base_location_data) - 3), f"{len(ids)} != {len(base_location_data)}"
    for char in vanilla_chars:
        for data in base_location_data:
            newkey = f"{char} {data.name}"
            newval = data.id + char_num*CHAR_OFFSET if data.type != LocationType.Event else data.id
            loc_name_to_id[newkey] = newval
            characters_to_locs[char][newkey] = data
            if newval is not None:
                ids_to_data[newval] = data
        char_num += 1

    for i in range(extras):
        for data in base_location_data:
            newkey = f"Custom Character {i+1} {data.name}"
            newval = data.id + char_num * CHAR_OFFSET if data.type != LocationType.Event else data.id
            loc_name_to_id[newkey] = newval
            characters_to_locs[i+1][newkey] = data
            if newval is not None:
                ids_to_data[newval] = data
        char_num += 1

    return loc_name_to_id, characters_to_locs, ids_to_data

location_table, characters_to_locs, loc_ids_to_data = create_location_tables(character_list, NUM_CUSTOM)
