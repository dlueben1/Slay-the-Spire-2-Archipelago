"""The numbers behind power logic, kept together so they are easy to read and tune.

Each region entrance checks the character's run against that region's row in REGIONS:

    card strength >= the row's minimum card strength
    power         >= the row's base power + the value of any support the run is missing

Card strength counts AP card rewards, the vanilla card rewards that half shuffle leaves in a
run, and an upgraded starter card. Power is card strength plus relics and an upgraded starter
relic. Support is what a normal run already has: its starting card and relic, rests, smiths,
card removals, Ancients and shop choices. While AP items have not restored a kind of shuffled
support, its value raises the requirement, so extra power can make up for it. Support that the
options leave unshuffled is never missing.

Each character's starter card and relic values live with its registration in characters.py.
scripts/print_logic_table.py prints these tables. Keep this module free of Archipelago imports
so that script can load it on its own.
"""
from dataclasses import dataclass


@dataclass(frozen=True)
class PowerCheck:
    """What a run needs to clear one region, and the support a normal run has by then."""
    act: int
    power: float
    # Combat card rewards a normal run has seen on entering the region.
    card_rewards: int
    # Card strength needed however many relics the run has.
    minimum_cards: float
    rest: bool = False    # this act's rest tier
    smith: bool = False   # every smith tier up to this act
    remove: bool = False  # this act's card removal; without it, only earlier acts' removals
    shop: bool = False    # this act's shop choices
    # Power with Seeded Run, where every run replays the same map, when it differs.
    seeded_power: float | None = None

    def power_for(self, seeded: bool) -> float:
        return self.seeded_power if seeded and self.seeded_power is not None else self.power

    @property
    def vanilla_card_strength(self) -> float:
        """Card strength from the card rewards that half shuffle leaves vanilla."""
        return self.card_rewards * VANILLA_CARD_REWARD_SHARE

    @property
    def rest_tier(self) -> int:
        return self.act if self.rest else 0

    @property
    def smith_tiers(self) -> int:
        return self.act if self.smith else 0

    @property
    def removal_tiers(self) -> int:
        return self.act if self.remove else self.act - 1

    @property
    def shop_scale(self) -> float:
        return SHOP_SCALE_BY_ACT[self.act - 1] if self.shop else 0


# --- Regions ---------------------------------------------------------------------------------

# Each act has two tracks. The path track (Early, Mid, Late, Boss) holds every check a run can
# reach while walking around elites: the map has no elites before row 6, where Mid begins, one
# chest on a fixed row inside Mid, and ? rooms never become elites. The elite track holds the
# later relic checks, which need elite fights. Each Elites region is entered from its act's Mid
# region and also needs the previous act's Elites region, so relic counts add up across acts.
#
# Budget roughly seven combat card rewards per act: three before Mid, five before Late and
# seven before the boss. Later acts expect this act's smiths from entry and its rest from Mid.
# Act 1 waits until the boss for smiths so the sparse floorless opening stays loose. Elite rows
# copy their act's Late row apart from power.
REGIONS: dict[str, PowerCheck] = {
    #                        act  power  card rewards  min cards
    # Seeded Run replays one map, which may force an elite, so it keeps elite-inclusive power.
    "Mid Act 1":        PowerCheck(1,  2.0,   3,  1.0, rest=True, seeded_power=4.0),
    "Late Act 1":       PowerCheck(1,  7.0,   5,  1.0, rest=True, shop=True, seeded_power=10.0),
    "Act 1 Elites":     PowerCheck(1,  7.0,   5,  1.0, rest=True, shop=True),
    "Act 1 Boss Arena": PowerCheck(1, 11.5,   7,  3.0, rest=True, smith=True, remove=True, shop=True),
    "Early Act 2":      PowerCheck(2, 12.0,   7,  3.0, smith=True, shop=True),
    "Mid Act 2":        PowerCheck(2, 14.0,  10,  3.0, rest=True, smith=True, shop=True),
    "Late Act 2":       PowerCheck(2, 18.0,  12,  3.0, rest=True, smith=True, shop=True),
    "Act 2 Elites":     PowerCheck(2, 15.5,  12,  3.0, rest=True, smith=True, shop=True),
    "Act 2 Boss Arena": PowerCheck(2, 23.5,  14,  5.0, rest=True, smith=True, remove=True, shop=True),
    "Early Act 3":      PowerCheck(3, 23.5,  14,  5.0, smith=True, shop=True),
    "Mid Act 3":        PowerCheck(3, 25.5,  17,  5.0, rest=True, smith=True, shop=True),
    "Late Act 3":       PowerCheck(3, 27.0,  19,  5.0, rest=True, smith=True, shop=True),
    "Act 3 Elites":     PowerCheck(3, 26.0,  19,  5.0, rest=True, smith=True, shop=True),
    "Act 3 Boss Arena": PowerCheck(3, 30.0,  21,  7.0, rest=True, smith=True, remove=True, shop=True),
}

