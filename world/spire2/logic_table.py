"""The numbers behind power logic, kept together so they are easy to read and tune.

Each region entrance checks the character's run against that region's row in REGIONS:

    card strength >= the row's minimum card strength
    power         >= the row's base power + the value of any support the run is missing

Card strength counts AP card rewards, the vanilla card rewards that half shuffle leaves in a
run, and an upgraded starter card. Power is card strength plus the relics a run can claim by
then and an upgraded starter relic. Support is what a normal run already has: its starting card and relic, rests, smiths,
card removals, Ancients and shop choices. While AP items have not restored a kind of shuffled
support, its value raises the requirement, so extra power can make up for it. Support that the
options leave unshuffled is never missing.

Each character's starter card and relic values live with its registration in characters.py.
scripts/print_logic_table.py prints these tables. Keep this module free of Archipelago imports
so that script can load it on its own.
"""
from dataclasses import dataclass
from math import ceil


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
    # Smith tiers up to this act count, this act's own for this share of its value (one of the
    # SMITHS_* shares below); None when smiths do not count yet.
    smith: float | None = None
    remove: bool = False  # this act's card removal; without it, only earlier acts' removals
    shop: bool = False    # this act's shop choices
    # Power with Seeded Run, where every run replays the same map, when it differs.
    seeded_power: float | None = None

    def power_for(self, seeded: bool) -> float:
        return self.seeded_power if seeded and self.seeded_power is not None else self.power

    @property
    def vanilla_card_strength(self) -> float:
        """Card strength from the card rewards that half shuffle leaves vanilla."""
        return self.card_rewards * VANILLA_CARD_REWARD_SHARE * VANILLA_CARD_POWER

    @property
    def rest_tier(self) -> int:
        return self.act if self.rest else 0

    @property
    def smith_tiers(self) -> int:
        return self.act if self.smith is not None else 0

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
# later relic checks, one region per relic: each elite fight, and in Acts 2 and 3 the chest a run
# reaches after the previous act's elites. Each act's chest and first elite are entered from its
# Mid region and need the previous act's last elite; each later elite is entered from the one
# before, so relic counts add up across a run.
#
# Budget roughly seven combat card rewards per act: three before Mid, five before Late and
# seven before the boss. Each act expects its rest from Mid and its smiths as its rest sites pass
# (the SMITHS_* shares below), and earlier acts' smiths in full. Early Act 1
# needs only the character: Act 1 opens with three weak hallway fights. The Act 1 Hallway, between
# Early and Mid, holds the checks of the fourth fight, the first from the normal pool. It asks only
# for a normal start (starter card, starter relic and Neow), so a run without one needs power to
# make up for it. Elite track rows copy their act's Late row apart from power and smith share:
# chests take Mid power, and elites climb from about Mid power to Late power. Act 1's second elite
# asks for more than Late Act 1: two Act 1 elites take a stronger deck than walking around them
# to the boss. The Act 2 boss and Early Act 3 split
# the climb to Act 3, where Early Act 3 also expects the third Ancient.
# Share of an act's rest sites a run has passed by each stage, from a port of the game's map
# generator: a path that seeks rest sites passes none before Mid, about two or three before the
# all-rest row in front of the boss, and that row. Upgrades add up only as a run passes rest sites,
# so an act's smith tier counts for this share of its value.

# These are values from 0 to 1, tweak as you see fit.
SMITHS_EARLY = 0.0
SMITHS_MID = 0.25
SMITHS_FIRST_ELITE = 0.4
SMITHS_LATE = 0.6
SMITHS_BOSS = 1.0

