import dataclasses
from types import EllipsisType
from typing import TYPE_CHECKING, List, NamedTuple
from typing_extensions import override

from BaseClasses import CollectionState
from NetUtils import JSONMessagePart
from rule_builder.options import OptionFilter
from rule_builder.rules import Rule, True_, CanReachRegion, Has, HasFromListUnique
from .characters import CharacterConfig, DEFAULT_STARTER_POWER, character_registry
from .logic_table import (
    ANCIENT_POWER_BY_TIER, BOSS_GOLD, ELITE_GOLD, ELITE_RELIC_REWARDS, FULL_SHOP_PAGE_VALUE, PATH_RELIC_REWARDS,
    RARE_CARD_POWER, REGIONS, RELIC_POWER, REST_POWER_BY_ACT, REST_SHARE_WHILE_SMITHING,
    SHOP_REMOVE_POWER_BY_TIER, SHOP_SLOT_VALUES, SMITH_POWER_BY_TIER,
    PowerCheck, card_reward_strength, card_reward_table, gold_sanity_run_gold, last_elite, normal_run_gold,
    shop_gold_needed, shop_page,
)
from .options import AncientRelicLocation

if TYPE_CHECKING:
    from .options import Spire2Options
    from .world import SlayTheSpire2World


class LedgerEntry(NamedTuple):
    label: str
    amount: float


@dataclasses.dataclass
class PowerLedger:
    """Each contribution to a power check, labelled for Universal Tracker's explanation."""
    cards: List[LedgerEntry] = dataclasses.field(default_factory=list)
    relics: List[LedgerEntry] = dataclasses.field(default_factory=list)
    # Missing support, in the order it is added to the requirement; shop entries also go in shop.
    adjustments: dict[str, float] = dataclasses.field(default_factory=dict)
    shop: dict[str, float] = dataclasses.field(default_factory=dict)


class PowerItems(NamedTuple):
    """The names of a character's items that its power checks count."""
    card_reward: str
    rare_card_reward: str
    starter_card: str
    starter_relic: str
    relic: str
    rest: str
    smith: str
    shop_remove: str
    ancient: str
    shop_slots: tuple[str, ...]  # in SHOP_SLOT_VALUES order
    elite_gold: str
    boss_gold: str

    @classmethod
    def of(cls, char: str) -> 'PowerItems':
        singles = ("Card Reward", "Rare Card Reward", "Progressive Starter Card", "Progressive Starter Relic",
                   "Relic", "Progressive Rest", "Progressive Smith", "Progressive Shop Remove", "Progressive Ancient")
        return cls(*(f"{char} {item}" for item in singles),
                   tuple(f"{char} {item}" for item, _ in SHOP_SLOT_VALUES),
                   f"{char} Elite Gold", f"{char} Boss Gold")


def tier_range(first: int, last: int) -> str:
    return f"tier {first}" if first == last else f"tiers {first}-{last}"


