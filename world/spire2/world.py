import string
import typing
from collections import defaultdict
from typing import List, Optional, Any

from BaseClasses import Item, Location, Region, MultiWorld, ItemClassification, CollectionState
from Options import OptionError
from worlds.AutoWorld import World
from .regions import create_regions
from .rules import set_rules, SpireLogic
from .web_world import SlayTheSpire2Web
from .characters import CharacterConfig, character_list, character_offset_map
from .constants import NUM_CUSTOM
from .items import item_table, chars_to_items, ItemType, base_event_item_pairs, ItemData
from .locations import location_table, MAX_CARD_REWARDS, loc_ids_to_data, LocationData, LocationType
from .options import Spire2Options


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
    mod_compat_version = 1
    origin_region_name = "Neow's Room"

    # Build the final Item Table
    item_name_to_id = {
        name: data.code for name, data in item_table.items()
    }

    ut_can_gen_without_yaml = True

    location_name_to_id = location_table

    def __init__(self, mw: MultiWorld, player: int):
        super().__init__(mw, player)
        self.characters: List[CharacterConfig] = []
        self.modded_num: int = 0
        self.modded_chars: List[CharacterConfig] = []
        self.total_shop_locations: int = 0
        self.total_shop_items: int = 0

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
        names = set()
        for config in self.characters:
            # self.logger.info("StS: Got character configuration" + str(config))
            names.add(config.official_name)
        if len(names) != len(self.characters):
            raise OptionError(f"Found duplicate characters: {[x.official_name for x in self.characters]}")
        for config in self.characters:
            if not config.locked:
                break
        else:
            raise OptionError("No character started unlocked!")
        # self.total_shop_items = (self.options.shop_card_slots.value + self.options.shop_neutral_card_slots.value +
        #                          self.options.shop_relic_slots.value + self.options.shop_potion_slots.value)
        # self.total_shop_locations = self.total_shop_items + (3 if self.options.shop_remove_slots else 0)
        # if self.total_shop_locations <= 0:
        #     self.options.shop_sanity.value = 0
        if len(self.modded_chars) > NUM_CUSTOM:
            raise OptionError(f"StS 2 only supports {NUM_CUSTOM} modded characters; got {len(self.modded_chars)}: {[x.option_name for x in self.modded_chars]}")
        num_chars_goal = self.options.num_chars_goal.value
        if num_chars_goal != 0:
            if num_chars_goal > len(self.characters):
                self.options.num_chars_goal.value = 0
        for char in self.characters:
            if not char.locked:
                self.options.start_inventory.value[f"{char.name} Unlock"] = 1
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
            unlocked_char = self.options.unlocked_character.value
            if type(unlocked_char) == int:
                unlocked_char = character_list[unlocked_char]

            for char in characters:
                if char.lower() == unlocked_char.lower():
                    return char
            else:
                raise OptionError(
                    f"Configured {unlocked_char} as the first unlocked character, but was not one of: {characters}")
        return unlocked_char

    def _handle_basic_chars(self) -> None:
        selected_chars = sorted(self.options.characters.value)
        num_rand_chars = self.options.pick_num_characters.value
        unlocked_char = self._get_unlocked_char(selected_chars)
        if num_rand_chars != 0 and num_rand_chars < len(selected_chars):
            if self.options.lock_characters.value != 0:
                selected_chars.remove(unlocked_char)
                selected_chars = [unlocked_char] + self.random.sample(selected_chars, k=num_rand_chars - 1)
            else:
                selected_chars = self.random.sample(selected_chars, k=num_rand_chars)

        # self.logger.info("Generating with characters %s", selected_chars)
        # ascension_down = self.options.ascension_down.value
        # if self.options.include_floor_checks.value == 0:
        #     ascension_down = 0
        for char_val in selected_chars:
            option_name = char_val
            char_offset = character_offset_map[option_name.lower()]
            name = character_list[char_offset - 1]
            if self.options.seeded:
                seed = "".join(self.random.choice(string.ascii_letters) for i in range(16))
            else:
                seed = ""
            locked = False if unlocked_char is None or unlocked_char.lower() == option_name.lower() else True
            config = CharacterConfig(name,
                                     option_name,
                                     char_offset,
                                     0,
                                     seed,
                                     locked,
                                     ascension=self.options.ascension.value)
            self.characters.append(config)

    def _handle_advanced_chars(self) -> None:
        advanced_chars = self.options.advanced_characters.keys()
        char_options = sorted(advanced_chars)
        num_rand_chars = self.options.pick_num_characters.value
        unlocked_char = self._get_unlocked_char(char_options)
        # include_ascension_down = self.options.include_floor_checks.value != 0
        if num_rand_chars != 0 and num_rand_chars < len(char_options):
            selected_chars = list(char_options)
            if self.options.lock_characters.value != 0:
                if unlocked_char in selected_chars:
                    selected_chars.remove(unlocked_char)
                selected_chars = [unlocked_char] + self.random.sample(selected_chars, k=num_rand_chars - 1)
            else:
                selected_chars = self.random.sample(selected_chars, k=num_rand_chars)
            modded_num = 0
            for char in selected_chars:
                if character_offset_map.get(char.lower(), None) is None:
                    modded_num += 1
            if modded_num > NUM_CUSTOM:
                supported_chars = sorted({x for x in char_options if x.lower() in character_offset_map})
                replace_num = modded_num - NUM_CUSTOM
                remove_me = self.random.sample(selected_chars, k=replace_num)
                for remove in remove_me:
                    selected_chars.remove(remove)
                selected_chars += self.random.sample(supported_chars, k=min(replace_num, len(supported_chars)))
        else:
            selected_chars = char_options

        # self.logger.info("Generating with characters %s", selected_chars)
        for option_name in selected_chars:
            options = self.options.advanced_characters[option_name]
            mod_num = 0
            char_offset = character_offset_map.get(option_name.lower(), None)
            if char_offset is None:
                self.modded_num += 1
                mod_num = self.modded_num
                char_offset = mod_num + len(character_list) - 1
                name = f"Custom Character {mod_num}"
            else:
                name = character_list[char_offset]
            if self.options.seeded:
                seed = "".join(self.random.choice(string.ascii_letters) for i in range(16))
            else:
                seed = ""
            locked = False if unlocked_char is None or unlocked_char.lower() == option_name.lower() else True
            config = CharacterConfig(name,
                                     option_name,
                                     char_offset,
                                     mod_num,
                                     seed,
                                     locked,
                                     **options)
            # if not include_ascension_down:
            #     config.ascension_down = 0
            self.characters.append(config)
            if config.mod_num > 0:
                self.modded_chars.append(config)

    def create_regions(self) -> None:
        # menu = Region("Menu", self.player, self.multiworld)
        # act_1 = Region("Act 1", self.player, self.multiworld)
        #
        # self.multiworld.regions += [menu, act_1]
        #
        # act_1.add_locations(self.location_name_to_id, SlayTheSpire2Location)
        # menu.connect(act_1)
        create_regions(self, self.player)

    def create_region(self, player: int, prefix: Optional[str], name: str, config: CharacterConfig, locations: List[str] = None, exits: List[str] =None):
        ret = Region(f"{prefix} {name}" if prefix is not None else name, player, self.multiworld)
        if locations:
            locs: dict[str, Optional[int]] = dict()
            for location in locations:
                loc_name = f"{prefix} {location}" if prefix is not None else location
                loc_id = location_table.get(loc_name, 0)
                loc_data = loc_ids_to_data.get(loc_id, None)
                if self._should_include_location(loc_data, config):
                    locs[loc_name] = loc_id
            ret.add_locations(locs, SlayTheSpire2Location)
        if exits:
            for exit in exits:
                exit_name = f"{prefix} {exit}" if prefix is not None else exit
                ret.create_exit(exit_name)
        return ret

    # Creates individual items based on the item table
    def create_item(self, name: str) -> SlayTheSpire2Item:
        data = item_table[name]
        item_id = self.item_name_to_id[name]
        return SlayTheSpire2Item(data, name, data.classification, item_id, self.player)

    # Randomly selects a filler item name from the item table
    def get_filler_item_name(self) -> str:
        return 'CAW CAW'

    # Add items to the multiworld
    def create_items(self) -> None:
        # TODO: implement
        # items_to_add = ["Victory"]
        #
        # location_count = len(self.location_name_to_id)
        # filler_count = location_count - len(items_to_add)
        # for _ in range(filler_count):
        #     items_to_add.append(self.get_filler_item_name())
        #
        # for name in items_to_add:
        #     self.multiworld.itempool.append(self.create_item(name))
        pool = []
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
                elif ItemType.RELIC == data.type:
                    amount = 10
                # elif ItemType.CAMPFIRE == data.type:
                #     if self.options.campfire_sanity.value != 0:
                #         amount = 3
                elif ItemType.CHAR_UNLOCK == data.type:
                    if self.options.lock_characters.value != 0 and config.locked:
                        amount = 1
                    else:
                        self.push_precollected(self.create_item(name))
                elif ItemType.GOLD == data.type:
                    if self.options.gold_sanity.value != 0:
                        if '15 Gold' in name:
                            amount = 13
                        elif '30 Gold' in name:
                            amount = 7
                        elif 'Boss Gold' in name:
                            amount = 2
                elif ItemType.POTION == data.type:
                    if self.options.potion_sanity.value != 0:
                        amount = 9
                # elif ItemType.ASCENSION_DOWN == data.type:
                #     if self.options.include_floor_checks.value != 0:
                #         amount = ascension_downs
                # elif self.options.shop_sanity.value != 0:
                #     if ItemType.SHOP_CARD == data.type:
                #         amount = self.options.shop_card_slots.value
                #     elif ItemType.SHOP_NEUTRAL == data.type:
                #         amount = self.options.shop_neutral_card_slots.value
                #     elif ItemType.SHOP_RELIC == data.type:
                #         amount = self.options.shop_relic_slots.value
                #     elif ItemType.SHOP_POTION == data.type:
                #         amount = self.options.shop_potion_slots.value
                #     elif ItemType.SHOP_REMOVE == data.type and self.options.shop_remove_slots.value != 0:
                #         amount = 3
                for _ in range(amount):
                    pool.append(self.create_item(name))

            if self.options.include_floor_checks.value:

                # remaining_checks = 51 - ascension_downs
                remaining_checks = 48
                if config.ascension >= 10:
                    # TODO: handle ascension downs
                    remaining_checks += 1

                # traps: list[bool] = [self.random.randint(0, 100) < self.options.trap_chance for _ in
                #                      range(remaining_checks)]
                # trap_num = traps.count(True)
                # filler_num = len(traps) - trap_num
                filler_num = remaining_checks
                # if trap_num > 0:
                #     for name in self.random.choices(list(self.options.trap_weights.keys()),
                #                                     weights=list(self.options.trap_weights.values()), k=trap_num):
                #         pool.append(SpireItem(name, self.player))

                # Char specific 1 Gold and 5 Gold, in that order
                filler_pool = [key for key, val in chars_to_items[char_lookup].items()
                               if ItemType.GOLD == val.type and ItemClassification.filler == val.classification]
                filler_pool.append("CAW CAW")
                # filler_pool.append("Combat Buff")
                filler_weights = [
                    self.options.filler_weights.get("1 Gold", 0),
                    self.options.filler_weights.get("5 Gold", 0),
                    self.options.filler_weights.get("CAW CAW", 0),
                    # self.options.filler_weights.get("Combat Buff", 0),
                ]
                if sum(filler_weights) <= 0:
                    filler_weights = [40, 60, 0]
                    # filler_weights = [40, 60, 0, 0]

                for name in self.random.choices(filler_pool, weights=filler_weights, k=filler_num):
                    pool.append(self.create_item(name))
            # Pair up our event locations with our event items
            for base_event, base_item in base_event_item_pairs.items():
                event = f"{config.name} {base_event}"
                item = f"{config.name} {base_item}"
                item_data = item_table[item]
                event_item = SlayTheSpire2Item(item_data, item, item_data.classification, item_data.code, self.player)
                self.multiworld.get_location(event, self.player).place_locked_item(event_item)

        self.multiworld.itempool += pool

    def _should_include_location(self, data: LocationData, config: CharacterConfig) -> bool:
        if data is None:
            return True
        if data.type == LocationType.Floor and self.options.include_floor_checks == 0:
            return False
        # elif data.type == LocationType.Campfire and self.options.campfire_sanity == 0:
        #     return False
        elif data.type == LocationType.Shop:
            return False
        #     if self.options.shop_sanity.value == 0:
        #         return False
        #     total_shop = self.total_shop_locations
        #     return total_shop >= data.id - 163
        elif data.type == LocationType.Start and (self.options.lock_characters.value == 0 or not config.locked):
            return False
        elif data.type == LocationType.Gold and self.options.gold_sanity.value == 0:
            return False
        elif data.type == LocationType.Potion and self.options.potion_sanity.value == 0:
            return False
        return True

    def set_rules(self) -> None:
        set_rules(self)

    def collect(self, state: CollectionState, item: Item) -> bool:
        change = super().collect(state, item)
        item_data = typing.cast(SlayTheSpire2Item, item).item_data
        if change and item_data.type in state.item_levels[self.player]:
            level = state.item_levels[self.player].get(item_data.type, 0.0)
            # char_level = state.power_level.setdefault(item.player, defaultdict(int))
            char_level = state.power_level[item.player]
            char_level[item_data.char_offset] = char_level[item_data.char_offset] + level
        return change

    def remove(self, state: CollectionState, item: Item) -> bool:
        change = super().remove(state, item)
        item_data = typing.cast(SlayTheSpire2Item, item).item_data
        if change and item_data.type in state.item_levels[self.player]:
            level = state.item_levels[self.player].get(item_data.type, 0.0)
            # state.power_level[item.player][item_data.char_offset] -= level
            # state.power_level.get(item.player, defaultdict(int))[item_data.char_offset] -= level
            char_level = state.power_level[item.player]
            # char_level = state.power_level[item.player]
            char_level[item_data.char_offset] = char_level[item_data.char_offset] - level
        return change

    def fill_slot_data(self) -> dict:
        slot_data = {
            'characters': [
                c.to_dict() for c in self.characters
            ],
            # 'shop_sanity_options': {
            #     "card_slots": self.options.shop_card_slots.value,
            #     "neutral_slots": self.options.shop_neutral_card_slots.value,
            #     "relic_slots": self.options.shop_relic_slots.value,
            #     "potion_slots": self.options.shop_potion_slots.value,
            #     "card_remove": self.options.shop_remove_slots != 0,
            #     "costs": self.options.shop_sanity_costs.value,
            # },
            "mod_compat_version": self.mod_compat_version,
        }
        slot_data.update(self.options.as_dict(
            "ascension",
            "num_chars_goal",
            "shuffle_all_cards",
            "include_floor_checks",
            "potion_sanity",
            "gold_sanity",
        ))
        return slot_data

    @staticmethod
    def interpret_slot_data(slot_data: dict[str, Any]) -> Any:
        return slot_data

    def _setup_ut(self, slot_data: dict[str, Any]) -> None:
        # self.options.shop_card_slots.value = slot_data["shop_sanity_options"]["card_slots"]
        # self.options.shop_remove_slots.value = slot_data["shop_sanity_options"]["card_remove"]
        # self.options.shop_neutral_card_slots.value = slot_data["shop_sanity_options"]["neutral_slots"]
        # self.options.shop_relic_slots.value = slot_data["shop_sanity_options"]["relic_slots"]
        # self.options.shop_potion_slots.value = slot_data["shop_sanity_options"]["potion_slots"]
        for char_dict in slot_data['characters']:
            config = CharacterConfig(
                char_dict['name'],
                char_dict['option_name'],
                char_dict['char_offset'],
                char_dict['mod_num'],
                char_dict['seed'],
                char_dict['locked'],
                ascension=char_dict['ascension'],
            )
            self.characters.append(config)
            if char_dict['mod_num'] > 0:
                self.modded_chars.append(config)
        # self.total_shop_items = (self.options.shop_card_slots.value + self.options.shop_neutral_card_slots.value +
        #                          self.options.shop_relic_slots.value + self.options.shop_potion_slots.value)
        # self.total_shop_locations = self.total_shop_items + (3 if self.options.shop_remove_slots else 0)
        # if self.total_shop_locations <= 0:
        #     self.options.shop_sanity.value = 0
        self.options.include_floor_checks.value = slot_data['include_floor_checks']
        # self.options.campfire_sanity.value = slot_data['campfire_sanity']
        # self.options.shop_sanity.value = slot_data['shop_sanity']
        self.options.gold_sanity.value = slot_data['gold_sanity']
        self.options.potion_sanity.value = slot_data['potion_sanity']
        self.options.num_chars_goal.value = slot_data['num_chars_goal']
        self.location_id_to_alias: dict[int, str] = dict()
        # pattern = re.compile("Custom Character [0-9]+ (?P<location_name>.*?)$")
        # for i in range(1, len(self.modded_chars) + 1):
        # for key, value in SpireWorld.location_id_to_name.items():
        #     if key < (len(character_list)) * CHAR_OFFSET:
        #         continue
        #     modded_index = (key // CHAR_OFFSET) - len(character_list)
        #     self.logger.info(f"Modded index: {modded_index}")
        #     self.logger.info(f"modded_chars index: {self.modded_chars}")
        #     if modded_index >= len(self.modded_chars):
        #         continue
        #     match = pattern.match(value)
        #     if match is None:
        #         raise Exception("Failed to match " + value)
        #     name = self.modded_chars[modded_index].official_name
        #     self.logger.info(name)
        #     self.location_id_to_alias[key] = name + " " + match.group("location_name")
