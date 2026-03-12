from worlds.spire2.options import CharacterOptions
from worlds.spire2.test import Spire2TestBase


class TestMultiCharsValid(Spire2TestBase):

    options = {
        "characters": [
            "ironclad",
            "silent",
        ]
    }

    def test_valid(self):
        CharacterOptions.schema.validate(self.world.options.advanced_characters.value)
        pass