@dataclasses.dataclass()
class SpireHasPower(Rule['SlayTheSpire2World'], game="Slay the Spire II"):
    char_offset: int
    power_level: float
    card_rewards: int = 0
    act: int = 1
    minimum_cards: float = 0
    rest: bool = False
    # Share of this act's smith tier that counts; earlier tiers count in full. None: no smiths.
    smith: float | None = None
    remove: bool = False
    shop: bool = False
    # Region name for explanations; checks built outside the region table may leave it empty.
    region: str = ""
    # Relic rewards the run has taken by this region, which with the anytime claims limit the
    # relics that count. None counts every received relic.
    relic_rewards: int | None = None
    # Larger relic reward counts, each for a run that can reach the given elite track region.
    elite_relic_rewards: tuple[tuple[str, int], ...] = ()

    @classmethod
    def for_region(cls, char_offset: int, region: str, seeded: bool, relic_rewards: int | None = None,
                   elite_relic_rewards: tuple[tuple[str, int], ...] = ()) -> 'SpireHasPower':
        check = REGIONS[region]
        return cls(char_offset, check.power_for(seeded), card_rewards=check.card_rewards, act=check.act,
                   minimum_cards=check.minimum_cards, rest=check.rest, smith=check.smith,
                   remove=check.remove, shop=check.shop, region=region, relic_rewards=relic_rewards,
                   elite_relic_rewards=elite_relic_rewards)

    @override
    def _instantiate(self, world: 'SlayTheSpire2World') -> Rule.Resolved:
        config = next(config for config in world.characters if config.char_offset == self.char_offset)
        options = world.options
        check = PowerCheck(self.act, self.power_level, self.card_rewards, self.minimum_cards,
                           rest=self.rest, smith=self.smith, remove=self.remove, shop=self.shop)
        definition = character_registry.get(config.char_offset)
        starter = (definition and definition.starter_power) or DEFAULT_STARTER_POWER
        anytime = options.relic_rewards_available_anytime.value
        return self.Resolved(
            char=config.name,
            power_level=self.power_level,
            vanilla_cards=0 if options.shuffle_all_cards else check.vanilla_card_strength,
            card_rewards_table=card_reward_table(bool(options.shuffle_all_cards)),
            minimum_cards=self.minimum_cards,
            rest_level=check.rest_tier if options.campfire_sanity else 0,
            smith_level=check.smith_tiers if options.campfire_sanity else 0,
            smith_share=float(self.smith or 0),
            remove_level=check.removal_tiers if options.shop_sanity and options.shop_remove_slots else 0,
            act=self.act,
            neow_sanity=bool(options.neow_sanity),
            ancient_anytime=bool(options.ancient_relic_location),
            shop_sanity=bool(options.shop_sanity),
            gold_sanity=bool(options.gold_sanity),
            # With Gold Sanity, shops need gold that AP items provide, even without Shop Sanity.
            shop_scale=check.shop_scale if options.shop_sanity or options.gold_sanity else 0,
            shuffled_shop_slots=(options.shop_card_slots.value, options.shop_neutral_card_slots.value,
                                 options.shop_relic_slots.value, options.shop_potion_slots.value),
            starter_card_power=starter.card if config.progressive_starter_card else (0, 0),
            starter_relic_power=starter.relic if config.progressive_starter_relic else (0, 0),
            starter_pair_power=starter.missing_pair,
            region=self.region or f"Act {self.act}",
            relic_cap=None if self.relic_rewards is None else anytime + self.relic_rewards,
            elite_relic_caps=tuple(sorted(((region, anytime + rewards) for region, rewards in self.elite_relic_rewards),
                                          key=lambda alternative: -alternative[1])),
            player=world.player,
        )

    class Resolved(Rule.Resolved):
        char: str
        power_level: float
        vanilla_cards: float
        card_rewards_table: tuple[float, ...]
        minimum_cards: float
        rest_level: int
        smith_level: int
        smith_share: float
        remove_level: int
        act: int
        neow_sanity: bool
        ancient_anytime: bool
        shop_sanity: bool
        gold_sanity: bool
        shop_scale: float
        shuffled_shop_slots: tuple[int, int, int, int]
        starter_card_power: tuple[float, float]
        starter_relic_power: tuple[float, float]
        starter_pair_power: float
        region: str
        relic_cap: int | None
        # Elite track regions, each with the relics claimable once a run can reach it, most first.
        elite_relic_caps: tuple[tuple[str, int], ...]

        def __post_init__(self) -> None:
            super().__post_init__()
            # Fill evaluates power checks millions of times, so what depends only on the rule is worked
            # out once here: item names, and each kind of missing support by how many items are held.
            # Upgrades add up as a run passes rest sites: this act's smith tier counts for the share of
            # the act's rest sites passed by this region, earlier tiers in full.
            missing_smiths = tuple(
                sum(SMITH_POWER_BY_TIER[tier] * (self.smith_share if tier + 1 == self.act else 1)
                    for tier in range(smiths, self.smith_level))
                for smiths in range(self.smith_level + 1))
            missing_removals = tuple(sum(SHOP_REMOVE_POWER_BY_TIER[removals:self.remove_level])
                                     for removals in range(self.remove_level + 1))
            # Anytime can provide later rewards early; Start of Act cannot.
            missing_ancients = tuple(sum(ANCIENT_POWER_BY_TIER[:self.act]) - sum(ANCIENT_POWER_BY_TIER[:claimable])
                                     for claimable in range((3 if self.ancient_anytime else self.act) + 1))
            # By shop slot item: how many slots are shuffled, and the value missing by slots restored.
            missing_shop_slots = tuple(
                (shuffled, tuple(sum(values[len(values) - shuffled + restored:]) for restored in range(shuffled + 1)))
                for (_, values), shuffled in zip(SHOP_SLOT_VALUES, self.shuffled_shop_slots))
            for name, value in (("items", PowerItems.of(self.char)), ("missing_smiths", missing_smiths),
                                ("missing_removals", missing_removals), ("missing_ancients", missing_ancients),
                                ("missing_shop_slots", missing_shop_slots)):
                object.__setattr__(self, name, value)

        @override
        def region_dependencies(self) -> dict[str, set[int]]:
            # Reaching an elite track region can let this check count more relics, so the entrance
            # must be checked again then.
            return {region: {id(self)} for region, _ in self.elite_relic_caps}

        def claimable_relic_cap(self, state: CollectionState) -> int | None:
            """The most relics a run can claim by this region: after the furthest elite track region
            it can reach, or else the anytime claims and the chests on the path. Power only grows
            with the cap, so this one check equals an either/or of checks, one per cap."""
            if (self.elite_relic_caps and self.relic_cap is not None
                    and state.prog_items[self.player][self.items.relic] > self.relic_cap):
                for region, cap in self.elite_relic_caps:
                    if cap <= self.relic_cap:
                        break
                    if state.can_reach_region(region, self.player):
                        return cap
            return self.relic_cap

        def tally(self, state: CollectionState, ledger: PowerLedger | None = None,
                  relic_cap: int | None | EllipsisType = ...) -> tuple[float, float, float]:
            """Card strength, power and requirement, counting at most relic_cap relics: by default
            the most this run can claim here. With a ledger, also records each contribution under its
            explanation label. Labels are built only then, and logic and Universal Tracker's
            explanation share this one calculation, so they cannot drift apart."""
            if relic_cap is ...:
                relic_cap = self.claimable_relic_cap(state)
            items: PowerItems = self.items
            held = state.prog_items[self.player]

            # Card strength.
            received = held[items.card_reward]
            table = self.card_rewards_table
            card_rewards = table[received] if received < len(table) else card_reward_strength(table, received)
            rares = held[items.rare_card_reward]
            cards = card_rewards + RARE_CARD_POWER * rares
            if ledger is not None:
                ledger.cards += [LedgerEntry(f"Card Rewards ({received} received)", card_rewards),
                                 LedgerEntry(f"Rare Card Rewards ({rares} received)", RARE_CARD_POWER * rares)]
            if self.vanilla_cards:
                cards += self.vanilla_cards
                if ledger is not None:
                    ledger.cards.append(LedgerEntry("Vanilla card rewards", self.vanilla_cards))
            # The first receipt restores the normal loadout; only the second adds power above it.
            starter_cards = held[items.starter_card]
            if starter_cards >= 2:
                cards += self.starter_card_power[1]
                if ledger is not None:
                    ledger.cards.append(LedgerEntry("Upgraded starter card", self.starter_card_power[1]))

            # Power: relics past the anytime claims are claimable only after relic rewards in the run.
            relics = held[items.relic]
            claimable_relics = relics if relic_cap is None else min(relics, relic_cap)
            power = cards + RELIC_POWER * claimable_relics
            if ledger is not None:
                label = (f"Relics ({relics} received)" if claimable_relics == relics
                         else f"Relics ({relics} received, {relic_cap} claimable)")
                ledger.relics.append(LedgerEntry(label, RELIC_POWER * claimable_relics))
            starter_relics = held[items.starter_relic]
            if starter_relics >= 2:
                power += self.starter_relic_power[1]
                if ledger is not None:
                    ledger.relics.append(LedgerEntry("Upgraded starter relic", self.starter_relic_power[1]))

            # Requirement: base power plus missing support, added in the order the explanation lists it.
            required = self.power_level
            adjustments = ledger.adjustments if ledger is not None else None
            missing_starter = False
            for kind, weights, held_starters in (("Card", self.starter_card_power, starter_cards),
                                                 ("Relic", self.starter_relic_power, starter_relics)):
                if weights[0] and not held_starters:
                    required += weights[0]
                    missing_starter = True
                    if adjustments is not None:
                        adjustments[f"Missing Starter {kind}"] = weights[0]
            if self.starter_pair_power and missing_starter:
                required += self.starter_pair_power
                if adjustments is not None:
                    adjustments["Missing starter card/relic synergy"] = self.starter_pair_power

            smiths = held[items.smith]
            if self.rest_level and held[items.rest] < self.rest_level:
                # A rest site offers one action; with this act's smith tier the run smiths there instead.
                rest_power = REST_POWER_BY_ACT[self.act - 1]
                smithing_instead = smiths >= self.rest_level
                if smithing_instead:
                    rest_power *= REST_SHARE_WHILE_SMITHING
                required += rest_power
                if adjustments is not None:
                    label = f"Missing Rest tier {self.rest_level}"
                    adjustments[label + " (smithing instead)" if smithing_instead else label] = rest_power

            missing = self.missing_smiths[min(smiths, self.smith_level)]
            if missing:
                required += missing
                if adjustments is not None:
                    # An Early region counts none of its own act's tier, so leave that tier out of the label.
                    last = self.smith_level if self.smith_share else self.smith_level - 1
                    label = f"Missing Progressive Smith {tier_range(smiths + 1, last)}"
                    if 0 < self.smith_share < 1:
                        own = "" if smiths + 1 == last else f"tier {last} "
                        label += f" ({own}x{self.smith_share:g}: share of Act {self.act}'s rest sites passed)"
                    adjustments[label] = missing

            removals = held[items.shop_remove]
            missing = self.missing_removals[min(removals, self.remove_level)]
            if missing:
                required += missing
                if adjustments is not None:
                    adjustments[f"Missing Progressive Shop Remove {tier_range(removals + 1, self.remove_level)}"] = \
                        missing

            # Missing rewards need replacement power wherever no hard gate applies.
            ancients = held[items.ancient] + int(not self.neow_sanity)
            missing = self.missing_ancients[min(len(self.missing_ancients) - 1, ancients)]
            if missing:
                required += missing
                if adjustments is not None:
                    adjustments[f"Ancient support ({'Anytime' if self.ancient_anytime else 'Start of Act'})"] = missing

            if self.shop_scale:
                # Missing shop choices, then the missing gold to buy the choices a shop does offer.
                missing_choices = 0.0
                if self.shop_sanity:
                    for (shuffled, missing_by_restored), item in zip(self.missing_shop_slots, items.shop_slots):
                        missing_choices += missing_by_restored[min(held[item], shuffled)]
                if missing_choices:
                    missing = missing_choices * self.shop_scale
                    required += missing
                    if ledger is not None:
                        ledger.adjustments["Missing shop choices"] = ledger.shop["Missing shop choices"] = missing
                if self.gold_sanity:
                    gold = gold_sanity_run_gold(self.act, held[items.elite_gold], held[items.boss_gold])
                    needed = normal_run_gold(self.act)
                    if gold < needed:
                        offered = FULL_SHOP_PAGE_VALUE - missing_choices
                        missing = offered * self.shop_scale * (1 - gold / needed)
                        required += missing
                        if ledger is not None:
                            label = f"Missing shop gold ({gold} of {needed})"
                            ledger.adjustments[label] = ledger.shop[label] = missing
            return cards, power, required

        def ledger(self, state: CollectionState, relic_cap: int | None | EllipsisType = ...) -> PowerLedger:
            ledger = PowerLedger()
            self.tally(state, ledger, relic_cap)
            return ledger

        def card_entries(self, state: CollectionState) -> List[LedgerEntry]:
            """Contributions to card strength, in the order they are added up."""
            return self.ledger(state).cards

        def relic_entries(self, state: CollectionState) -> List[LedgerEntry]:
            """Contributions to power beyond card strength, in the order they are added up."""
            return self.ledger(state).relics

        def strength(self, state: CollectionState) -> tuple[float, float, float]:
            return self.tally(state)

        def power_adjustments(self, state: CollectionState) -> dict[str, float]:
            """Missing support, in the order it is added to the requirement."""
            return self.ledger(state).adjustments

        def shop_penalty(self, state: CollectionState) -> float:
            return sum(self.ledger(state).shop.values())

        @override
        def _evaluate(self, state: CollectionState) -> bool:
            cards, power, required = self.tally(state)
            return cards >= self.minimum_cards and power >= required

        @override
        def explain_json(self, state: CollectionState | None = None) -> List[JSONMessagePart]:
            if state is None:
                lines = self.describe()
            else:
                lines = self.ledger_lines(state)
            return [{"type": "text", "text": "\n".join(lines) + "\n"}]

        def ledger_lines(self, state: CollectionState) -> List[str]:
            ledger = PowerLedger()
            cards, power, required = self.tally(state, ledger)
            lines = [
                f"{self.char} {self.region} power check: {'met' if self._evaluate(state) else 'not met'}",
                f"  Card strength {cards:g} of {self.minimum_cards:g} needed",
                *(f"    {entry.label}: {entry.amount:g}" for entry in ledger.cards),
                f"  Power {power:g} of {required:g} needed",
                f"    Card strength: {cards:g}",
                *(f"    {entry.label}: {entry.amount:g}" for entry in ledger.relics),
                *(f"    At most {cap} claimable once {region} is reachable"
                  f" ({'reached' if state.can_reach_region(region, self.player) else 'not reached'})"
                  for region, cap in reversed(self.elite_relic_caps)),
                f"  Requirement {required:g}",
                f"    Base: {self.power_level:g}",
                *(f"    {label}: {adjustment:+g}" for label, adjustment in ledger.adjustments.items()),
                "  Missing support raises the requirement; extra power can make up for it.",
                "  Separate item gates still apply.",
            ]
            return lines

        def describe(self) -> List[str]:
            support = []
            if self.starter_card_power[0] or self.starter_relic_power[0]:
                support.append("starters")
            if self.rest_level:
                support.append(f"Rest tier {self.rest_level} (x{REST_SHARE_WHILE_SMITHING:g} while smithing works)")
            if self.smith_level:
                support.append(f"Smith {tier_range(1, self.smith_level)} (tier {self.act} x{self.smith_share:g}: "
                               f"share of Act {self.act}'s rest sites passed)")
            if self.remove_level:
                support.append(f"Shop Remove {tier_range(1, self.remove_level)}")
            support.append(f"Ancient {tier_range(1, self.act)}")
            if self.shop_scale:
                support.append("shop choices and gold" if self.gold_sanity else "shop choices")
            table = self.card_rewards_table
            strengths = " / ".join(f"{strength:g}" for strength in table)
            later_step = card_reward_strength(table, len(table)) - table[-1]
            lines = [
                f"{self.char} {self.region} power check",
                f"  Base power {self.power_level:g}; card strength at least {self.minimum_cards:g}.",
                f"  Card strength by ordinary card rewards held: {strengths}; each later one adds {later_step:.3g}.",
                f"  Rare card rewards {RARE_CARD_POWER:g}; relics {RELIC_POWER:g}.",
            ]
            if self.relic_cap is not None:
                lines.append(f"  At most {self.relic_cap} relics count: the anytime claims and relic rewards so far.")
            for region, cap in reversed(self.elite_relic_caps):
                lines.append(f"  Or at most {cap} once {region} is reachable.")
            if self.vanilla_cards:
                lines.append(f"  Vanilla card rewards add {self.vanilla_cards:g} card strength.")
            lines.extend((
                f"  Missing support raises the requirement: {', '.join(support)}.",
                "  Separate item gates still apply.",
            ))
            return lines


