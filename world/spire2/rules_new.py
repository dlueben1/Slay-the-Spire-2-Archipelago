import dataclasses
from typing import TYPE_CHECKING, List, NamedTuple
from typing_extensions import override

from BaseClasses import CollectionState
from NetUtils import JSONMessagePart
from rule_builder.options import OptionFilter
from rule_builder.rules import Rule, True_, CanReachRegion, Has, HasFromListUnique
from .characters import CharacterConfig, VANILLA_CHARACTERS, DEFAULT_STARTER_POWER
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


# Reviewed values live with character registration. New vanilla definitions explicitly
# select DEFAULT_STARTER_POWER until their starting loadout has been assessed.
STARTER_CARD_POWER = {definition.name: definition.starter_power.card
                      for definition in VANILLA_CHARACTERS if definition.starter_power is not None}
STARTER_RELIC_POWER = {definition.name: definition.starter_power.relic
                       for definition in VANILLA_CHARACTERS if definition.starter_power is not None}
STARTER_PAIR_POWER = {definition.name: definition.starter_power.missing_pair
                      for definition in VANILLA_CHARACTERS if definition.starter_power is not None}


class LedgerEntry(NamedTuple):
    label: str
    amount: float


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

    @classmethod
    def for_region(cls, char_offset: int, region: str, seeded: bool,
                   relic_rewards: int | None = None) -> 'SpireHasPower':
        check = REGIONS[region]
        return cls(char_offset, check.power_for(seeded), card_rewards=check.card_rewards, act=check.act,
                   minimum_cards=check.minimum_cards, rest=check.rest, smith=check.smith,
                   remove=check.remove, shop=check.shop, region=region, relic_rewards=relic_rewards)

    @override
    def _instantiate(self, world: 'SlayTheSpire2World') -> Rule.Resolved:
        config = next(config for config in world.characters if config.char_offset == self.char_offset)
        options = world.options
        check = PowerCheck(self.act, self.power_level, self.card_rewards, self.minimum_cards,
                           rest=self.rest, smith=self.smith, remove=self.remove, shop=self.shop)
        if config.mod_num:
            # Modded characters are assumed to define Archaic Tooth and Touch of Orobas mappings,
            # as every predefined one does, so their starters use the default values.
            card_power = DEFAULT_STARTER_POWER.card
            relic_power = DEFAULT_STARTER_POWER.relic
            pair_power = DEFAULT_STARTER_POWER.missing_pair
        else:
            card_power = STARTER_CARD_POWER.get(config.name, DEFAULT_STARTER_POWER.card)
            relic_power = STARTER_RELIC_POWER.get(config.name, DEFAULT_STARTER_POWER.relic)
            pair_power = STARTER_PAIR_POWER.get(config.name, DEFAULT_STARTER_POWER.missing_pair)
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
            starter_card_power=card_power if config.progressive_starter_card else (0, 0),
            starter_relic_power=relic_power if config.progressive_starter_relic else (0, 0),
            starter_pair_power=pair_power,
            region=self.region or f"Act {self.act}",
            relic_cap=None if self.relic_rewards is None
                      else options.relic_rewards_available_anytime.value + self.relic_rewards,
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

        def count(self, state: CollectionState, item: str) -> int:
            return state.count(f"{self.char} {item}", self.player)

        def card_entries(self, state: CollectionState) -> List[LedgerEntry]:
            """Contributions to card strength, in the order they are added up."""
            received = self.count(state, "Card Reward")
            entries = [
                LedgerEntry(f"Card Rewards ({received} received)",
                            card_reward_strength(self.card_rewards_table, received)),
                LedgerEntry(f"Rare Card Rewards ({self.count(state, 'Rare Card Reward')} received)",
                            RARE_CARD_POWER * self.count(state, "Rare Card Reward")),
            ]
            if self.vanilla_cards:
                entries.append(LedgerEntry("Vanilla card rewards", self.vanilla_cards))
            # The first receipt restores the normal loadout; only the second adds power above it.
            if self.count(state, "Progressive Starter Card") >= 2:
                entries.append(LedgerEntry("Upgraded starter card", self.starter_card_power[1]))
            return entries

        def relic_entries(self, state: CollectionState) -> List[LedgerEntry]:
            """Contributions to power beyond card strength, in the order they are added up."""
            # Relics past the anytime claims are claimable only after relic rewards in the run.
            relics = self.count(state, "Relic")
            label = f"Relics ({relics} received)"
            if self.relic_cap is not None and relics > self.relic_cap:
                label = f"Relics ({relics} received, {self.relic_cap} claimable)"
                relics = self.relic_cap
            entries = [LedgerEntry(label, RELIC_POWER * relics)]
            if self.count(state, "Progressive Starter Relic") >= 2:
                entries.append(LedgerEntry("Upgraded starter relic", self.starter_relic_power[1]))
            return entries

        def strength(self, state: CollectionState) -> tuple[float, float, float]:
            cards = 0.0
            for entry in self.card_entries(state):
                cards += entry.amount
            power = cards
            for entry in self.relic_entries(state):
                power += entry.amount
            required = self.power_level
            for adjustment in self.power_adjustments(state).values():
                required += adjustment
            return cards, power, required

        def power_adjustments(self, state: CollectionState) -> dict[str, float]:
            """Missing support, in the order it is added to the requirement."""
            # Share the actual adjustments with UT's explanation so it cannot drift from logic.
            adjustments = {}
            for kind, weights in (("Card", self.starter_card_power), ("Relic", self.starter_relic_power)):
                if weights[0] and not self.count(state, f"Progressive Starter {kind}"):
                    adjustments[f"Missing Starter {kind}"] = weights[0]
            if self.starter_pair_power and adjustments:
                adjustments["Missing starter card/relic synergy"] = self.starter_pair_power

            if self.rest_level and self.count(state, "Progressive Rest") < self.rest_level:
                # A rest site offers one action; with this act's smith tier the run smiths there instead.
                label = f"Missing Rest tier {self.rest_level}"
                rest_power = REST_POWER_BY_ACT[self.act - 1]
                if self.count(state, "Progressive Smith") >= self.rest_level:
                    label += " (smithing instead)"
                    rest_power *= REST_SHARE_WHILE_SMITHING
                adjustments[label] = rest_power

            # Upgrades add up as a run passes rest sites: this act's smith tier counts for the share of
            # the act's rest sites passed by this region, earlier tiers in full.
            smiths = self.count(state, "Progressive Smith")
            missing = sum(SMITH_POWER_BY_TIER[tier] * (self.smith_share if tier + 1 == self.act else 1)
                          for tier in range(smiths, self.smith_level))
            if missing:
                # An Early region counts none of its own act's tier, so leave that tier out of the label.
                last = self.smith_level if self.smith_share else self.smith_level - 1
                label = f"Missing Progressive Smith {tier_range(smiths + 1, last)}"
                if 0 < self.smith_share < 1:
                    own = "" if smiths + 1 == last else f"tier {last} "
                    label += f" ({own}x{self.smith_share:g}: share of Act {self.act}'s rest sites passed)"
                adjustments[label] = missing

            removals = self.count(state, "Progressive Shop Remove")
            missing = sum(SHOP_REMOVE_POWER_BY_TIER[removals:self.remove_level])
            if missing:
                adjustments[f"Missing Progressive Shop Remove {tier_range(removals + 1, self.remove_level)}"] = missing

            # Anytime can provide later rewards early; Start of Act cannot.
            # Missing rewards need replacement power wherever no hard gate applies.
            received = self.count(state, "Progressive Ancient") + int(not self.neow_sanity)
            claimable = min(3 if self.ancient_anytime else self.act, received)
            ancient_adjustment = sum(ANCIENT_POWER_BY_TIER[:self.act]) - sum(ANCIENT_POWER_BY_TIER[:claimable])
            if ancient_adjustment:
                mode = "Anytime" if self.ancient_anytime else "Start of Act"
                adjustments[f"Ancient support ({mode})"] = ancient_adjustment
            adjustments.update(self.shop_adjustments(state))
            return adjustments

        def shop_penalty(self, state: CollectionState) -> float:
            return sum(self.shop_adjustments(state).values())

        def shop_adjustments(self, state: CollectionState) -> dict[str, float]:
            """Missing shop choices, then the missing gold to buy the choices a shop does offer."""
            if not self.shop_scale:
                return {}
            adjustments = {}
            missing = self.missing_shop_value(state)
            if missing:
                adjustments["Missing shop choices"] = missing * self.shop_scale
            if self.gold_sanity:
                gold = gold_sanity_run_gold(self.act, self.count(state, "Elite Gold"), self.count(state, "Boss Gold"))
                needed = normal_run_gold(self.act)
                if gold < needed:
                    offered = FULL_SHOP_PAGE_VALUE - missing
                    adjustments[f"Missing shop gold ({gold} of {needed})"] = \
                        offered * self.shop_scale * (1 - gold / needed)
            return adjustments

        def missing_shop_value(self, state: CollectionState) -> float:
            if not self.shop_sanity:
                return 0.0
            missing = 0.0
            for (item, values), shuffled in zip(SHOP_SLOT_VALUES, self.shuffled_shop_slots):
                restored = min(self.count(state, item), shuffled)
                available = len(values) - shuffled + restored
                missing += sum(values[available:])
            return missing

        @override
        def _evaluate(self, state: CollectionState) -> bool:
            cards, power, required = self.strength(state)
            return cards >= self.minimum_cards and power >= required

        @override
        def explain_json(self, state: CollectionState | None = None) -> List[JSONMessagePart]:
            if state is None:
                lines = self.describe()
            else:
                lines = self.ledger_lines(state)
            return [{"type": "text", "text": "\n".join(lines) + "\n"}]

        def ledger_lines(self, state: CollectionState) -> List[str]:
            cards, power, required = self.strength(state)
            lines = [
                f"{self.char} {self.region} power check: {'met' if self._evaluate(state) else 'not met'}",
                f"  Card strength {cards:g} of {self.minimum_cards:g} needed",
                *(f"    {entry.label}: {entry.amount:g}" for entry in self.card_entries(state)),
                f"  Power {power:g} of {required:g} needed",
                f"    Card strength: {cards:g}",
                *(f"    {entry.label}: {entry.amount:g}" for entry in self.relic_entries(state)),
                f"  Requirement {required:g}",
                f"    Base: {self.power_level:g}",
                *(f"    {label}: {adjustment:+g}" for label, adjustment in self.power_adjustments(state).items()),
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
    elites, or after fighting every elite up to an earlier act's last elite when it is reachable."""
    rule: Rule = SpireHasPower.for_region(offset, region, seeded, relic_rewards=PATH_RELIC_REWARDS[region])
    for elites, rewards in ELITE_RELIC_REWARDS.get(region, {}).items():
        rule |= (SpireHasPower.for_region(offset, region, seeded, relic_rewards=rewards)
                 & CanReachRegion(f"{prefix} {elites}"))
    return rule


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