REGIONS: dict[str, PowerCheck] = {
    #                        act  power  card rewards  min cards
    # This is the 1st 'hard' combat in act 1 and having an entry here makes it have requirements
    # That you need power equivalent to neow's bonus and all starter relics. Otherwise a full
    # sanities run is too difficult with literally 0 support or you get very lucky with events
    "Act 1 Hallway":    PowerCheck(1,  0.0,   3,  0.0),
    #                        act  power  card rewards  min cards
    # Seeded Run replays one map, which may force an elite, so it keeps elite-inclusive power.
    "Mid Act 1":        PowerCheck(1,  2.5,   3,  1.0, rest=True, smith=SMITHS_MID, seeded_power=4.0),
    "Late Act 1":       PowerCheck(1,  7.0,   5,  1.0, rest=True, smith=SMITHS_LATE, shop=True, seeded_power=10.0),
    "Act 1 Elite 1":    PowerCheck(1,  5.0,   5,  1.0, rest=True, smith=SMITHS_FIRST_ELITE, shop=True),
    "Act 1 Elite 2":    PowerCheck(1,  8.0,   5,  1.0, rest=True, smith=SMITHS_LATE, shop=True),
    "Act 1 Boss Arena": PowerCheck(1, 11.5,   7,  3.0, rest=True, smith=SMITHS_BOSS, remove=True, shop=True),
    "Early Act 2":      PowerCheck(2, 12.0,   7,  3.0, smith=SMITHS_EARLY, shop=True),
    "Mid Act 2":        PowerCheck(2, 14.0,  10,  3.0, rest=True, smith=SMITHS_MID, shop=True),
    "Late Act 2":       PowerCheck(2, 18.0,  12,  3.0, rest=True, smith=SMITHS_LATE, shop=True),
    "Act 2 Chest":      PowerCheck(2, 14.0,  12,  3.0, rest=True, smith=SMITHS_MID, shop=True),
    "Act 2 Elite 1":    PowerCheck(2, 15.5,  12,  3.0, rest=True, smith=SMITHS_FIRST_ELITE, shop=True),
    "Act 2 Elite 2":    PowerCheck(2, 17.0,  12,  3.0, rest=True, smith=SMITHS_LATE, shop=True),
    "Act 2 Boss Arena": PowerCheck(2, 22.0,  14,  4.0, rest=True, smith=SMITHS_BOSS, remove=True, shop=True),
    "Early Act 3":      PowerCheck(3, 23.0,  14,  5.0, smith=SMITHS_EARLY, shop=True),
    "Mid Act 3":        PowerCheck(3, 25.5,  17,  5.0, rest=True, smith=SMITHS_MID, shop=True),
    "Late Act 3":       PowerCheck(3, 27.0,  19,  5.0, rest=True, smith=SMITHS_LATE, shop=True),
    "Act 3 Chest":      PowerCheck(3, 25.5,  19,  5.0, rest=True, smith=SMITHS_MID, shop=True),
    "Act 3 Elite 1":    PowerCheck(3, 26.0,  19,  5.0, rest=True, smith=SMITHS_FIRST_ELITE, shop=True),
    "Act 3 Elite 2":    PowerCheck(3, 26.5,  19,  5.0, rest=True, smith=SMITHS_LATE, shop=True),
    "Act 3 Elite 3":    PowerCheck(3, 27.0,  19,  5.0, rest=True, smith=SMITHS_LATE, shop=True),
    "Act 3 Boss Arena": PowerCheck(3, 30.0,  21,  7.0, rest=True, smith=SMITHS_BOSS, remove=True, shop=True),
}

# Relic N is the run's Nth elite, chest or Black Star reward. Relic 1 is the Act 1 chest on the
# path. Elite-ready runs are expected to fight two elites in Acts 1 and 2 and three in Act 3:
# two Act 1 elites give relics 2-3, Act 2's chest and elites give 4-6, and Act 3's give 7-10.
# Perhaps this can be adjusted in the future to account for player preferences
# Just a rough guess but act 1 is probably the time people fight the most elites in AP's
ELITES_PER_ACT = (2, 2, 3)

# The main goal of RELIC Regions is so you can have '2 tracks' where you can dodge elites i.e
# get a lot of floor checks
# until you get strong enough to where you can fight elites. In practice this has worked pretty well
# especially with relic 2 and relic 3 which felt too hard at times to logically get
RELIC_REGIONS = {
    1: "Mid Act 1",
    2: "Act 1 Elite 1", 3: "Act 1 Elite 2",
    4: "Act 2 Chest", 5: "Act 2 Elite 1", 6: "Act 2 Elite 2",
    7: "Act 3 Chest", 8: "Act 3 Elite 1", 9: "Act 3 Elite 2", 10: "Act 3 Elite 3",
}


def last_elite(act: int) -> str:
    """The elite track region of an act's last expected elite."""
    return f"Act {act} Elite {ELITES_PER_ACT[act - 1]}"

# --- Claimable relics ------------------------------------------------------------------------

