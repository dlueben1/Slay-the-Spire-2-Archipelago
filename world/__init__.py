from BaseClasses import Item, Location, Region, Tutorial
from worlds.AutoWorld import WebWorld, World
from worlds.generic.Rules import set_rule

from .items import item_table, START_ID as ITEM_START_ID
from .locations import ACT_1_FLOOR_COUNT, location_table

# The info needed for the Archipelago Website, not the actual `apworld`
class SlayTheSpire2Web(WebWorld):
    tutorials = [
        Tutorial(
            "Setup Guide",
            "A very small test world for Slay the Spire II.",
            "English",
            "setup_en.md",
            "setup/en",
            ["Kirbyfanner"]
        )
    ]


class SlayTheSpire2Item(Item):
    game = "Slay the Spire II"


class SlayTheSpire2Location(Location):
    game = "Slay the Spire II"


class SlayTheSpire2World(World):
    game = "Slay the Spire II"
    web = SlayTheSpire2Web()

    # Build the final Item Table
    item_name_to_id = {
        name: ITEM_START_ID + i
        for i, name in enumerate(item_table.keys(), start=1)
    }

    location_name_to_id = location_table

    def create_regions(self) -> None:
      menu = Region("Menu", self.player, self.multiworld)
      act_1 = Region("Act 1", self.player, self.multiworld)

      self.multiworld.regions += [menu, act_1]

      act_1.add_locations(self.location_name_to_id, SlayTheSpire2Location)
      menu.connect(act_1)

    # Creates individual items based on the item table
    def create_item(self, name: str) -> SlayTheSpire2Item:
        data = item_table[name]
        item_id = self.item_name_to_id[name]
        return SlayTheSpire2Item(name, data.classification, item_id, self.player)

    # Randomly selects a filler item name from the item table
    def get_filler_item_name(self) -> str:
        return self.multiworld.random.choice(["2 Gold", "5 Gold", "25 Gold"])

    # Add items to the multiworld
    def create_items(self) -> None:
        items_to_add = ["Victory"]

        location_count = len(self.location_name_to_id)
        filler_count = location_count - len(items_to_add)
        for _ in range(filler_count):
            items_to_add.append(self.get_filler_item_name())

        for name in items_to_add:
            self.multiworld.itempool.append(self.create_item(name))

    # Set Archipelago Logic
    def set_rules(self) -> None:
        
      # Register linear floor progression (e.g. "Act 1 - Reach Floor 2" requires "Act 1 - Reach Floor 1")
      for floor in range(2, ACT_1_FLOOR_COUNT + 1):
        current_name = f"Act 1 - Reach Floor {floor}"
        previous_name = f"Act 1 - Reach Floor {floor - 1}"

        set_rule(
            self.multiworld.get_location(current_name, self.player),
            lambda state, prev=previous_name: state.can_reach_location(prev, self.player)
        )

      # The goal condition is:
      self.multiworld.completion_condition[self.player] = (
          lambda state: state.has("Victory", self.player)
      )

    def fill_slot_data(self) -> dict:
        return {}