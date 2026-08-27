from BaseClasses import Tutorial
from worlds.AutoWorld import WebWorld, World

from .Items import (
    BallXPitItem,
    blueprint_item_names,
    character_item_names,
    early_blueprint_item_names,
    elevator_upgrade_filler_item_names,
    item_table,
    land_expansion_filler_item_names,
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

    def generate_early(self) -> None:
        # Soft bias, not a guarantee - see Items.py's early_blueprint_item_names for why
        # these specific buildings. multiworld.early_items[self.player] is already
        # pre-populated (confirmed live, from the player's own YAML early_items option -
        # empty here since this world doesn't expose that option) as a plain dict, not a
        # Counter, so a direct assignment is used rather than +=, which would KeyError on
        # a name not already present.
        for name in early_blueprint_item_names:
            self.multiworld.early_items[self.player][name] = 1

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
        items += [self.create_item(name) for name in land_expansion_filler_item_names]
        items += [self.create_item(name) for name in elevator_upgrade_filler_item_names]
        self.multiworld.itempool += items

    def set_rules(self) -> None:
        set_rules(self)

    def fill_slot_data(self) -> dict:
        return {
            "death_link": bool(self.options.death_link),
            # Computed once in set_rules() (BlueprintPools.py / Rules.py) - the mod applies
            # this exact per-level order rather than computing its own, so the access rules
            # set during generation and what the mod actually does at runtime can never
            # diverge. {level_enum: [building_enum, ...]}.
            "blueprint_order": self.blueprint_order,
            # Fixed (not per-seed) real difficulty progression order - see Rules.py's
            # LEVEL_UNLOCK_ORDER. Exported rather than hand-duplicated on the mod side so
            # the level-select screen's unlock check and the generator's access rules can
            # never disagree about the order. [level_enum, ...].
            "level_unlock_order": self.level_unlock_order,
        }
