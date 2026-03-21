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

class Test49Floors(Spire2TestBase):
    options = {
        "characters": [
            "ironclad",
            "silent",
        ],
        "ascension": 10
    }

    def test_ensure_floor_49(self):
        self.assertIsNotNone(self.world.get_location("Ironclad Reached Floor 49"))

    def test_no_dupes(self):
        stuff = dict()

        for location in self.world.get_locations():
            if location.address is None:
                continue
            self.assertTrue(location.address not in stuff, f"location duplicated {location.name} {stuff.get(location.address, None)}")
            stuff[location.address] = location

class Test48Floors(Spire2TestBase):
    options = {
        "characters": [
            "ironclad",
            "silent",
        ],
        "ascension": 9
    }

    def test_no_floor_49(self):
        self.assertFalse( "Ironclad Reached Floor 49" in self.world.get_locations())

