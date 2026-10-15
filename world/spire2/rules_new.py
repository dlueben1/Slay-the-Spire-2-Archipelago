import dataclasses
import typing
from typing import TYPE_CHECKING, List
from typing_extensions import override

from BaseClasses import CollectionState
from NetUtils import JSONMessagePart
from rule_builder.options import OptionFilter
from rule_builder.rules import Rule, True_, Has, HasFromListUnique
from .characters import CharacterConfig, VANILLA_CHARACTERS, DEFAULT_STARTER_POWER
from .options import AncientRelicLocation, CampfireSanity, GoldSanity

if TYPE_CHECKING:
    from .world import SlayTheSpire2World


# Sparse-opening item guarantees use the same budget as this first power check.
MID_ACT_1_POWER = 4.0


# First useful choices matter more than a full page. Values are marginal per slot;
# colored-card/relic choice contributes 1.5 each, neutral 0.5, potions 1 altogether.
# The client restores entries from the end: first available is Power/rare neutral.
SHOP_SLOT_VALUES = (
    ("Shop Card Slot", 5, (0.625, 0.375, 0.25, 0.125, 0.125)),
    ("Neutral Shop Card Slot", 2, (0.375, 0.125)),
    ("Shop Relic Slot", 3, (0.75, 0.5, 0.25)),
    ("Shop Potion Slot", 3, (0.5, 0.375, 0.125)),
)


# Reviewed values live with character registration. New vanilla definitions explicitly
# select DEFAULT_STARTER_POWER until their starting loadout has been assessed.
STARTER_CARD_POWER = {definition.name: definition.starter_power.card
                      for definition in VANILLA_CHARACTERS if definition.starter_power is not None}
