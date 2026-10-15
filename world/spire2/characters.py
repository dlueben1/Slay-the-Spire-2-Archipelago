from dataclasses import dataclass
from typing import List, Any, Sequence

from .constants import (NUM_CUSTOM, VANILLA_CHARACTER_BLOCKS, GENERIC_CHARACTER_BLOCKS,
                        PREDEFINED_CHARACTER_BLOCKS)


@dataclass(frozen=True)
class StarterPower:
    # Missing-starter penalty, then second-receipt bonus above the normal loadout.
    card: tuple[float, float] = (1.0, 4.0)
    relic: tuple[float, float] = (2.5, 4.0)


DEFAULT_STARTER_POWER = StarterPower()


@dataclass(frozen=True)
class CharacterDefinition:
    name: str
    internal_id: str | None
    # Vanilla entries explicitly choose reviewed values or DEFAULT_STARTER_POWER.
    # Modded entries have no assumed power credit for unverified starter upgrades.
    starter_power: StarterPower | None = None


# Append only: position within each list determines its permanent ID block.
VANILLA_CHARACTERS = (
    CharacterDefinition("Ironclad", "Ironclad", StarterPower((1.0, 4.5), (3.0, 2.5))),
    CharacterDefinition("Silent", "Silent", StarterPower((1.5, 4.5), (2.0, 3.5))),
    CharacterDefinition("Defect", "Defect", StarterPower((1.0, 3.0), (1.0, 3.0))),
    CharacterDefinition("Regent", "Regent", StarterPower((1.5, 5.0), (1.5, 2.5))),
    CharacterDefinition("Necrobinder", "Necrobinder", StarterPower((1.0, 2.5), (4.0, 5.0))),
)

PREDEFINED_CHARACTERS = (
    CharacterDefinition("Tectonic", "INTOTHESPIREVERSE-SHADOW_IRONCLAD"),
    CharacterDefinition("Captain", "INTOTHESPIREVERSE-SHADOW_REGENT"),
    CharacterDefinition("Acolyte", "INTOTHESPIREVERSE-SHADOW_SILENT"),
    CharacterDefinition("Slime Boss", "SLIMEBOSS-SLIME_BOSS"),
    CharacterDefinition("Guardian", "GUARDIAN-GUARDIAN"),
    CharacterDefinition("Hexaghost", "HEXAGHOST-HEXAGHOST"),
    CharacterDefinition("Champ", "CHAMP-CHAMP"),
    CharacterDefinition("Automaton", "AUTOMATON-AUTOMATON"),
    CharacterDefinition("Snecko", "SNECKO-SNECKO"),
    CharacterDefinition("Hermit", "HERMIT-HERMIT"),
    CharacterDefinition("Awakened One", "AWAKENED-AWAKENED"),
    CharacterDefinition("Watcher", "WATCHER-WATCHER"),
)


def create_character_registry(
        vanilla: Sequence[CharacterDefinition] = VANILLA_CHARACTERS,
        predefined: Sequence[CharacterDefinition] = PREDEFINED_CHARACTERS,
) -> dict[int, CharacterDefinition]:
    """Return character definitions keyed by their one-based AP ID block number.

    The integer key is the slot-data ``char_offset``, not a YAML roster position
    or a complete item/location ID. Items use block * CHAR_OFFSET + base ID;
    locations use (block - 1) * CHAR_OFFSET + base ID.
    """
    if len(vanilla) > len(VANILLA_CHARACTER_BLOCKS) or len(predefined) > len(PREDEFINED_CHARACTER_BLOCKS):
        raise ValueError("Character registry exceeds its reserved ID range")
    if NUM_CUSTOM > len(GENERIC_CHARACTER_BLOCKS):
        raise ValueError("Custom character limit exceeds its reserved ID range")
    registry = dict(zip(VANILLA_CHARACTER_BLOCKS, vanilla))
    registry.update({block: CharacterDefinition(f"Custom Character {index}", None)
                     for index, block in enumerate(GENERIC_CHARACTER_BLOCKS[:NUM_CUSTOM], start=1)})
    registry.update(zip(PREDEFINED_CHARACTER_BLOCKS, predefined))
    names = [definition.name for definition in registry.values()]
    internal_ids = [definition.internal_id.casefold() for definition in registry.values()
                    if definition.internal_id is not None]
    if len(set(names)) != len(names) or len(set(internal_ids)) != len(internal_ids):
        raise ValueError("Character registry contains duplicate names or internal IDs")
    aliases = {}
    for block, definition in registry.items():
        if definition.internal_id is None:
            continue
        for alias in (definition.name, definition.internal_id):
            if aliases.setdefault(alias.casefold(), block) != block:
                raise ValueError(f"Character registry contains an ambiguous alias: {alias}")
    return registry


character_registry = create_character_registry()
character_list = [definition.name for definition in VANILLA_CHARACTERS]
vanilla_by_name = {definition.name: definition for definition in VANILLA_CHARACTERS}
vanilla_character_ids = {
    name.casefold()
    for definition in VANILLA_CHARACTERS
    for name in (definition.name, definition.internal_id)
    if name is not None
}
registered_character_blocks = {
    alias.casefold(): block
    for block, definition in character_registry.items() if definition.internal_id is not None
    for alias in (definition.name, definition.internal_id)
}


def character_identity(value: str) -> str:
    """Compare aliases and full IDs as the same character without rewriting YAML keys."""
    key = value.strip().casefold()
    block = registered_character_blocks.get(key)
    if block is not None:
        return character_registry[block].internal_id.casefold()
    return key


def unique_character_ids(values: Sequence[str]) -> list[str]:
    """Keep the first occurrence of each character, resolving aliases before deduplication."""
    result = []
    seen = set()
    for value in values:
        if not isinstance(value, str) or not value.strip():
            raise ValueError("Character IDs must be non-empty strings")
        value = value.strip()
        identity = character_identity(value)
        if identity not in seen:
            result.append(value)
            seen.add(identity)
    return result


class CharacterConfig:

    def __init__(self, name: str, option_name: str, char_offset: int, mod_num: int, seed: str, locked: bool, **kwargs):
        self.name: str = name
        self.option_name: str = option_name
        self.mod_num = mod_num
        self.char_offset: int = char_offset
        if self.mod_num == 0:
            self.official_name: str = vanilla_by_name[name].internal_id
        else:
            definition = character_registry.get(char_offset)
            self.official_name = (definition.internal_id if definition is not None
                                  and definition.name == name and definition.internal_id is not None
                                  else option_name)
        self.seed: str = seed
        self.locked: bool = locked
        self.progressive_starter_card: bool = bool(kwargs.get('progressive_starter_card', False))
        self.progressive_starter_relic: bool = bool(kwargs.get('progressive_starter_relic', False))
        self.ascension: List[str] = kwargs['ascension']
        # Doesn't need to make it to the mod
        if 'ascension_down' in kwargs:
            self.ascension_down: List[str] = kwargs['ascension_down']
        else:
            self.ascension_down = []

    def to_dict(self) -> dict[str, Any]:
        return {
            'name': self.name,
            'option_name': self.option_name,
            'char_offset': self.char_offset,
            'official_name': self.official_name,
            'seed': self.seed,
            'locked': self.locked,
            'mod_num': self.mod_num,
            'ascension': self.ascension,
            'ascension_down': self.ascension_down,
            'progressive_starter_card': self.progressive_starter_card,
            'progressive_starter_relic': self.progressive_starter_relic,
        }

    def __repr__(self):
        return self.to_dict().__repr__()
