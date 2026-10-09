import logging
import re
import string
import typing
from copy import deepcopy
from math import ceil
from typing import List, Optional, Any

from BaseClasses import CollectionState, Item, Location, Region, MultiWorld, ItemClassification
from Options import OptionError
from worlds.AutoWorld import World
from .regions import create_regions
from .rules import FALLBACK_REASON, MID_ACT_1_POWER, select_logic, set_rules
from . import rules_old
from .web_world import SlayTheSpire2Web
from .characters import CharacterConfig, character_list, character_offset_map
from .constants import NUM_CUSTOM, ASCENSION_LIST, CHAR_OFFSET
from .items import item_table, chars_to_items, bonus_item_table, ItemType, base_event_item_pairs, ItemData, item_groups
from .locations import location_table, MAX_CARD_REWARDS, loc_ids_to_data, LocationData, LocationType, location_groups
from .options import Spire2Options

COMBAT_GOLD_ITEM_COUNT = 13
ELITE_GOLD_ITEM_COUNT = 7
BOSS_GOLD_ITEM_COUNT = 2


class SlayTheSpire2Item(Item):
    game = "Slay the Spire II"

    def __init__(self, item_data: ItemData, name: str, classification: ItemClassification, code: Optional[int], player: int):
        super().__init__(name, classification, code, player)
        self.item_data = item_data


class SlayTheSpire2Location(Location):
    game = "Slay the Spire II"


