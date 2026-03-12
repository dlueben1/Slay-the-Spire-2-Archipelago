from typing import List, Any

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
        self.option_name: str = option_name
        self.mod_num = mod_num
        self.char_offset: int = char_offset
        self.official_name: str = official_names[char_offset - 1]
        self.seed: str = seed
        self.locked: bool = locked
        self.ascension: int = kwargs['ascension']

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
        }

    def __repr__(self):
        return self.to_dict().__repr__()