# Relics logically gotten from just the mid act chest. Doesn't take into account the small
# chance to get chests from events.
PATH_RELIC_REWARDS = {
    "Act 1 Hallway": 0, "Mid Act 1": 1, "Late Act 1": 1, "Act 1 Boss Arena": 1,
    "Early Act 2": 1, "Mid Act 2": 2, "Late Act 2": 2, "Act 2 Boss Arena": 2,
    "Early Act 3": 2, "Mid Act 3": 3, "Late Act 3": 3, "Act 3 Boss Arena": 3,
    **{region: number - 1 for number, region in RELIC_REGIONS.items() if number > 1},
}
# Relic rewards taken by a region after also fighting every elite up to an act's last elite.
# Helper table to better handle relics available anytime.
ELITE_RELIC_REWARDS = {
    "Late Act 1": {last_elite(1): 3},
    "Act 1 Boss Arena": {last_elite(1): 3},
    "Early Act 2": {last_elite(1): 3},
    "Mid Act 2": {last_elite(1): 4},
    "Late Act 2": {last_elite(1): 4, last_elite(2): 6},
    "Act 2 Boss Arena": {last_elite(1): 4, last_elite(2): 6},
    "Early Act 3": {last_elite(1): 4, last_elite(2): 6},
    "Mid Act 3": {last_elite(1): 5, last_elite(2): 7},
    "Late Act 3": {last_elite(1): 5, last_elite(2): 7, last_elite(3): 10},
    "Act 3 Boss Arena": {last_elite(1): 5, last_elite(2): 7, last_elite(3): 10},
}


# --- Card strength and power from AP items ---------------------------------------------------

# Total card strength from ordinary AP Card Rewards, by how many the run holds (index 0 is none).
# The first rewards are worth a full card each, until a lean deck has most of the cards it keeps.
# After that, extra rewards add choice rather than cards, and each adds less than the one before,
# as a simple card draft model suggested. Half shuffle reaches that point sooner because its
# vanilla card rewards also add cards. Keep a full pool's total (8.5 and 17.3) unless the region
# power is retuned too. Past the end of a table, each extra reward adds the table's last step.
# Basically have diminishing returns on card rewards. Tweak as you see fit
CARD_REWARD_STRENGTH = (0, 1, 2, 3, 4, 5, 5.9, 6.7, 7.4, 8.0, 8.5)
CARD_REWARD_STRENGTH_ALL_SHUFFLED = (0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
                                     11, 11.95, 12.85, 13.65, 14.4, 15.1, 15.75, 16.3, 16.8, 17.3)
RARE_CARD_POWER = 1.5
RELIC_POWER = 1.5
# Half shuffle leaves every other combat card reward vanilla.
VANILLA_CARD_REWARD_SHARE = 0.5
# Card strength of each vanilla card reward. Vanilla rewards arrive one at a time, so early ones
# are often survival picks; every AP Card Reward can be compared at the start of a run.
# note worthy that its less than a normal card reward since once its skipped: its skipped forever
VANILLA_CARD_POWER = 0.85


def card_reward_table(all_shuffled: bool) -> tuple[float, ...]:
    return CARD_REWARD_STRENGTH_ALL_SHUFFLED if all_shuffled else CARD_REWARD_STRENGTH


def card_reward_strength(table: tuple[float, ...], rewards: int) -> float:
    """Card strength from this many ordinary card rewards, extending the table by its last step."""
    if rewards < len(table):
        return table[rewards]
    return table[-1] + (rewards - len(table) + 1) * (table[-1] - table[-2])


def card_rewards_needed(table: tuple[float, ...], strength: float) -> int:
    """The fewest ordinary card rewards whose card strength reaches the given amount."""
    for rewards in range(len(table) + 100):
        if card_reward_strength(table, rewards) >= strength:
            return rewards
    raise ValueError(f"Card rewards cannot reach card strength {strength:g}")


# --- Missing support: requirement added until AP items restore it ----------------------------

REST_POWER_BY_ACT = (3.0, 3.5, 4.0)    # this act's rest tier

# A rest site offers one action. A run that can smith but not rest in this act smiths at each rest
# site instead, so its missing rest tier costs only this share. Missing smiths keep their full
# value: upgrades last the whole run, while the extra heals are rarely needed.
# Basically if you have both options, rests are only currently half as effective
REST_SHARE_WHILE_SMITHING = 0.5
# About one power per upgrade: a run that seeks rest sites upgrades two or three times an act.
# Act 3 upgrades matter less, because the best cards are already upgraded by then.
SMITH_POWER_BY_TIER = (2.5, 2.5, 1.25)
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
# Missing shop choices count for more in each act, because purchases stay for the run: about 330
# vanilla gold per act buys roughly two items besides a card removal, and removals add up the same
# way. A page missing every slot costs 1.5, 3 and 4.5.
SHOP_SCALE_BY_ACT = (1 / 3, 2 / 3, 1)
FULL_SHOP_PAGE_VALUE = sum(sum(values) for _, values in SHOP_SLOT_VALUES)