@dataclasses.dataclass()
class SpireHasGold(Rule['SlayTheSpire2World'], game="Slay the Spire II"):
    char: str
    gold: int

    @override
    def _instantiate(self, world: 'SlayTheSpire2World') -> Rule.Resolved:
        if self.gold <= 0:
            return True_().resolve(world)
        return self.Resolved(self.gold, self.char, player=world.player)

    class Resolved(Rule.Resolved):
        gold: int
        char: str

        def gold_entries(self, state: CollectionState) -> List[LedgerEntry]:
            entries = []
            for item, amount in (("Elite Gold", ELITE_GOLD), ("Boss Gold", BOSS_GOLD)):
                received = state.count(f"{self.char} {item}", self.player)
                entries.append(LedgerEntry(f"{item} ({received} received)", received * amount))
            return entries

        @override
        def _evaluate(self, state: CollectionState) -> bool:
            return sum(entry.amount for entry in self.gold_entries(state)) >= self.gold

        @override
        def explain_json(self, state: CollectionState | None = None) -> List[JSONMessagePart]:
            if state is None:
                lines = [f"{self.char} shop gold: {self.gold:g} from Elite Gold ({ELITE_GOLD:g} each) "
                         f"and Boss Gold ({BOSS_GOLD:g} each)"]
            else:
                entries = self.gold_entries(state)
                lines = [f"{self.char} shop gold: {sum(entry.amount for entry in entries):g} "
                         f"of {self.gold:g} needed",
                         *(f"  {entry.label}: {entry.amount:g}" for entry in entries)]
            lines.append("  Starting gold and chest gold are already counted.")
            return [{"type": "text", "text": "\n".join(lines) + "\n"}]