STARTER_RELIC_POWER = {definition.name: definition.starter_power.relic
                       for definition in VANILLA_CHARACTERS if definition.starter_power is not None}


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

    @override
    def _instantiate(self, world: 'SlayTheSpire2World') -> Rule.Resolved:
        config = next(config for config in world.characters if config.char_offset == self.char_offset)
        options = world.options
        return self.Resolved(
            char=config.name,
            power_level=self.power_level,
            vanilla_cards=0 if options.shuffle_all_cards else self.card_rewards / 2,
            full_value_cards=11 if options.shuffle_all_cards else 5,
            minimum_cards=self.minimum_cards,
            rest_level=self.act if self.rest and options.campfire_sanity else 0,
            smith_level=self.act if self.smith and options.campfire_sanity else 0,
            remove_level=(self.act if self.remove else self.act - 1)
                         if options.shop_sanity and options.shop_remove_slots else 0,
            act=self.act,
            neow_sanity=bool(options.neow_sanity),
            ancient_anytime=bool(options.ancient_relic_location),
            shop_sanity=bool(options.shop_sanity),
            shop_scale=(2 / 3, 1 / 3, 1 / 6)[self.act - 1] if self.shop and options.shop_sanity else 0,
            shuffled_shop_slots=(options.shop_card_slots.value, options.shop_neutral_card_slots.value,
                                 options.shop_relic_slots.value, options.shop_potion_slots.value),
            # The APWorld cannot verify a modded character's Orobas mappings. Do not assume
            # those receipts grant upgrades when the client may leave its starters unchanged.
            starter_card_power=STARTER_CARD_POWER.get(config.name, DEFAULT_STARTER_POWER.card)
                               if config.mod_num == 0 and config.progressive_starter_card else (0, 0),
            starter_relic_power=STARTER_RELIC_POWER.get(config.name, DEFAULT_STARTER_POWER.relic)
                                if config.mod_num == 0 and config.progressive_starter_relic else (0, 0),
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

        def strength(self, state: CollectionState) -> tuple[float, float, float]:
            received_cards = state.count(f"{self.char} Card Reward", self.player)
            # Later ordinary choices are more likely to be skipped; half shuffle reaches
            # this point with fewer AP receipts because it also provides free combat cards.
            cards = (min(received_cards, self.full_value_cards)
                     + 0.5 * max(0, received_cards - self.full_value_cards)
                     + 1.5 * state.count(f"{self.char} Rare Card Reward", self.player)
                     + self.vanilla_cards)
            # The first receipt restores the normal loadout; only the second adds power above it.
            if state.has(f"{self.char} Progressive Starter Card", self.player, 2):
                cards += self.starter_card_power[1]
            # Credit receipts immediately, as in the previous APWorld. Claim timing
            # remains a client setting rather than an additional logic restriction.
            relics = state.count(f"{self.char} Relic", self.player)
            power = cards + 1.5 * relics
            if state.has(f"{self.char} Progressive Starter Relic", self.player, 2):
                power += self.starter_relic_power[1]

            required = self.power_level
            for adjustment in self.power_adjustments(state).values():
                required += adjustment
            return cards, power, required

        def power_adjustments(self, state: CollectionState) -> dict[str, float]:
            # Share the actual adjustments with UT's explanation so it cannot drift from logic.
            adjustments = {}
            for kind, weights in (("Card", self.starter_card_power), ("Relic", self.starter_relic_power)):
                if weights[0] and not state.has(f"{self.char} Progressive Starter {kind}", self.player):
                    adjustments[f"Missing Starter {kind}"] = weights[0]
            # Normal Necrobinder starts with both halves of this synergy. Losing either half
            # needs compensation; restoring both should match leaving the starters unshuffled.
            if self.char == "Necrobinder" and adjustments:
                adjustments["Missing starter card/relic synergy"] = 0.5

            if self.rest_level and not state.has(f"{self.char} Progressive Rest", self.player, self.rest_level):
                adjustments[f"Missing Rest tier {self.rest_level}"] = (3, 3.5, 4)[self.act - 1]

            # earlier progressive smith's matter way more, act 3 one is weaker
            # for progressive shop remove's - early on it doesn't matter but later on
            # it matters more? its sort of unclear how to do shop remove's here
            # its all vibe based anyway
            for item, level, weights in (
                ("Progressive Smith", self.smith_level, (3.5, 3.0, 1.5)),
                ("Progressive Shop Remove", self.remove_level, (1, 1.5, 2)),
            ):
                received = state.count(f"{self.char} {item}", self.player)
                missing = sum(weights[received:level])
                if missing:
                    adjustments[f"Missing {item} tiers {received + 1}-{level}"] = missing

            # Anytime can provide later rewards early; Start of Act cannot.
            # Missing rewards need replacement power wherever no hard gate applies.
            ancient_weights = (2, 4.5, 4.5)
            received = state.count(f"{self.char} Progressive Ancient", self.player) + int(not self.neow_sanity)
            claimable = min(3 if self.ancient_anytime else self.act, received)
            ancient_adjustment = sum(ancient_weights[:self.act]) - sum(ancient_weights[:claimable])
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

            # later missing stuff matters less than the initial amounts
            for (item, total, values), shuffled in zip(SHOP_SLOT_VALUES, self.shuffled_shop_slots):
                restored = min(state.count(f"{self.char} {item}", self.player), shuffled)
                available = total - shuffled + restored
                missing += sum(values[available:])
            return missing

        @override
        def _evaluate(self, state: CollectionState) -> bool:
            cards, power, required = self.strength(state)
            return cards >= self.minimum_cards and power >= required

        @override
        def explain_json(self, state: CollectionState | None = None) -> List[JSONMessagePart]:
            lines = [f"{self.char} Act {self.act} power check"]
            if state is not None:
                cards, power, required = self.strength(state)
                lines.extend((
                    f"  Power: {power:g} / {required:g} required ({'met' if power >= required else 'not met'})",
                    f"  Card strength: {cards:g} / {self.minimum_cards:g} required "
                    f"({'met' if cards >= self.minimum_cards else 'not met'})",
                    f"  Power includes {cards:g} card strength and {power - cards:g} relic/starter relic power.",
                ))
            lines.append(f"  Base power requirement: {self.power_level:g}")
            if state is not None:
                for label, adjustment in self.power_adjustments(state).items():
                    lines.append(f"    {label}: {adjustment:+g}")
            else:
                lines.append(f"  Minimum card strength: {self.minimum_cards:g}; support adjusts required power.")
            lines.extend((
                f"  Ordinary cards: first {self.full_value_cards} worth 1 each, later cards 0.5; rare cards 1.5.",
                f"  Expected unshuffled card power: {self.vanilla_cards:g}.",
                "  Support adjustments can be offset by power. Separate item gates still apply.",
            ))
            return [{"type": "text", "text": "\n".join(lines) + "\n"}]


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
            # Keep the conservative Poverty allowance. Combat/filler gold is not progression.
            return (state.count(f"{self.char} Elite Gold", self.player) * 30
                    + state.count(f"{self.char} Boss Gold", self.player) * 75 >= self.gold)


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

    # Budget roughly seven combat rewards per act: three before Mid, five before Late,
    # seven before the boss. Half shuffle leaves half of those rewards as free cards.
    world.set_rule(world.get_entrance(f"{prefix} Mid Act 1"),
                   SpireHasPower(offset, MID_ACT_1_POWER, card_rewards=3, minimum_cards=1.0, rest=True))
    late_act_1 = SpireHasPower(offset, 10, card_rewards=5, minimum_cards=1.0, rest=True, shop=True)
    if config.mod_num:
        # Unknown starter effects keep the conservative 'gates' for modded characters.
        if config.progressive_starter_relic:
            late_act_1 &= Has(f"{prefix} Progressive Starter Relic")
        if config.progressive_starter_card:
            late_act_1 &= Has(f"{prefix} Progressive Starter Card")
    world.set_rule(world.get_entrance(f"{prefix} Late Act 1"), late_act_1)
    world.set_rule(world.get_entrance(f"{prefix} Act 1 Boss Arena"),
                   SpireHasPower(offset, 11.5, card_rewards=7, minimum_cards=3.0,
                                 rest=True, smith=True, remove=True, shop=True)
                   & Has(f"{prefix} Progressive Smith", options=[OptionFilter(CampfireSanity, 1)], filtered_resolution=True))

    world.set_rule(world.get_entrance(f"{prefix} Early Act 2"),
                   SpireHasPower(offset, 12.0, card_rewards=7, act=2, minimum_cards=3.0, smith=True, shop=True)
                   & Has(f"{prefix} Progressive Ancient", count=1 + neow_tier, **ancient_filter))
    # Later acts account for current-act smithing from entry, and resting from Mid Act.
    # Act 1 keeps smithing at the boss to avoid tightening the sparse floorless opening.
    world.set_rule(world.get_entrance(f"{prefix} Mid Act 2"),
                   SpireHasPower(offset, 14.0, card_rewards=10, act=2, minimum_cards=3.0, rest=True, smith=True, shop=True))
    world.set_rule(world.get_entrance(f"{prefix} Late Act 2"),
                   SpireHasPower(offset, 18.0, card_rewards=12, act=2, minimum_cards=3.0, rest=True, smith=True, shop=True))
    world.set_rule(world.get_entrance(f"{prefix} Act 2 Boss Arena"),
                   SpireHasPower(offset, 23.5, card_rewards=14, act=2, minimum_cards=5.0,
                                 rest=True, smith=True, remove=True, shop=True))

    world.set_rule(world.get_entrance(f"{prefix} Early Act 3"),
                   SpireHasPower(offset, 23.5, card_rewards=14, act=3, minimum_cards=5.0, smith=True, shop=True)
                   & Has(f"{prefix} Progressive Ancient", count=2 + neow_tier, **ancient_filter))
    world.set_rule(world.get_entrance(f"{prefix} Mid Act 3"),
                   SpireHasPower(offset, 25.5, card_rewards=17, act=3, minimum_cards=5.0, rest=True, smith=True, shop=True))
    world.set_rule(world.get_entrance(f"{prefix} Late Act 3"),
                   SpireHasPower(offset, 27.0, card_rewards=19, act=3, minimum_cards=5.0, rest=True, smith=True, shop=True))
    world.set_rule(world.get_entrance(f"{prefix} Act 3 Boss Arena"),
                   SpireHasPower(offset, 30.0, card_rewards=21, act=3, minimum_cards=7.0,
                                 rest=True, smith=True, remove=True, shop=True))

    if world.options.shop_sanity:
        for act, purchase_gold in enumerate((50, 150, 270), start=1):
            world.set_rule(world.get_entrance(f"{prefix} Act {act} Shop"),
                           SpireHasGold(prefix, purchase_gold, **gold_filter))
