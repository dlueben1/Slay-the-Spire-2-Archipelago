"""Stable identities inside one AP slot. Player 1 retains the original IDs/names."""

PLAYER_OFFSET = 1_000_000
MAX_PLAYERS = 4


def player_name(name: str, number: int) -> str:
    return name if number == 1 else f"P{number} {name}"


def split_player_name(name: str) -> tuple[int, str]:
    for number in range(2, MAX_PLAYERS + 1):
        prefix = f"P{number} "
        if name.startswith(prefix):
            return number, name[len(prefix):]
    return 1, name


def player_id(code: int | None, number: int) -> int | None:
    return None if code is None else code + (number - 1) * PLAYER_OFFSET


def power_key(char_offset: int, number: int) -> int:
    return char_offset + (number - 1) * 100


def expand_player_groups(groups: dict[str, set[str]], characters: list[str]) -> None:
    """Qualified groups own one player's names; broad categories include all players."""
    original = {name: set(entries) for name, entries in groups.items()}
    prefixes = tuple(f"{character} " for character in characters)
    for number in range(2, MAX_PLAYERS + 1):
        for group, entries in original.items():
            qualified = {player_name(name, number) for name in entries}
            groups[player_name(group, number)] = qualified
            if not group.startswith(prefixes):
                groups[group].update(qualified)