def shop_gold_by_act(options: 'Spire2Options') -> tuple[int, int, int]:
    """Gold from Elite and Boss Gold that each act's shop needs. Without Gold Sanity, vanilla
    gold rewards pay for shops, and logic does not count them."""
    if not (options.shop_sanity and options.gold_sanity):
        return 0, 0, 0
    page = shop_page(options.shop_card_slots.value, options.shop_neutral_card_slots.value,
                     options.shop_relic_slots.value, options.shop_potion_slots.value,
                     bool(options.shop_remove_slots))
    cost_mode = options.shop_sanity_costs.value
    return (shop_gold_needed(page, 1, cost_mode), shop_gold_needed(page, 2, cost_mode),
            shop_gold_needed(page, 3, cost_mode))


def region_power(offset: int, prefix: str, region: str, seeded: bool) -> Rule:
    """The region's power check, counting the relics a run can claim by then: walking around
    elites, or after an earlier act's elites when their elite track region is reachable. One check
    counts the run's items once for all of these."""
    elite_relic_rewards = tuple((f"{prefix} {elite}", rewards)
                                for elite, rewards in ELITE_RELIC_REWARDS.get(region, {}).items())
    return SpireHasPower.for_region(offset, region, seeded, relic_rewards=PATH_RELIC_REWARDS[region],
                                    elite_relic_rewards=elite_relic_rewards)


