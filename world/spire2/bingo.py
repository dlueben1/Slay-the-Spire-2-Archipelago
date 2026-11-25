"""The bingo goal: dealing a board from the square catalog, its region and its logic."""
import logging
from typing import TYPE_CHECKING, Any, Dict, FrozenSet, List, Optional, Sequence, Tuple

from BaseClasses import ItemClassification, LocationProgressType, Region
from Options import OptionError
from rule_builder.rules import Has, HasAny, Rule
from .bingo_squares import BINGO_CONFLICTS, BINGO_SQUARES, BingoSquare
from .items import ItemData, ItemType

if TYPE_CHECKING:
    from .characters import CharacterConfig
    from .world import SlayTheSpire2World

BINGO_REGION = "Bingo Board"
BINGO_LINE_ITEM = "Bingo Line"
BINGO_LINE_ITEM_DATA = ItemData(None, ItemType.EVENT, ItemClassification.progression, True)

# Share of the board each tier gets, before rounding. Tune freely; a tier the roster runs short of
# is made up from the others.
TIER_WEIGHTS: Dict[str, float] = {"E": 0.4, "M": 0.4, "H": 0.2}

# A square is in logic once a character who can do it beats the boss of the square's deepest act.
TIER_EVENTS: Dict[str, str] = {
    "E": "Beat Act 1 Boss",
    "M": "Beat Act 2 Boss",
    "H": "Victory",
}

# Each placement attempt reshuffles the board, and a failed one swaps out the square it could not place.
MAX_PLACEMENT_ATTEMPTS = 200

SQUARES_BY_KEY: Dict[str, BingoSquare] = {square.key: square for square in BINGO_SQUARES}
BINGO_LOCATION_NAME_TO_ID: Dict[str, int] = {square.location_name: square.location_id for square in BINGO_SQUARES}

CONFLICTS_BY_KEY: Dict[str, FrozenSet[str]] = {
    square.key: frozenset(
        [b for a, b in BINGO_CONFLICTS if a == square.key] + [a for a, b in BINGO_CONFLICTS if b == square.key])
    for square in BINGO_SQUARES
}


def board_text(square: BingoSquare) -> str:
    """The square's wording on the board; a character's own squares name the character."""
    if square.key.startswith("C-") and square.characters:
        return f"{square.characters[0]}: {square.text}"
    return square.text


def board_lines(size: int) -> List[Tuple[str, Tuple[int, ...]]]:
    """Every row, column and main diagonal as (name, row-major cell indices)."""
    lines = [(f"Row {row + 1}", tuple(row * size + column for column in range(size))) for row in range(size)]
    lines += [(f"Column {column + 1}", tuple(row * size + column for row in range(size))) for column in range(size)]
    lines.append(("Diagonal 1", tuple(index * size + index for index in range(size))))
    lines.append(("Diagonal 2", tuple(index * size + size - 1 - index for index in range(size))))
    return lines


def line_location_name(line: str) -> str:
    return f"Bingo Line {line}"


def max_lines(size: int) -> int:
    return 2 * size + 2


def roster_can_do(square: BingoSquare, roster: Sequence['CharacterConfig']) -> List['CharacterConfig']:
    """The roster characters who can do this square. Squares naming characters only match vanilla
    ones; modded characters count for squares anyone can do."""
    if square.characters is None:
        return list(roster)
    return [config for config in roster if config.mod_num == 0 and config.name in square.characters]


def requirement_met(requirement: Optional[str], world: 'SlayTheSpire2World') -> bool:
    options = world.options
    if requirement is None:
        return True
    if requirement == "no_shop_sanity":
        return not options.shop_sanity
    if requirement.startswith("boon:"):
        return requirement.removeprefix("boon:") in options.boons.value
    if requirement.startswith("run_modifier:"):
        return requirement.removeprefix("run_modifier:") in options.run_modifiers.value
    if requirement == "dread_mode":
        return bool(options.dread_mode)
    if requirement == "blitz_merchants":
        return options.blitz_merchant_chance.value > 0
    raise ValueError(f"Unknown bingo square requirement '{requirement}'")


