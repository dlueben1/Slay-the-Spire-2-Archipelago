import dataclasses
from typing import TYPE_CHECKING, List, NamedTuple
from typing_extensions import override

from BaseClasses import CollectionState
from NetUtils import JSONMessagePart
from rule_builder.options import OptionFilter
from rule_builder.rules import Rule, True_, CanReachRegion, Has, HasFromListUnique
from .characters import CharacterConfig, VANILLA_CHARACTERS, DEFAULT_STARTER_POWER
from .logic_table import (
    ANCIENT_POWER_BY_TIER, BOSS_GOLD, ELITE_GOLD, FULL_VALUE_CARD_REWARDS, FULL_VALUE_CARD_REWARDS_ALL_SHUFFLED,
    LATER_CARD_POWER, RARE_CARD_POWER, REGIONS, RELIC_POWER, REST_POWER_BY_ACT, SHOP_GOLD_BY_ACT,
    SHOP_REMOVE_POWER_BY_TIER, SHOP_SLOT_VALUES, SMITH_POWER_BY_TIER, PowerCheck,
)
from .options import AncientRelicLocation, CampfireSanity, GoldSanity

if TYPE_CHECKING:
    from .world import SlayTheSpire2World


# Sparse-opening item guarantees use the same budget as this first power check.
MID_ACT_1_POWER = REGIONS["Mid Act 1"].power

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