class SlayTheSpire2World(World):
    game = "Slay the Spire II"
    web = SlayTheSpire2Web()
    options_dataclass = Spire2Options
    options: Spire2Options
    compat_flag = 1
    origin_region_name = "Neow's Room"

    # Build the final Item Table
    item_name_to_id = {
        name: data.code for name, data in item_table.items()
    }
    item_name_groups = item_groups

    ut_can_gen_without_yaml = True

    location_name_to_id = location_table
    location_name_groups = location_groups

    filler_universal: dict[str, int]
    filler_characters: dict[str, dict[str, int]]
    filler_fallbacks: dict[str, str]

    def __init__(self, mw: MultiWorld, player: int):
        super().__init__(mw, player)
        self.characters: List[CharacterConfig] = []
        self.modded_num: int = 0
        self.modded_chars: List[CharacterConfig] = []
        self.total_shop_locations: int = 0
        self.total_shop_items: int = 0
        self.effective_logic = "new"
        self.logic_fallback_reason = None

    def generate_early(self) -> None:
        if hasattr(self.multiworld, 're_gen_passthrough'):
            self._setup_ut(self.multiworld.re_gen_passthrough[self.game])
            return
        if self.options.use_advanced_characters.value == 0:
            self._handle_basic_chars()
        else:
            self._handle_advanced_chars()

        if not self.characters:
            raise OptionError("At least one character must be configured")
        if self.options.include_floor_checks.value == 0:
            # Progressive starter items replace floor-check filler. Without those locations there
            # is no filler budget for them, so normalize both global toggles to disabled.
            self.options.progressive_starter_card.value = 0
            self.options.progressive_starter_relic.value = 0
        names = {config.official_name for config in self.characters}
        if len(names) != len(self.characters):
            raise OptionError(f"Found duplicate characters: {[c.official_name for c in self.characters]}")
        if not any(not config.locked for config in self.characters):
            raise OptionError("No character started unlocked!")
        if len(self.modded_chars) > NUM_CUSTOM:
            raise OptionError(
                f"StS 2 only supports {NUM_CUSTOM} modded characters; "
                f"got {len(self.modded_chars)}: {[c.option_name for c in self.modded_chars]}"
            )
        self.total_shop_items = (self.options.shop_card_slots.value + self.options.shop_neutral_card_slots.value +
                                 self.options.shop_relic_slots.value + self.options.shop_potion_slots.value)
        self.total_shop_locations = self.total_shop_items + (3 if self.options.shop_remove_slots else 0)
        if self.total_shop_locations <= 0:
            self.options.shop_sanity.value = 0
        select_logic(self)
        if self.logic_fallback_reason:
            logging.info("Slay the Spire II player %s: %s", self.player, self.logic_fallback_reason)
        num_chars_goal = self.options.num_chars_goal.value
        if num_chars_goal > len(self.characters):
            self.options.num_chars_goal.value = 0
        if not self.options.include_floor_checks:
            # Give sparse starts enough support to open Mid Act 1.
            # Mixing cards and relics meets the power/card floor within the available checks;
            # ordinary cards alone can fill every opening slot and still fall short.
            early_items = self.multiworld.early_items[self.player]
            cards = 1 if self.options.shuffle_all_cards else 0
            vanilla_cards = 0 if self.options.shuffle_all_cards else 1.5
            relics = ceil((MID_ACT_1_POWER - cards - vanilla_cards) / 1.5)
            if self.effective_logic == "old" and not self.options.shuffle_all_cards:
                # Old logic values a half-shuffled Card Reward at 2 power. Pair it with
                # one Relic for 3.5 power using the same two early slots as two Relics.
                cards, relics = 1, 1
            for config in self.characters:
                if config.locked:
                    continue
                if cards:
                    early_items[f"{config.name} Card Reward"] = cards
                early_items[f"{config.name} Relic"] = relics
                if self.options.campfire_sanity:
                    early_items[f"{config.name} Progressive Rest"] = 1
                if self.options.neow_sanity:
                    early_items[f"{config.name} Progressive Ancient"] = 1
        # for weight in self.options.trap_weights.values():
        #     if weight > 0:
        #         break
        # else:
        #     self.options.trap_chance.value = 0

    def _get_unlocked_char(self, characters: List[str]) -> Optional[str]:
        if len(characters) <= 0:
            raise OptionError("At least one character must be selected.")
        locked_opt = self.options.lock_characters.value
        unlocked_char = None
        if locked_opt == 1:
            unlocked_char = self.random.choice([x for x in characters])
        elif locked_opt == 2:
            unlocked_char_value = self.options.unlocked_character.value
            # Convert to string if it's an int index
            if isinstance(unlocked_char_value, int):
                unlocked_char_value = character_list[unlocked_char_value]

            # If unlocked_character is empty string (meaning "any"), randomly select from available characters
            if unlocked_char_value == "":
                unlocked_char = self.random.choice([x for x in characters])
            else:
                # Validate that the selected character is in the configured characters list
                for char in characters:
                    if char.lower() == unlocked_char_value.lower():
                        return char
                # We really shouldn't be able to get here anymore...but if we do, let us know, because it means this logic needs more work...
                raise OptionError(
                    f"Configured {unlocked_char_value} as the first unlocked character, but was not one of: {characters}")
        return unlocked_char

    def _select_character_names(self, char_options: List[str], unlocked_char: Optional[str]) -> List[str]:
        num_rand_chars = self.options.pick_num_characters.value
        if num_rand_chars == 0 or num_rand_chars >= len(char_options):
            return list(char_options)

        remaining = list(char_options)
        if unlocked_char is not None:
            remaining.remove(unlocked_char)
            selected_chars = [unlocked_char] + self.random.sample(remaining, k=num_rand_chars - 1)
        else:
            selected_chars = self.random.sample(remaining, k=num_rand_chars)

        modded_chars = [char for char in selected_chars if char.lower() not in character_offset_map]
        if len(modded_chars) <= NUM_CUSTOM:
            return selected_chars

        supported_chars = sorted(
            char for char in char_options
            if char.lower() in character_offset_map and char not in selected_chars
        )
        replace_num = len(modded_chars) - NUM_CUSTOM
        if unlocked_char is not None and unlocked_char in modded_chars:
            modded_chars.remove(unlocked_char)
        for char in self.random.sample(modded_chars, k=replace_num):
            selected_chars.remove(char)
        selected_chars.extend(self.random.sample(supported_chars, k=min(replace_num, len(supported_chars))))
        return selected_chars

    def _handle_basic_chars(self) -> None:
        selected_chars = list(self.options.characters.value)
        selected_chars.extend(self.options.modded_characters.value)
        char_options = sorted(selected_chars)
        ascension_down: typing.Set[str] = self.options.ascension_down.value
        ascension: typing.Set[str] = self.options.ascension.value
        ascension = self._to_ascensions(ascension)
        ascension_down = self._to_ascension_downs(ascension_down, ascension)
        if self.options.include_floor_checks.value == 0:
            ascension_down = set()
        ascension_down = ascension.intersection(ascension_down)

        seeds = {
            option_name: "".join(self.random.choice(string.ascii_letters) for _ in range(16))
            for option_name in char_options
        } if self.options.seeded else {}

        unlocked_char = self._get_unlocked_char(char_options)
        selected_chars = self._select_character_names(char_options, unlocked_char)
        configs = []
        modded_num = 0
        for option_name in selected_chars:
            mod_num = 0
            char_offset = character_offset_map.get(option_name.lower(), None)
            if char_offset is None:
                modded_num += 1
                mod_num = modded_num
                char_offset = mod_num + len(character_list)
                name = f"Custom Character {mod_num}"
            else:
                name = character_list[char_offset - 1]
            locked = unlocked_char is not None and unlocked_char.lower() != option_name.lower()
            configs.append(CharacterConfig(
                name,
                option_name,
                char_offset,
                mod_num,
                seeds.get(option_name, ""),
                locked,
                ascension=ascension,
                ascension_down=ascension_down,
            ))
        self.characters = configs
        self.modded_chars = [config for config in self.characters if config.mod_num > 0]
        self.modded_num = len(self.modded_chars)


    @staticmethod
    def _to_ascensions(ascensions: typing.Set[str]) -> typing.Set[str]:
        ret = set()
        if len(ascensions) == 1:
            try:
                number = int(next(iter(ascensions)))
                for i in range(0, number):
                    ret.add(ASCENSION_LIST[i].lower())
                return ret
            except (TypeError, ValueError, IndexError):
                return {asc.lower() for asc in ascensions}

        for asc in ascensions:
            try:
                number = int(asc)
                ret.add(ASCENSION_LIST[number - 1].lower())
            except (TypeError, ValueError, IndexError):
                ret.add(asc.lower())
        return ret

    @staticmethod
    def _to_ascension_downs(ascension_downs: typing.Set[str], ascensions: typing.Set[str]) -> typing.Set[str]:
        ret = set()
        if len(ascension_downs) == 1:
            try:
                number = int(next(iter(ascension_downs)))
                asc_list = ASCENSION_LIST[::-1]
                count = 0
                for i in range(0, len(asc_list)):
                    asc = asc_list[i].lower()
                    if asc in ascensions:
                        ret.add(asc_list[i].lower())
                        count += 1
                    if count >= len(ascensions) or count >= number:
                        break
                return ret
            except (TypeError, ValueError, IndexError):
                return {asc.lower() for asc in ascension_downs}

        for asc in ascension_downs:
            try:
                number = int(asc)
                ret.add(ASCENSION_LIST[number - 1].lower())
            except (TypeError, ValueError, IndexError):
                ret.add(asc.lower())
        return ret


    def _handle_advanced_chars(self) -> None:
        advanced_chars = self.options.advanced_characters.keys()
        char_options = sorted(advanced_chars)
        include_ascension_down = self.options.include_floor_checks.value != 0
        seeds = {
            option_name: "".join(self.random.choice(string.ascii_letters) for _ in range(16))
            for option_name in char_options
        } if self.options.seeded else {}

        unlocked_char = self._get_unlocked_char(char_options)
        selected_chars = self._select_character_names(char_options, unlocked_char)
        configs = []
        modded_num = 0
        for option_name in selected_chars:
            options = self.options.advanced_characters[option_name]
            mod_num = 0
            char_offset = character_offset_map.get(option_name.lower(), None)
            if char_offset is None:
                modded_num += 1
                mod_num = modded_num
                char_offset = mod_num + len(character_list)
                name = f"Custom Character {mod_num}"
            else:
                name = character_list[char_offset - 1]
            locked = unlocked_char is not None and unlocked_char.lower() != option_name.lower()
            ascension = self._to_ascensions(options['ascension'])
            ascension_down = self._to_ascension_downs(options['ascension_down'], ascension)
            if not include_ascension_down:
                ascension_down = set()
            ascension_down = ascension.intersection(ascension_down)
            configs.append(CharacterConfig(
                name,
                option_name,
                char_offset,
                mod_num,
                seeds.get(option_name, ""),
                locked,
                ascension=ascension,
                ascension_down=ascension_down,
            ))
        self.characters = configs
        self.modded_chars = [config for config in self.characters if config.mod_num > 0]
        self.modded_num = len(self.modded_chars)

    def create_regions(self) -> None:
        create_regions(self, self.player)
        # Region creation already follows character/act/stage order, including shop branches.
        self._ut_region_order = {region.name: index for index, region in enumerate(self.get_regions())}

    def custom_ut_sort(self, region_label: str, location_label: str) -> str:
        region_order = self._ut_region_order.get(region_label, len(self._ut_region_order))
        def pad_number(match: re.Match[str]) -> str:
            return match.group().zfill(10)

        # UT may append a modded-character alias; retain it while sorting check numbers naturally.
        location_key = re.sub(r"\d+", pad_number, location_label.casefold())
        return f"{region_order:04d}:{location_key}"

    def create_region(
            self,
            player: int,
            prefix: Optional[str],
            name: str,
            config: CharacterConfig,
            locations: list[str] | None = None,
            exits: list[str] | None = None,
    ):
        ret = Region(f"{prefix} {name}" if prefix is not None else name, player, self.multiworld)
        locs: dict[str, Optional[int]] = dict()
        for location in locations or ():
            loc_name = f"{prefix} {location}" if prefix is not None else location
            loc_id = location_table.get(loc_name, 0)
            loc_data = loc_ids_to_data.get(loc_id, None)
            if self._should_include_location(loc_data, config):
                locs[loc_name] = loc_id
        if locations:
            ret.add_locations(locs, SlayTheSpire2Location)
        for destination in exits or ():
            exit_name = f"{prefix} {destination}" if prefix is not None else destination
            ret.create_exit(exit_name)
        return ret

    # Creates individual items based on the item table
    def create_item(self, name: str) -> SlayTheSpire2Item:
        data = item_table[name]
        item_id = data.code
        classification = data.classification
        if (self.effective_logic == 'new' and not self.options.shop_sanity
                and name.endswith((' Elite Gold', ' Boss Gold'))):
            # Only shops require gold in new logic; legacy boss gates still need it.
            classification = ItemClassification.useful
        return SlayTheSpire2Item(data, name, classification, item_id, self.player)

    def build_filler_pools(self) -> None:
        """Cache positive per-item weights and character-specific gold fallbacks.

        Also called lazily when AP requests replacement filler before create_items.
        """
        trap_weights = {
            "Weak Trap": self.options.weak_trap_weight.value,
            "Frail Trap": self.options.frail_trap_weight.value,
            "Vulnerable Trap": self.options.vulnerable_trap_weight.value,
            "No Draw Trap": self.options.no_draw_trap_weight.value,
            "Tangled Trap": self.options.tangled_trap_weight.value,
            "Vakuu Trap": self.options.vakuu_trap_weight.value,
            "Confused Trap": self.options.confused_trap_weight.value,
            "Sloth Trap": self.options.sloth_trap_weight.value,
            "Dazed Trap": self.options.dazed_trap_weight.value,
        }
        self.trap_weights = {name: weight for name, weight in trap_weights.items() if weight > 0}
        if self.options.trap_chance.value > 0 and not self.trap_weights:
            raise ValueError("Trap Chance is greater than 0, but all trap weights are None. "
                             "Enable a trap weight or set Trap Chance to 0.")

        # --- Universal (buff) item pools ---
        # Map each universal item name to its configured option weight value.
        # These items are character-agnostic: they can apply to any character's run.
        universal_item_option_map = {
            "Free Attack": self.options.free_attack_filler_weight.value,
            "Free Power": self.options.free_power_filler_weight.value,
            "Free Skill": self.options.free_skill_filler_weight.value,
            "Dexterity": self.options.dexterity_filler_weight.value,
            "Strength": self.options.strength_filler_weight.value,
            "Plating": self.options.plating_filler_weight.value,
            "Friendship": self.options.friendship_filler_weight.value,
            "Post-Combat Card Upgrade": self.options.post_combat_card_upgrade_filler_weight.value,
            "Post-Combat Card Removal": self.options.post_combat_card_removal_filler_weight.value,
            "Additional Card Reward": self.options.additional_card_reward_filler_weight.value,
            "Buffer": self.options.buffer_filler_weight.value,
            "Vigor": self.options.vigor_filler_weight.value,
            "Thorns": self.options.thorns_filler_weight.value,
            "Artifact": self.options.artifact_filler_weight.value,
        }

        self.filler_universal = {
            name: weight for name, weight in universal_item_option_map.items() if weight > 0
        }
        self.filler_characters = {}
        self.filler_fallbacks = {}
        for config in self.characters:
            char_lookup = config.name if config.mod_num == 0 else config.mod_num
            gold_weights = {}
            for name, data in chars_to_items[char_lookup].items():
                if data.type != ItemType.GOLD or data.classification != ItemClassification.filler:
                    continue
                if "One Gold" in name:
                    self.filler_fallbacks[config.name] = name
                    weight = self.options.one_gold_filler_weight.value
                elif "Five Gold" in name:
                    weight = self.options.five_gold_filler_weight.value
                else:
                    continue
                if weight > 0:
                    gold_weights[name] = weight
            self.filler_characters[config.name] = gold_weights

    def get_filler_item(self, character: Optional[str] = None) -> str:
        """Roll for a trap, then draw by item weight within the selected category.

        Ordinary filler includes only the selected character's gold.

        A high item has five times the probability of a low item regardless of how
        many other items share either weight. Zero-weight items are excluded.
        """
        if not hasattr(self, 'filler_universal'):
            self.build_filler_pools()
        if self.options.trap_chance.value > 0 and self.random.randrange(100) < self.options.trap_chance.value:
            return self.random.choices(list(self.trap_weights), weights=list(self.trap_weights.values()), k=1)[0]
        if character is None and self.characters:
            character = self.random.choice(self.characters).name

        weights = self.filler_universal | self.filler_characters.get(character, {})
        if not weights:
            return self.filler_fallbacks.get(character, "Ironclad One Gold")
        return self.random.choices(list(weights), weights=list(weights.values()), k=1)[0]

    def get_filler_item_name(self) -> str:
        """AP framework entry point for fill and item-link replacements."""
        return self.get_filler_item()

    def create_items(self) -> None:
        # Pre-compute filler item pools once here so `get_filler_item()` is just a simple lookup
        self.build_filler_pools()

        pool = []
        filler_counts: list[tuple[CharacterConfig, int]] = []
        card_reward_count = MAX_CARD_REWARDS if self.options.shuffle_all_cards.value else MAX_CARD_REWARDS // 2
        for config in self.characters:
            char_lookup = config.name if config.mod_num == 0 else config.mod_num
            # ascension_downs = min(config.ascension_down, config.ascension)
            for name, data in chars_to_items[char_lookup].items():
                amount = 0
                if ItemType.CARD_REWARD == data.type:
                    amount = card_reward_count
                # elif ItemType.RARE_CARD_REWARD == data.type or ItemType.BOSS_RELIC == data.type:
                elif ItemType.RARE_CARD_REWARD == data.type:
                    amount = 2
                elif ItemType.PROGRESSIVE_ANCIENT == data.type:
                    amount = 2 if self.options.neow_sanity.value == 0 else 3
                elif ItemType.RELIC == data.type:
                    amount = 10
                elif ItemType.PROGRESSIVE_STARTER_CARD == data.type:
                    amount = 2 if self.options.progressive_starter_card.value else 0
                elif ItemType.PROGRESSIVE_STARTER_RELIC == data.type:
                    amount = 2 if self.options.progressive_starter_relic.value else 0
                elif ItemType.CAMPFIRE == data.type:
                    if self.options.campfire_sanity.value != 0:
                        amount = 3
                elif ItemType.CHAR_UNLOCK == data.type:
                    if self.options.lock_characters.value != 0 and config.locked:
                        amount = 1
                    else:
                        self.push_precollected(self.create_item(name))
                elif ItemType.GOLD == data.type:
                    if self.options.gold_sanity.value != 0:
                        if 'Combat Gold' in name:
                            amount = COMBAT_GOLD_ITEM_COUNT
                        elif 'Elite Gold' in name:
                            amount = ELITE_GOLD_ITEM_COUNT
                        elif 'Boss Gold' in name:
                            amount = BOSS_GOLD_ITEM_COUNT
                elif ItemType.POTION == data.type:
                    if self.options.potion_sanity.value != 0:
                        amount = 9
                elif ItemType.ASCENSION_DOWN == data.type:
                    if self.options.include_floor_checks.value != 0:
                        # dumb math cause I've made this hard
                        base_item_code = data.code - (CHAR_OFFSET*config.char_offset)
                        amount = 1 if ASCENSION_LIST[base_item_code - 19].lower() in config.ascension_down else 0
                elif self.options.shop_sanity.value != 0:
                    if ItemType.SHOP_CARD == data.type:
                        amount = self.options.shop_card_slots.value
                    elif ItemType.SHOP_NEUTRAL == data.type:
                        amount = self.options.shop_neutral_card_slots.value
                    elif ItemType.SHOP_RELIC == data.type:
                        amount = self.options.shop_relic_slots.value
                    elif ItemType.SHOP_POTION == data.type:
                        amount = self.options.shop_potion_slots.value
                    elif ItemType.SHOP_REMOVE == data.type and self.options.shop_remove_slots.value != 0:
                        amount = 3
                pool.extend(
                    self.create_item(name)
                    for _ in range(amount)
                )

            if self.options.include_floor_checks.value:

                # remaining_checks = 51 - ascension_downs
                remaining_checks = 48 - len(config.ascension_down)
                if 'DoubleBoss'.lower() in config.ascension and 'DoubleBoss'.lower() not in config.ascension_down:
                    remaining_checks += 1

                # Generate filler items for floor checks using the weighted filler system
                progressive_starter_items = (
                    (2 if self.options.progressive_starter_card.value else 0) +
                    (2 if self.options.progressive_starter_relic.value else 0)
                )
                filler_num = remaining_checks - progressive_starter_items
                filler_counts.append((config, filler_num))
            # Pair up our event locations with our event items
            for base_event, base_item in base_event_item_pairs.items():
                event = f"{config.name} {base_event}"
                item = f"{config.name} {base_item}"
                item_data = item_table[item]
                event_item = SlayTheSpire2Item(item_data, item, item_data.classification, item_data.code, self.player)
                self.multiworld.get_location(event, self.player).place_locked_item(event_item)

        bonus_item_names = []
        for bonus_entry in self.options.bonus_items.value:
            bonus_type = next(iter(bonus_entry))
            bonus_data = bonus_item_table.get(bonus_type)
            if bonus_data is None:
                raise OptionError(
                    f"Unknown Bonus Items type '{bonus_type}'. "
                    f"Supported types: {', '.join(sorted(bonus_item_table))}"
                )
            bonus_item_names.append(bonus_data.item_name)

        available_filler_slots = sum(count for _, count in filler_counts)
        if len(bonus_item_names) > available_filler_slots:
            raise OptionError(
                f"Configured {len(bonus_item_names)} Bonus Items, but only "
                f"{available_filler_slots} filler slots are available."
            )

        pool.extend(self.create_item(name) for name in bonus_item_names)
        bonus_slots_remaining = len(bonus_item_names)
        for config, filler_count in filler_counts:
            consumed_slots = min(bonus_slots_remaining, filler_count)
            bonus_slots_remaining -= consumed_slots
            for _ in range(filler_count - consumed_slots):
                pool.append(self.create_item(self.get_filler_item(character=config.name)))

        self.multiworld.itempool += pool

    def _should_include_location(self, data: Optional[LocationData], config: CharacterConfig) -> bool:
        if data is None:
            return True
        if data.type == LocationType.Floor and self.options.include_floor_checks == 0:
            return False
        elif data.type == LocationType.Campfire and self.options.campfire_sanity == 0:
            return False
        elif data.type == LocationType.Ancient:
            if self.options.neow_sanity == 0 and "Ancient Act 1" in data.name:
                return False
            return True
        elif data.type == LocationType.Shop:
            if self.options.shop_sanity.value == 0:
                return False
            total_shop = self.total_shop_locations
            return total_shop >= data.id - 36
        elif data.type == LocationType.Start and (self.options.lock_characters.value == 0 or not config.locked):
            return False
        elif data.type == LocationType.Gold and self.options.gold_sanity.value == 0:
            return False
        elif data.type == LocationType.Potion and self.options.potion_sanity.value == 0:
            return False
        return True

    def collect(self, state: CollectionState, item: Item) -> bool:
        if self.effective_logic == "old":
            return rules_old.collect(self, state, item)
        return super().collect(state, item)

    def remove(self, state: CollectionState, item: Item) -> bool:
        if self.effective_logic == "old":
            return rules_old.remove(self, state, item)
        return super().remove(state, item)

    def write_spoiler_header(self, spoiler_handle) -> None:
        spoiler_handle.write(f"Effective logic: {self.effective_logic}\n")
        if self.logic_fallback_reason:
            spoiler_handle.write(f"Logic fallback: {self.logic_fallback_reason}\n")

    def set_rules(self) -> None:
        set_rules(self)

    def fill_slot_data(self) -> dict:
        slot_data = {
            "effective_logic": self.effective_logic,
            'characters': [
                c.to_dict() for c in self.characters
            ],
            'shop_sanity_options': {
                "card_slots": self.options.shop_card_slots.value,
                "neutral_slots": self.options.shop_neutral_card_slots.value,
                "relic_slots": self.options.shop_relic_slots.value,
                "potion_slots": self.options.shop_potion_slots.value,
                "card_remove": self.options.shop_remove_slots != 0,
                "costs": self.options.shop_sanity_costs.value,
            },
            # Keep the wire key for clients; Archipelago loads world_version from archipelago.json.
            "mod_compat_version": self.world_version.as_simple_string(),
            "CompatFlag": self.compat_flag,
        }
        slot_data.update(self.options.as_dict(
            "use_new_logic",
            "lock_characters",
            "seeded",
            "ascension",
            "num_chars_goal",
            "shuffle_all_cards",
            "include_floor_checks",
            "neow_sanity",
            "ancient_relic_location",
            "ancient_relic_pool",
            "relic_rewards_available_anytime",
            "release_on_victory",
            "shop_sanity",
            "potion_sanity",
            "gold_sanity",
            "campfire_sanity",
            "progressive_starter_card",
            "progressive_starter_relic",
            "bonus_items",
            "death_link",
            "enable_death_fragments",
            "death_link_damage_percent",
        ))
        return slot_data

    @staticmethod
    def interpret_slot_data(slot_data: dict[str, Any]) -> Any:
        return slot_data

    def _setup_ut(self, slot_data: dict[str, Any]) -> None:
        self.options.use_new_logic.value = self.options.use_new_logic.from_any(slot_data.get("use_new_logic", True)).value
        # Reconstruct the generated mode; do not rerun fallback selection on tracker input.
        self.effective_logic = slot_data.get("effective_logic", "new")
        self.logic_fallback_reason = (
            FALLBACK_REASON if self.options.use_new_logic and self.effective_logic == "old" else None
        )
        self.options.lock_characters.value = slot_data['lock_characters']
        self.options.shop_card_slots.value = slot_data["shop_sanity_options"]["card_slots"]
        self.options.shop_remove_slots.value = slot_data["shop_sanity_options"]["card_remove"]
        self.options.shop_neutral_card_slots.value = slot_data["shop_sanity_options"]["neutral_slots"]
        self.options.shop_relic_slots.value = slot_data["shop_sanity_options"]["relic_slots"]
        self.options.shop_potion_slots.value = slot_data["shop_sanity_options"]["potion_slots"]
        self.options.shop_sanity_costs.value = slot_data["shop_sanity_options"]["costs"]
        for char_dict in slot_data['characters']:
            config = CharacterConfig(
                char_dict['name'],
                char_dict['option_name'],
                char_dict['char_offset'],
                char_dict['mod_num'],
                char_dict['seed'],
                char_dict['locked'],
                ascension=char_dict['ascension'],
                ascension_down=char_dict['ascension_down'],
            )
            self.characters.append(config)
            if char_dict['mod_num'] > 0:
                self.modded_chars.append(config)
        self.total_shop_items = (self.options.shop_card_slots.value + self.options.shop_neutral_card_slots.value +
                                 self.options.shop_relic_slots.value + self.options.shop_potion_slots.value)
        self.total_shop_locations = self.total_shop_items + (3 if self.options.shop_remove_slots else 0)
        if self.total_shop_locations <= 0:
            self.options.shop_sanity.value = 0
        self.options.shuffle_all_cards.value = slot_data['shuffle_all_cards']
        self.options.include_floor_checks.value = slot_data['include_floor_checks']
        self.options.neow_sanity.value = slot_data['neow_sanity']
        self.options.ancient_relic_location.value = slot_data['ancient_relic_location']
        self.options.ancient_relic_pool.value = slot_data['ancient_relic_pool']
        self.options.relic_rewards_available_anytime.value = slot_data['relic_rewards_available_anytime']
        self.options.release_on_victory.value = slot_data['release_on_victory']
        self.options.campfire_sanity.value = slot_data['campfire_sanity']
        self.options.progressive_starter_card.value = slot_data['progressive_starter_card']
        self.options.progressive_starter_relic.value = slot_data['progressive_starter_relic']
        self.options.bonus_items.value = deepcopy(slot_data.get('bonus_items', []))
        if self.options.include_floor_checks.value == 0:
            self.options.progressive_starter_card.value = 0
            self.options.progressive_starter_relic.value = 0
        self.options.shop_sanity.value = slot_data['shop_sanity']
        self.options.gold_sanity.value = slot_data['gold_sanity']
        self.options.potion_sanity.value = slot_data['potion_sanity']
        self.options.num_chars_goal.value = slot_data['num_chars_goal']
        if self.effective_logic == 'old':
            rules_old.restore_tracker_scoring(self)
        self.location_id_to_alias: dict[int, str] = dict()
        pattern = re.compile("Custom Character [0-9]+ (?P<location_name>.*?)$")
        for key, value in SlayTheSpire2World.location_id_to_name.items():
            if key < (len(character_list)) * CHAR_OFFSET:
                continue
            modded_index = (key // CHAR_OFFSET) - len(character_list)
            if modded_index >= len(self.modded_chars):
                continue
            match = pattern.match(value)
            if match is None:
                raise Exception("Failed to match " + value)
            name = self.modded_chars[modded_index].official_name
            self.location_id_to_alias[key] = name + " " + match.group("location_name")