# Relic N is the run's Nth elite, chest or Black Star reward. Relic 1 is the Act 1 chest on the
# path. Elite-ready runs are expected to fight two elites in Acts 1 and 2 and three in Act 3:
# two Act 1 elites give relics 2-3, Act 2's chest and elites give 4-6, and Act 3's give 7-10.
RELIC_REGIONS = {
    1: "Mid Act 1",
    2: "Act 1 Elites", 3: "Act 1 Elites",
    4: "Act 2 Elites", 5: "Act 2 Elites", 6: "Act 2 Elites",
    7: "Act 3 Elites", 8: "Act 3 Elites", 9: "Act 3 Elites", 10: "Act 3 Elites",
}


# --- Card strength and power from AP items ---------------------------------------------------

# The first ordinary card rewards are worth one card each. Later ones are worth less because
# later choices are skipped more often; a higher value expects players to progress with weaker
# decks. Half shuffle reaches the discount sooner because its vanilla card rewards also add cards.
FULL_VALUE_CARD_REWARDS = 5
FULL_VALUE_CARD_REWARDS_ALL_SHUFFLED = 11
LATER_CARD_POWER = 0.7
RARE_CARD_POWER = 1.5
RELIC_POWER = 1.5
# Half shuffle leaves every other combat card reward vanilla, each worth one card.
VANILLA_CARD_REWARD_SHARE = 0.5


# --- Missing support: requirement added until AP items restore it ----------------------------

REST_POWER_BY_ACT = (3.0, 3.5, 4.0)    # this act's rest tier
SMITH_POWER_BY_TIER = (3.5, 3.0, 1.5)  # early smiths matter most
# Removal matters more as the deck grows. These values are a rough estimate.
SHOP_REMOVE_POWER_BY_TIER = (1.0, 1.5, 2.0)
# Neow, then the Act 2 and Act 3 Ancients.
ANCIENT_POWER_BY_TIER = (2.0, 4.5, 4.5)

# Slots a normal shop offers, valued in the order the client restores them: from the end of the
# page, so the first one back is the Power or rare neutral card. The first useful choices matter
# more than a full page. Colored cards and relics total 1.5 each, neutral cards 0.5, potions 1.
SHOP_SLOT_VALUES = (
    ("Shop Card Slot", (0.625, 0.375, 0.25, 0.125, 0.125)),
    ("Neutral Shop Card Slot", (0.375, 0.125)),
    ("Shop Relic Slot", (0.75, 0.5, 0.25)),
    ("Shop Potion Slot", (0.5, 0.375, 0.125)),
)
# Missing shop choices count less in later acts.
SHOP_SCALE_BY_ACT = (2 / 3, 1 / 3, 1 / 6)


# --- Shop gold (Gold Sanity with Shop Sanity) ------------------------------------------------

# Only Elite and Boss Gold are progression; combat gold stays filler. These amounts keep a
# conservative allowance for Poverty.
ELITE_GOLD = 30
BOSS_GOLD = 75
SHOP_GOLD_BY_ACT = (50, 150, 270)
