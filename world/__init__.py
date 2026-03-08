from BaseClasses import Item, Location, Region, Tutorial
from worlds.AutoWorld import WebWorld, World

from .items import item_table
from .locations import location_table


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
    """
    Minimal test world for Slay the Spire II.
    """
    game = "Slay the Spire II"
    web = SlayTheSpire2Web()

    item_name_to_id = {name: data["id"] for name, data in item_table.items()}
    location_name_to_id = location_table

    def create_regions(self) -> None:
        menu = Region("Menu", self.player, self.multiworld)
        test_region = Region("Spire Test", self.player, self.multiworld)

        self.multiworld.regions += [menu, test_region]

        test_region.locations.append(
            SlayTheSpire2Location(
                self.player,
                "Got Gold",
                self.location_name_to_id["Got Gold"],
                test_region
            )
        )

        test_region.locations.append(
            SlayTheSpire2Location(
                self.player,
                "Got Card",
                self.location_name_to_id["Got Card"],
                test_region
            )
        )

        menu.connect(test_region)

    def create_item(self, name: str) -> SlayTheSpire2Item:
        data = item_table[name]
        return SlayTheSpire2Item(name, data["classification"], data["id"], self.player)

    def create_items(self) -> None:
        self.multiworld.itempool.append(self.create_item("Gold Reward"))
        self.multiworld.itempool.append(self.create_item("Card Reward"))

    def set_rules(self) -> None:
        self.multiworld.completion_condition[self.player] = (
            lambda state: state.can_reach_location("Got Gold", self.player)
            and state.can_reach_location("Got Card", self.player)
        )

    def fill_slot_data(self) -> dict:
        return {}