@dataclasses.dataclass()
class SpireHasPower(Rule['SlayTheSpire2World'], game="Slay the Spire II"):
    char_offset: int
    power_level: float
    card_rewards: int = 0
    act: int = 1
    minimum_cards: float = 0
    rest: bool = False
    smith: bool = False
    remove: bool = False
    shop: bool = False
    # Region name for explanations; checks built outside the region table may leave it empty.
    region: str = ""

    @classmethod
    def for_region(cls, char_offset: int, region: str, seeded: bool) -> 'SpireHasPower':
        check = REGIONS[region]
        return cls(char_offset, check.power_for(seeded), card_rewards=check.card_rewards, act=check.act,
                   minimum_cards=check.minimum_cards, rest=check.rest, smith=check.smith,
                   remove=check.remove, shop=check.shop, region=region)

    @override
    def _instantiate(self, world: 'SlayTheSpire2World') -> Rule.Resolved:
        config = next(config for config in world.characters if config.char_offset == self.char_offset)
        options = world.options
        check = PowerCheck(self.act, self.power_level, self.card_rewards, self.minimum_cards,
                           rest=self.rest, smith=self.smith, remove=self.remove, shop=self.shop)
        # The APWorld cannot verify a modded character's Orobas mappings. Do not assume
        # those receipts grant upgrades when the client may leave its starters unchanged.
        reviewed_starters = config.mod_num == 0
        return self.Resolved(
            char=config.name,
            power_level=self.power_level,
            vanilla_cards=0 if options.shuffle_all_cards else check.vanilla_card_strength,
            full_value_cards=(FULL_VALUE_CARD_REWARDS_ALL_SHUFFLED if options.shuffle_all_cards
                              else FULL_VALUE_CARD_REWARDS),
            minimum_cards=self.minimum_cards,
            rest_level=check.rest_tier if options.campfire_sanity else 0,
            smith_level=check.smith_tiers if options.campfire_sanity else 0,
            remove_level=check.removal_tiers if options.shop_sanity and options.shop_remove_slots else 0,
            act=self.act,
            neow_sanity=bool(options.neow_sanity),
            ancient_anytime=bool(options.ancient_relic_location),
            shop_sanity=bool(options.shop_sanity),
            shop_scale=check.shop_scale if options.shop_sanity else 0,
            shuffled_shop_slots=(options.shop_card_slots.value, options.shop_neutral_card_slots.value,
                                 options.shop_relic_slots.value, options.shop_potion_slots.value),
            starter_card_power=STARTER_CARD_POWER.get(config.name, DEFAULT_STARTER_POWER.card)
                               if reviewed_starters and config.progressive_starter_card else (0, 0),
            starter_relic_power=STARTER_RELIC_POWER.get(config.name, DEFAULT_STARTER_POWER.relic)
                                if reviewed_starters and config.progressive_starter_relic else (0, 0),
            starter_pair_power=STARTER_PAIR_POWER.get(config.name, DEFAULT_STARTER_POWER.missing_pair)
                               if reviewed_starters else 0,
            region=self.region or f"Act {self.act}",
            player=world.player,
        )

    class Resolved(Rule.Resolved):
        char: str
        power_level: float
        vanilla_cards: float
        full_value_cards: int
        minimum_cards: float
        rest_level: int
        smith_level: int
        remove_level: int
        act: int
        neow_sanity: bool
        ancient_anytime: bool
        shop_sanity: bool
        shop_scale: float
        shuffled_shop_slots: tuple[int, int, int, int]
        starter_card_power: tuple[float, float]
        starter_relic_power: tuple[float, float]
        starter_pair_power: float
        region: str

        def count(self, state: CollectionState, item: str) -> int:
            return state.count(f"{self.char} {item}", self.player)

        def card_entries(self, state: CollectionState) -> List[LedgerEntry]:
            """Contributions to card strength, in the order they are added up."""
            received = self.count(state, "Card Reward")
            entries = [
                # Later ordinary choices are more likely to be skipped.
                LedgerEntry(f"Card Rewards ({received} received)",
                            min(received, self.full_value_cards)
                            + LATER_CARD_POWER * max(0, received - self.full_value_cards)),
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
            # Credit receipts immediately, as in the previous APWorld. Claim timing
            # remains a client setting rather than an additional logic restriction.
            relics = self.count(state, "Relic")
            entries = [LedgerEntry(f"Relics ({relics} received)", RELIC_POWER * relics)]
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
                adjustments[f"Missing Rest tier {self.rest_level}"] = REST_POWER_BY_ACT[self.act - 1]

            for item, level, weights in (
                ("Progressive Smith", self.smith_level, SMITH_POWER_BY_TIER),
                ("Progressive Shop Remove", self.remove_level, SHOP_REMOVE_POWER_BY_TIER),
            ):
                received = self.count(state, item)
                missing = sum(weights[received:level])
                if missing:
                    adjustments[f"Missing {item} tiers {received + 1}-{level}"] = missing

            # Anytime can provide later rewards early; Start of Act cannot.
            # Missing rewards need replacement power wherever no hard gate applies.
            received = self.count(state, "Progressive Ancient") + int(not self.neow_sanity)
            claimable = min(3 if self.ancient_anytime else self.act, received)
            ancient_adjustment = sum(ANCIENT_POWER_BY_TIER[:self.act]) - sum(ANCIENT_POWER_BY_TIER[:claimable])
            if ancient_adjustment:
                mode = "Anytime" if self.ancient_anytime else "Start of Act"
                adjustments[f"Ancient support ({mode})"] = ancient_adjustment
            shop_penalty = self.shop_penalty(state)
            if shop_penalty:
                adjustments["Missing shop choices"] = shop_penalty
            return adjustments

        def shop_penalty(self, state: CollectionState) -> float:
            if not self.shop_scale:
                return 0
            return self.missing_shop_value(state) * self.shop_scale

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
            def up_to(tier: int) -> str:
                return "tier 1" if tier == 1 else f"tiers 1-{tier}"

            support = []
            if self.starter_card_power[0] or self.starter_relic_power[0]:
                support.append("starters")
            if self.rest_level:
                support.append(f"Rest tier {self.rest_level}")
            if self.smith_level:
                support.append(f"Smith {up_to(self.smith_level)}")
            if self.remove_level:
                support.append(f"Shop Remove {up_to(self.remove_level)}")
            support.append(f"Ancient {up_to(self.act)}")
            if self.shop_scale:
                support.append("shop choices")
            lines = [
                f"{self.char} {self.region} power check",
                f"  Base power {self.power_level:g}; card strength at least {self.minimum_cards:g}.",
                f"  Ordinary card rewards: first {self.full_value_cards} worth 1 each, "
                f"later ones {LATER_CARD_POWER:g}; rare card rewards {RARE_CARD_POWER:g}; relics {RELIC_POWER:g}.",
            ]
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

        @override
        def _evaluate(self, state: CollectionState) -> bool:
            return (state.count(f"{self.char} Elite Gold", self.player) * ELITE_GOLD
                    + state.count(f"{self.char} Boss Gold", self.player) * BOSS_GOLD >= self.gold)


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
    gold_filter = dict(options=[OptionFilter(GoldSanity, 1)], filtered_resolution=True)
    # Start of Act checkpoints require the corresponding Ancient reward in the client.
    ancient_filter = dict(options=[OptionFilter(AncientRelicLocation, 0)], filtered_resolution=True)
    neow_tier = int(bool(world.options.neow_sanity))
    world.set_rule(world.get_entrance(f"{prefix} Early Act 1"), Has(f"{prefix} Unlock"))

    seeded = bool(world.options.seeded)
    region_rules: dict[str, Rule] = {region: SpireHasPower.for_region(offset, region, seeded) for region in REGIONS}
    # Relic counts in later Elites regions assume the earlier acts' elites were fought too.
    for act in (2, 3):
        region_rules[f"Act {act} Elites"] &= CanReachRegion(f"{prefix} Act {act - 1} Elites")
    # Item gates that extra power cannot replace.
    if config.mod_num:
        # Unknown starter effects keep the conservative 'gates' for modded characters.
        if config.progressive_starter_relic:
            region_rules["Late Act 1"] &= Has(f"{prefix} Progressive Starter Relic")
        if config.progressive_starter_card:
            region_rules["Late Act 1"] &= Has(f"{prefix} Progressive Starter Card")
    region_rules["Act 1 Boss Arena"] &= Has(f"{prefix} Progressive Smith",
                                            options=[OptionFilter(CampfireSanity, 1)], filtered_resolution=True)
    for act in (2, 3):
        region_rules[f"Early Act {act}"] &= Has(f"{prefix} Progressive Ancient", count=act - 1 + neow_tier,
                                                **ancient_filter)
    for region, rule in region_rules.items():
        world.set_rule(world.get_entrance(f"{prefix} {region}"), rule)

    if world.options.shop_sanity:
        for act, purchase_gold in enumerate(SHOP_GOLD_BY_ACT, start=1):
            world.set_rule(world.get_entrance(f"{prefix} Act {act} Shop"),
                           SpireHasGold(prefix, purchase_gold, **gold_filter))
