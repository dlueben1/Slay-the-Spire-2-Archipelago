import typing

from rule_builder.rules import Rule
from test.bases import WorldTestBase
from worlds.spire2 import SlayTheSpire2World
from worlds.spire2.rules_new import SpireHasPower


def power_checks(rule: Rule.Resolved) -> list[SpireHasPower.Resolved]:
    """Every power check in an entrance rule. The first one counts the relics a run claims while
    walking around elites; any others count an earlier Elites region's rewards too."""
    if isinstance(rule, SpireHasPower.Resolved):
        return [rule]
    return [check for child in getattr(rule, "children", ()) for check in power_checks(child)]


def path_power(rule: Rule.Resolved) -> SpireHasPower.Resolved:
    return power_checks(rule)[0]


class Spire2TestBase(WorldTestBase):
    game = 'Slay the Spire II'
    world: SlayTheSpire2World
    prefix: typing.ClassVar[str] = "Silent"

    options = {
        'characters': ["silent"],
    }
