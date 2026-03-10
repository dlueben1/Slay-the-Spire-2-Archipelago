START_ID = 900000

# Build the "Reach Floor X" locations for each Act
def build_act_locations(act_number: int, floor_count: int, start_id: int) -> dict[str, int]:
    locations: dict[str, int] = {}

    next_id = start_id
    for floor in range(1, floor_count + 1):
        locations[f"Act {act_number} - Reach Floor {floor}"] = next_id
        next_id += 1

    locations[f"Act {act_number} - Defeat Boss"] = next_id
    return locations

# Temporary for testing, will be replaced later
ACT_1_FLOOR_COUNT = 15

# Build the final location table
location_table = build_act_locations(1, ACT_1_FLOOR_COUNT, START_ID)