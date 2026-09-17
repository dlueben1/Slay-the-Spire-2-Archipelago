"""Optional shared-slot progression prefill through Archipelago's fill hook.

Each batch removes one item per numbered player from the assumed inventory.
Every placement still passes the normal item and access rules. Constrained
options use the framework filler, as does an attempt that cannot finish.
"""

import logging
from collections import deque

from BaseClasses import Item, Location, LocationProgressType, MultiWorld
from Fill import sweep_from_pool

from .coop import PLAYER_OFFSET


def fill_shared_slots(multiworld: MultiWorld, worlds, progression: list[Item],
                      locations: list[Location]) -> bool:
    eligible = {world.player for world in worlds
                if world.options.player_count.value > 1
                and world.options.accessibility == "full"
                and world.options.include_floor_checks.value}
    if not eligible or multiworld.groups or any(
            location.progress_type != LocationProgressType.DEFAULT for location in locations):
        return False

    owned = [item for item in progression if item.player in eligible]
    if not owned:
        return False
    other_items = [item for item in progression if item.player not in eligible]
    queues: dict[tuple[int, int], deque[Item]] = {}
    for item in owned:
        if item.code is None:
            return False
        queues.setdefault((item.player, item.code // PLAYER_OFFSET + 1), deque()).append(item)

    # Only local lists and copied exploration states change during an attempt.
    # Keep the caller's already shuffled order for the framework fallback.
    remaining_items = list(owned)
    remaining_locations = list(locations)
    placements: list[tuple[Location, Item]] = []
    random_state = multiworld.random.getstate()
    committed = False
    try:
        base_state = sweep_from_pool(multiworld.state, other_items)
        while remaining_items:
            batch = [queue.pop() for queue in queues.values() if queue]
            batch_ids = {id(item) for item in batch}
            remaining_items = [item for item in remaining_items if id(item) not in batch_ids]
            maximum_state = sweep_from_pool(base_state, remaining_items)
            for item in batch:
                for index, location in enumerate(remaining_locations):
                    if location.can_fill(maximum_state, item, check_access=True):
                        remaining_locations.pop(index)
                        placements.append((location, item))
                        multiworld.push_item(location, item, collect=False)
                        break
                else:
                    logging.info("[StS2AP Fill] Batched prefill stalled after %s/%s items; "
                                 "restoring the original fill.", len(placements), len(owned))
                    return False

        progression[:] = other_items
        locations[:] = remaining_locations
        committed = True
        logging.info("[StS2AP Fill] Batched %s progression items across %s numbered players.",
                     len(owned), len(queues))
        return True
    finally:
        if not committed:
            for location, item in placements:
                location.item = None
                item.location = None
            multiworld.random.setstate(random_state)
