from BaseClasses import ItemClassification, Tutorial
from worlds.AutoWorld import WebWorld, World

from .Items import (
    ELEVATOR_GATING_CHARACTER_COUNT,
    PROGRESSIVE_LEVEL_ACCESS_COUNT,
    PROGRESSIVE_LEVEL_ACCESS_ITEM_NAME,
    BallXPitItem,
    all_filler_item_names,
    blueprint_item_names,
    character_item_names,
    early_blueprint_item_names,
    elevator_upgrade_filler_item_names,
    item_table,
    jumpstart_blueprint_item_names,
    land_expansion_filler_item_names,
    padding_filler_item_names,
)
from .Locations import blueprint_pool_location_names, char_housing_location_names, location_table
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

        # Which ELEVATOR_GATING_CHARACTER_COUNT of the 21 characters are progression is
        # picked per-seed here (self.random, not Python's global random - deterministic per
        # player/seed, matching every other per-seed choice AP makes) rather than fixed in
        # Items.py's static item_table - see that file's comment on why WHICH 5 doesn't
        # matter to Rules.py's access rule, only that some 5 are guaranteed reachable.
        self.progression_character_names = set(
            self.random.sample(character_item_names, ELEVATOR_GATING_CHARACTER_COUNT)
        )

        # evosanity_jumpstart hands over the 12 evolution-helper buildings up front (see
        # Options.py for why, and Items.py's JUMPSTART_BUILDING_ENUMS for which). Precollected
        # rather than granted mod-side so AP itself knows the player holds them: their blueprint
        # items come out of the pool in create_items() (replaced by filler, so their locations
        # still hold something worth finding) and any access rule depending on them - each level's
        # blueprint discovery chain gates position N on position N-1's item - sees them as held
        # from the start, which is true.
        #
        # No mod-side change is needed for these: AP delivers starting inventory through the
        # ordinary received-items stream, so ItemReceiver applies them exactly like any other
        # blueprint item.
        self.precollected_blueprint_names = (
            set(jumpstart_blueprint_item_names) if self.options.evosanity_jumpstart else set()
        )
        for name in sorted(self.precollected_blueprint_names):
            self.multiworld.push_precollected(self.create_item(name))

    def create_regions(self) -> None:
        create_regions(self)

    def create_item(self, name: str) -> BallXPitItem:
        data = item_table[name]
        classification = (
            ItemClassification.progression
            if name in self.progression_character_names
            else data.classification
        )
        return BallXPitItem(name, classification, data.code, self.player)

    def create_items(self) -> None:
        items = []
        items += [self.create_item(name) for name in character_item_names]
        # Precollected blueprints are already in the player's starting inventory - creating them
        # again here would put a second, redundant copy in the pool and overrun the location count.
        items += [
            self.create_item(name)
            for name in blueprint_item_names
            if name not in self.precollected_blueprint_names
        ]
        items += [self.create_item(PROGRESSIVE_LEVEL_ACCESS_ITEM_NAME) for _ in range(PROGRESSIVE_LEVEL_ACCESS_COUNT)]
        items += [self.create_item(name) for name in land_expansion_filler_item_names]
        items += [self.create_item(name) for name in elevator_upgrade_filler_item_names]
        items += [self.create_item(name) for name in padding_filler_item_names]

        # Everything above is a fixed set that balances the always-present locations exactly
        # 1:1 (see Items.py's filler list comments for how that balance was arrived at). What's
        # left is whatever THIS seed's options added or removed: evosanity's up-to-90 extra
        # locations, which have no item category of their own, and the blueprint items pulled out
        # above for being precollected. Both are covered by cycling real resource filler.
        #
        # Measured from the regions rather than recomputed from the options, so this stays correct
        # if another conditional location category is ever added - the one thing it must never do
        # is silently disagree with Regions.py about how many locations exist, which is a hard
        # generation failure ("Item count must equal location count").
        shortfall = len(self.multiworld.get_unfilled_locations(self.player)) - len(items)
        if shortfall < 0:
            raise AssertionError(
                f"Ball x Pit built {len(items)} items for only {len(items) + shortfall} locations - "
                "the fixed item set has outgrown the always-present locations, which means one of "
                "Items.py's filler lists needs shrinking rather than padding here."
            )
        items += [
            self.create_item(all_filler_item_names[i % len(all_filler_item_names)])
            for i in range(shortfall)
        ]

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
            # building_enum -> real location name, for every building whose location isn't
            # simply "Blueprint: {building's real name}" - see Locations.py's positional-
            # naming redesign (items always keep their real name; pooled and CharHousing-
            # only blueprint LOCATIONS are positionally named instead, e.g. "Boneyard pooled
            # blueprint #3"). Exported rather than hand-duplicated on the mod side so the
            # check the mod actually sends can never disagree with what the generator named
            # that location. Any building not present here keeps the default
            # "Blueprint: {display}" convention (currently just the 7 remaining Trophies).
            "blueprint_location_names": {
                **blueprint_pool_location_names,
                **char_housing_location_names,
            },
            # Yaml-configurable filler grant sizes (Options.py) - the mod applies these
            # instead of the vanilla 50/50/50/200 amounts when granting a Wood/Stone/Wheat/
            # Gold filler item. Purely cosmetic (filler items are never access-rule-relevant),
            # so no generation-time validation needed.
            "filler_amounts": {
                "Wood": int(self.options.filler_wood_amount),
                "Stone": int(self.options.filler_stone_amount),
                "Wheat": int(self.options.filler_wheat_amount),
                "Gold": int(self.options.filler_gold_amount),
            },
            # Yaml-configurable economy discounts (Options.py) - the mod scales every
            # building/upgrade cost and every land expansion chunk's cost by these percents.
            # Also purely cosmetic to the generator: land expansion and building purchases
            # are unrestricted vanilla (not gated by any access rule), so discounting them
            # can't affect seed completability either way.
            "building_cost_percent": int(self.options.building_cost_percent),
            "land_expansion_cost_percent": int(self.options.land_expansion_cost_percent),
            # How much more a bundle filler item grants than its plain counterpart (Options.py's
            # FillerBundleMultiplier) - ItemReceiver applies it rather than hardcoding a factor.
            "filler_bundle_multiplier": int(self.options.filler_bundle_multiplier),
            # "none" / "evolutions" / "all_balls" - which ball checks this seed created. The mod
            # needs it so its discovery poll doesn't warn about locations absent from the data
            # package, and so an all_balls seed also reports the 21 base-ball checks.
            "evosanity": self.options.evosanity.current_key,
            # "all_biomes" / "evosanity". Purely a runtime concern: the generator's
            # completion_condition is identical either way (see Rules.py), and this is what tells
            # LocationHooks.cs whether beating all 8 biomes is enough to call SetGoalAchieved or
            # whether every evolved ball is also required.
            "goal": self.options.goal.current_key,
            # One-time resource grant for evosanity_jumpstart, or null when it's off. Not modelled
            # as items: resources gate nothing in logic, and keeping them out of the pool keeps the
            # item/location balance above from having to account for them. The mod applies this
            # once per seed, tracked in ApState - see ItemReceiver.ApplyJumpstartIfNeeded.
            "jumpstart_resources": {
                "Gold": int(self.options.jumpstart_gold_amount),
                "Wood": int(self.options.jumpstart_wood_amount),
                "Stone": int(self.options.jumpstart_stone_amount),
                "Wheat": int(self.options.jumpstart_wheat_amount),
            } if self.options.evosanity_jumpstart else None,
        }
