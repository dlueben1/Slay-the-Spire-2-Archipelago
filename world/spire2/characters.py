from typing import List, Any
from .coop import player_name, power_key

character_list: List[str] = [
    "Ironclad",
    "Silent",
    "Defect",
    "Regent",
    "Necrobinder",
]

# TODO: verify
official_names: List[str] = [
    "Ironclad",
    "Silent",
    "Defect",
    "Regent",
    "Necrobinder",
]

character_offset_map = {
    name.lower(): i
    for i, name in enumerate(character_list, start=1)
}

for i, name in enumerate(official_names, start=1):
    character_offset_map[name.lower()] = i

class CharacterConfig:

    def __init__(self, name: str, option_name: str, char_offset: int, mod_num: int, seed: str, locked: bool, **kwargs):
        self.name: str = name
        self.player_number: int = kwargs.get('player_number', 1)
        self.option_name: str = option_name
        self.mod_num = mod_num
        self.char_offset: int = char_offset
        if self.mod_num == 0:
            self.official_name: str = official_names[char_offset - 1]
        else:
            self.official_name = option_name
        self.seed: str = seed
        self.locked: bool = locked
        self.ascension: List[str] = kwargs['ascension']
        # Doesn't need to make it to the mod
        if 'ascension_down' in kwargs:
            self.ascension_down: List[str] = kwargs['ascension_down']
        else:
            self.ascension_down = []

    @property
    def ap_name(self) -> str:
        return player_name(self.name, self.player_number)

    @property
    def power_key(self) -> int:
        return power_key(self.char_offset, self.player_number)

    def to_dict(self) -> dict[str, Any]:
        return {
            'name': self.name,
            'player_number': self.player_number,
            'option_name': self.option_name,
            'char_offset': self.char_offset,
            'official_name': self.official_name,
            'seed': self.seed,
            'locked': self.locked,
            'mod_num': self.mod_num,
            'ascension': self.ascension,
            'ascension_down': self.ascension_down,
        }

    def __repr__(self):
        return self.to_dict().__repr__()