# --- Shop gold (Gold Sanity with Shop Sanity) ------------------------------------------------

# Shop checks persist between runs, and every shop visit lists the unchecked Shop Slot checks in
# slot order, up to the act's ceiling. A run needs gold for the dearest checks its shop can list:
# two per visit, so clearing a shop takes fewer restarts, but one with Tiered prices, where two
# would need almost every gold item and a shop takes several runs to clear anyway.
#
# Only Elite and Boss Gold count; combat gold stays useful. Gold amounts keep a conservative
# allowance for Poverty (x0.75). Each act's chest is on every path and gives 42-52 gold.
ELITE_GOLD = 30
BOSS_GOLD = 75
STARTING_GOLD = 99
CHEST_GOLD = 31
SHOP_CHECKS_LISTED_BY_ACT = (5, 10, 16)
# AP page positions in the order shop checks fill them, up to each category's vanilla count.
# The card-removal checks take otherwise unused positions in the same order.
SHOP_PAGE = (("card", 5), ("neutral", 2), ("relic", 3), ("potion", 3))
SHOP_REMOVE_CHECKS = 3
# Dearest Tiered price of a check in each position: cards cost as rare cards (the price for a
# progression item), relics as shop relics and potions as common potions, at the top of the
# shop's random price range.
SHOP_TIERED_PRICES = {"card": 158, "neutral": 158, "relic": 230, "potion": 53}
FIXED_SHOP_PRICE = 15
# By Shop Sanity Costs: the share of the Tiered price (Fixed charges FIXED_SHOP_PRICE instead),
# and how many checks one shop visit should afford.
SHOP_PRICE_SHARE = (None, 0.2, 0.5, 1.0)
SHOP_CHECKS_PER_VISIT = (2, 2, 2, 1)


def shop_page(card_slots: int, neutral_slots: int, relic_slots: int, potion_slots: int,
              remove_slots: bool) -> list[str]:
    """The category of each AP page position that shop checks fill, in fill order."""
    spare = SHOP_REMOVE_CHECKS if remove_slots else 0
    page = []
    for (category, limit), configured in zip(SHOP_PAGE, (card_slots, neutral_slots, relic_slots, potion_slots)):
        borrowed = min(limit - configured, spare)
        spare -= borrowed
        page += [category] * (configured + borrowed)
    return page


def shop_check_price(category: str, cost_mode: int) -> int:
    share = SHOP_PRICE_SHARE[cost_mode]
    if share is None:
        return FIXED_SHOP_PRICE
    return ceil(SHOP_TIERED_PRICES[category] * share)


def shop_gold_needed(page: list[str], act: int, cost_mode: int) -> int:
    """Gold from Elite and Boss Gold that a run needs for the dearest checks this act's shop lists."""
    listed = page[:SHOP_CHECKS_LISTED_BY_ACT[act - 1]]
    prices = sorted((shop_check_price(category, cost_mode) for category in listed), reverse=True)
    dearest = sum(prices[:SHOP_CHECKS_PER_VISIT[cost_mode]])
    return max(0, dearest - STARTING_GOLD - CHEST_GOLD * (act - 1))


# --- Gold for shop choices (Gold Sanity) -----------------------------------------------------

# A shop turns gold into power. With Gold Sanity, whose checks replace vanilla fight gold, shop
# choices count only as far as a run's gold reaches a normal run's by that act's Late region,
# walking around elites. A run counts its starting and chest gold, Elite and Boss Gold, and every
# Combat Gold: those are small and plentiful, and counting them would make 13 more items per
# character progression.
COMBAT_GOLD = 11        # a vanilla fight's gold or a Combat Gold item, with the Poverty allowance
COMBAT_GOLD_ITEMS = 13  # in a Gold Sanity item pool


def normal_run_gold(act: int) -> int:
    """Gold a normal run has by an act's Late region, walking around elites."""
    fights = REGIONS[f"Late Act {act}"].card_rewards
    return STARTING_GOLD + COMBAT_GOLD * fights + CHEST_GOLD * act + BOSS_GOLD * (act - 1)


def gold_sanity_run_gold(act: int, elite_gold_items: int, boss_gold_items: int) -> int:
    """Gold a Gold Sanity run has for an act's shops, with every Combat Gold assumed held."""
    return (STARTING_GOLD + CHEST_GOLD * act + COMBAT_GOLD * COMBAT_GOLD_ITEMS
            + ELITE_GOLD * elite_gold_items + BOSS_GOLD * boss_gold_items)