def candidate_squares(world: 'SlayTheSpire2World') -> List[BingoSquare]:
    return [square for square in BINGO_SQUARES
            if roster_can_do(square, world.characters) and requirement_met(square.requires, world)]


def tier_counts(cells: int, available: Dict[str, int]) -> Dict[str, int]:
    """Squares per tier: the weights rounded by largest remainder, then any tier's shortfall moved
    to the other tiers in weight order."""
    exact = {tier: cells * weight for tier, weight in TIER_WEIGHTS.items()}
    counts = {tier: int(share) for tier, share in exact.items()}
    by_remainder = sorted(TIER_WEIGHTS, key=lambda tier: exact[tier] - counts[tier], reverse=True)
    for tier in by_remainder[:cells - sum(counts.values())]:
        counts[tier] += 1
    shortfall = 0
    for tier in TIER_WEIGHTS:
        if counts[tier] > available.get(tier, 0):
            shortfall += counts[tier] - available.get(tier, 0)
            counts[tier] = available.get(tier, 0)
    for tier in sorted(TIER_WEIGHTS, key=TIER_WEIGHTS.get, reverse=True):
        extra = min(shortfall, available.get(tier, 0) - counts[tier])
        counts[tier] += extra
        shortfall -= extra
    return counts


def deal_board(world: 'SlayTheSpire2World', size: int) -> List[BingoSquare]:
    """Deals size x size squares, row-major, with no conflicting pair sharing a line."""
    random = world.random
    cells = size * size
    candidates = candidate_squares(world)
    if len(candidates) < cells:
        raise OptionError(f"Slay the Spire II player {world.player_name}: only {len(candidates)} bingo squares fit "
                          f"this roster and these settings, too few for a {size}x{size} board.")
    by_tier: Dict[str, List[BingoSquare]] = {tier: [] for tier in TIER_WEIGHTS}
    for square in candidates:
        by_tier[square.tier].append(square)
    counts = tier_counts(cells, {tier: len(squares) for tier, squares in by_tier.items()})
    chosen: List[BingoSquare] = []
    spares: Dict[str, List[BingoSquare]] = {}
    for tier, squares in by_tier.items():
        shuffled = random.sample(squares, len(squares))
        chosen += shuffled[:counts[tier]]
        spares[tier] = shuffled[counts[tier]:]

    lines_of_cell: List[List[Tuple[int, ...]]] = [[] for _ in range(cells)]
    for _, line in board_lines(size):
        for cell in line:
            lines_of_cell[cell].append(line)

    for _ in range(MAX_PLACEMENT_ATTEMPTS):
        board, unplaced = place_squares(random, chosen, size, lines_of_cell)
        if board is not None:
            return board
        # Swap the square that found no cell for a spare, preferring one of its tier with no
        # conflict on the board.
        chosen_keys = {square.key for square in chosen}
        pool = spares[unplaced.tier] or [square for tier_spares in spares.values() for square in tier_spares]
        if not pool:
            continue
        clean = [square for square in pool if not CONFLICTS_BY_KEY[square.key] & chosen_keys]
        replacement = random.choice(clean or pool)
        spares[replacement.tier].remove(replacement)
        spares[unplaced.tier].append(unplaced)
        chosen[chosen.index(unplaced)] = replacement
    raise OptionError(f"Slay the Spire II player {world.player_name}: could not deal a {size}x{size} bingo board "
                      "without conflicting squares in a line.")


def place_squares(random, chosen: List[BingoSquare], size: int, lines_of_cell: List[List[Tuple[int, ...]]]
                  ) -> Tuple[Optional[List[BingoSquare]], Optional[BingoSquare]]:
    """Places the squares in random cells, most conflicted first, so no conflicting pair shares a
    line. Returns the board, or None and the first square that found no cell."""
    order = random.sample(chosen, len(chosen))
    order.sort(key=lambda square: len(CONFLICTS_BY_KEY[square.key]), reverse=True)
    cells = random.sample(range(size * size), size * size)
    board: List[Optional[BingoSquare]] = [None] * (size * size)
    for square in order:
        conflicts = CONFLICTS_BY_KEY[square.key]
        for cell in cells:
            if board[cell] is not None:
                continue
            if any(board[other] is not None and board[other].key in conflicts
                   for line in lines_of_cell[cell] for other in line):
                continue
            board[cell] = square
            break
        else:
            return None, square
    return board, None


