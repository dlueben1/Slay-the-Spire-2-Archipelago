import typing

from test.bases import WorldTestBase
from worlds.spire2 import SlayTheSpire2World


class Spire2TestBase(WorldTestBase):
    game = 'Slay the Spire II'
    world = SlayTheSpire2World
    prefix: typing.ClassVar[str] = "Silent"

    options = {
        'characters': ["silent"],
    }