def set_rules(world: 'SlayTheSpire2World') -> None:
    for config in world.characters:
        _set_rules(world, config)
    num_goals = world.options.num_chars_goal.value or len(world.characters)
    assert num_goals > 0
    world.set_completion_rule(HasFromListUnique(
        *[f"{config.name} Victory" for config in world.characters], count=num_goals))


def _set_rules(world: 'SlayTheSpire2World', config: CharacterConfig) -> None:
    prefix = config.name
    offset = config.char_offset
    # Start of Act checkpoints require the corresponding Ancient reward in the client.
    ancient_filter = dict(options=[OptionFilter(AncientRelicLocation, 0)], filtered_resolution=True)
    neow_tier = int(bool(world.options.neow_sanity))
    world.set_rule(world.get_entrance(f"{prefix} Early Act 1"), Has(f"{prefix} Unlock"))

    seeded = bool(world.options.seeded)
    region_rules: dict[str, Rule] = {region: region_power(offset, prefix, region, seeded) for region in REGIONS}
    # Relic counts on the elite track assume every earlier elite was fought: each later elite is
    # entered from the one before, and each act's chest and first elite need the previous act's last.
    for act in (2, 3):
        for region in (f"Act {act} Chest", f"Act {act} Elite 1"):
            region_rules[region] &= CanReachRegion(f"{prefix} {last_elite(act - 1)}")
    # Item gates that extra power cannot replace.
    for act in (2, 3):
        region_rules[f"Early Act {act}"] &= Has(f"{prefix} Progressive Ancient", count=act - 1 + neow_tier,
                                                **ancient_filter)
    for region, rule in region_rules.items():
        world.set_rule(world.get_entrance(f"{prefix} {region}"), rule)

    if world.options.shop_sanity:
        for act, gold in enumerate(shop_gold_by_act(world.options), start=1):
            world.set_rule(world.get_entrance(f"{prefix} Act {act} Shop"), SpireHasGold(prefix, gold))
