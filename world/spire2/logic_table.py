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

# Budget roughly seven combat card rewards per act: three before Mid, five before Late and
# seven before the boss. Later acts expect this act's smiths from entry and its rest from Mid.
# Act 1 waits until the boss for smiths so the sparse floorless opening stays loose.
REGIONS: dict[str, PowerCheck] = {
    #                        act  power  card rewards  min cards
    "Mid Act 1":        PowerCheck(1,  4.0,   3,  1.0, rest=True),
    "Late Act 1":       PowerCheck(1, 10.0,   5,  1.0, rest=True, shop=True),
    "Act 1 Boss Arena": PowerCheck(1, 11.5,   7,  3.0, rest=True, smith=True, remove=True, shop=True),
    "Early Act 2":      PowerCheck(2, 12.0,   7,  3.0, smith=True, shop=True),
    "Mid Act 2":        PowerCheck(2, 14.0,  10,  3.0, rest=True, smith=True, shop=True),
    "Late Act 2":       PowerCheck(2, 18.0,  12,  3.0, rest=True, smith=True, shop=True),
    "Act 2 Boss Arena": PowerCheck(2, 23.5,  14,  5.0, rest=True, smith=True, remove=True, shop=True),
    "Early Act 3":      PowerCheck(3, 23.5,  14,  5.0, smith=True, shop=True),
    "Mid Act 3":        PowerCheck(3, 25.5,  17,  5.0, rest=True, smith=True, shop=True),
    "Late Act 3":       PowerCheck(3, 27.0,  19,  5.0, rest=True, smith=True, shop=True),
    "Act 3 Boss Arena": PowerCheck(3, 30.0,  21,  7.0, rest=True, smith=True, remove=True, shop=True),
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