def setup_bingo(world: 'SlayTheSpire2World') -> None:
    """Clamps the lines option and deals the board. Call once the roster and options are final."""
    size = world.options.bingo_size.value
    if world.options.bingo_lines.value > max_lines(size):
        logging.warning("Slay the Spire II player %s: a %dx%d bingo board has %d lines; lowering Bingo Lines "
                        "Needed from %d.", world.player_name, size, size, max_lines(size),
                        world.options.bingo_lines.value)
        world.options.bingo_lines.value = max_lines(size)
    world.bingo_board = deal_board(world, size)


def restore_bingo(world: 'SlayTheSpire2World', bingo: Dict[str, Any]) -> None:
    """Rebuilds the board from slot data, for Universal Tracker."""
    world.options.bingo_size.value = bingo["size"]
    world.options.bingo_lines.value = bingo["lines"]
    world.bingo_board = [SQUARES_BY_KEY[square["key"]] for square in bingo["squares"]]


def create_bingo_region(world: 'SlayTheSpire2World', origin: Region) -> None:
    from .world import SlayTheSpire2Item, SlayTheSpire2Location

    region = Region(BINGO_REGION, world.player, world.multiworld)
    world.multiworld.regions.append(region)
    origin.connect(region, BINGO_REGION)
    for square in world.bingo_board:
        location = SlayTheSpire2Location(world.player, square.location_name, square.location_id, region)
        location.progress_type = LocationProgressType.EXCLUDED
        region.locations.append(location)
    for line, _ in board_lines(world.options.bingo_size.value):
        location = SlayTheSpire2Location(world.player, line_location_name(line), None, region)
        location.place_locked_item(SlayTheSpire2Item(BINGO_LINE_ITEM_DATA, BINGO_LINE_ITEM,
                                                     ItemClassification.progression, None, world.player))
        region.locations.append(location)


def square_rule(square: BingoSquare, world: 'SlayTheSpire2World') -> Rule:
    event = TIER_EVENTS[square.tier]
    return HasAny(*(f"{config.name} {event}" for config in roster_can_do(square, world.characters)))


def set_bingo_rules(world: 'SlayTheSpire2World') -> None:
    """Rules for the squares and lines, and the bingo completion rule. Both logic models use it:
    it builds only on the per-character boss events."""
    rules = [square_rule(square, world) for square in world.bingo_board]
    for square, rule in zip(world.bingo_board, rules):
        world.set_rule(world.get_location(square.location_name), rule)
    for line, cells in board_lines(world.options.bingo_size.value):
        line_rule = rules[cells[0]]
        for cell in cells[1:]:
            line_rule = line_rule & rules[cell]
        world.set_rule(world.get_location(line_location_name(line)), line_rule)
    world.set_completion_rule(Has(BINGO_LINE_ITEM, count=world.options.bingo_lines.value))


def bingo_slot_data(world: 'SlayTheSpire2World') -> Dict[str, Any]:
    return {
        "size": world.options.bingo_size.value,
        "lines": world.options.bingo_lines.value,
        "squares": [
            {"key": square.key, "location": square.location_id, "tier": square.tier, "text": board_text(square)}
            for square in world.bingo_board
        ],
    }


def spoiler_board(world: 'SlayTheSpire2World') -> List[str]:
    size = world.options.bingo_size.value
    lines = [f"Bingo board ({size}x{size}, {world.options.bingo_lines.value} lines needed):"]
    for row in range(size):
        squares = world.bingo_board[row * size:(row + 1) * size]
        lines.append("  " + " | ".join(f"[{square.tier}] {board_text(square)}" for square in squares))
    return lines
