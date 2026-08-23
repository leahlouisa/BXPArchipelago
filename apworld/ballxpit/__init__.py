from BaseClasses import Tutorial
from worlds.AutoWorld import WebWorld, World

from .Items import (
    BallXPitItem,
    LAND_EXPANSION_COUNT,
    blueprint_item_names,
    character_item_names,
    elevator_upgrade_filler_item_names,
    item_table,
    level_access_item_names,
)
from .Locations import location_table
from .Options import BallXPitOptions
from .Regions import create_regions
from .Rules import set_rules


class BallXPitWeb(WebWorld):
    tutorials = [Tutorial(
        "Multiworld Setup Guide",
        "A guide to playing Ball x Pit with Archipelago.",
        "English",
        "setup_en.md",
        "setup/en",
        ["leahlouisa"],
    )]


class BallXPitWorld(World):
    """
    Ball x Pit is a base-building deck-builder roguelite by Kenny Sun. Characters,
    building blueprints, base expansion, and biome access are all randomized.
    """
    game = "Ball x Pit"
    web = BallXPitWeb()
    options_dataclass = BallXPitOptions
    topology_present = False

    item_name_to_id = {name: data.code for name, data in item_table.items()}
    location_name_to_id = {name: data.code for name, data in location_table.items()}

    def create_regions(self) -> None:
        create_regions(self)

    def create_item(self, name: str) -> BallXPitItem:
        data = item_table[name]
        return BallXPitItem(name, data.classification, data.code, self.player)

    def create_items(self) -> None:
        items = []
        items += [self.create_item(name) for name in character_item_names]
        items += [self.create_item(name) for name in blueprint_item_names]
        items += [self.create_item(name) for name in level_access_item_names]
        items += [self.create_item("Progressive Land Expansion") for _ in range(LAND_EXPANSION_COUNT)]
        items += [self.create_item(name) for name in elevator_upgrade_filler_item_names]
        self.multiworld.itempool += items

    def set_rules(self) -> None:
        set_rules(self)

    def fill_slot_data(self) -> dict:
        return {"death_link": bool(self.options.death_link)}
