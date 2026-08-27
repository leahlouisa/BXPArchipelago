import json
from dataclasses import dataclass
from importlib import resources
from typing import Dict

from BaseClasses import Item, ItemClassification

from .BlueprintPools import BLUEPRINT_POOLS_BY_LEVEL

_POOLED_BUILDING_ENUMS = {b for pool in BLUEPRINT_POOLS_BY_LEVEL.values() for b in pool}

# .apworld files are loaded directly as zip archives via zipimport, so game_data.json
# isn't a real filesystem path - os.path.dirname(__file__) + open() doesn't work here.
# importlib.resources is zip-safe.
_game_data = json.loads(
    (resources.files(__package__) / "game_data.json").read_text(encoding="utf-8-sig")
)

# Number of purchasable base-expansion chunks the randomizer models. Confirmed real (not a
# guess): the game has 25 total land tracts, 1 of which you start with, leaving 24
# purchasable - see ConfirmExpansionLocationPatch in the mod for how purchases stay
# unrestricted vanilla. This number just needs to match between Items.py and Locations.py
# so "Land Expansion #n" locations exist for exactly as many chunks as are purchasable.
LAND_EXPANSION_COUNT = 24

# Matches the game's real elevator-upgrade cadence: 8 levels, the first free, each of the
# other 7 needing one elevator upgrade (escalating gear cost) - see BaseMgr.RunElevatorUpgrade
# in the mod. Unlike LAND_EXPANSION_COUNT this isn't a guess, it's read off the wiki's level
# list (gear costs 2/2/2/3/4/4/5 for levels 2-8).
ELEVATOR_UPGRADE_COUNT = 7

FILLER_ITEM_IDS = {
    "Wood": 900401,
    "Stone": 900402,
    "Wheat": 900403,
    "Gold": 900404,
}


class BallXPitItem(Item):
    game = "Ball x Pit"


@dataclass(frozen=True)
class ItemData:
    code: int
    classification: ItemClassification


item_table: Dict[str, ItemData] = {}

# Characters aren't required by any access rule in this world (nothing about reaching a
# location or the goal depends on having a specific character - the game's own
# resource/skill requirements aren't modeled), so they're "useful" rather than
# "progression": nice to receive, not logically load-bearing.
for _c in _game_data["characters"]:
    item_table[f"Character: {_c['display']}"] = ItemData(_c["id"], ItemClassification.useful)

# Blueprints in BLUEPRINT_POOLS_BY_LEVEL are progression, not useful: Rules.py chains each
# level's pool into a dependency ladder (position N requires position N-1's item, since the
# mod suppresses the vanilla grant and only the matching AP item flips it), and AP's fill
# algorithm only guarantees progression items are placed early enough to satisfy logic that
# depends on them - useful items are placed in a later, unconstrained pass that doesn't
# respect access rules. Getting this wrong causes real generation failures (confirmed: it
# did, before this fix). Everything else (Trophies, CharHousing-only buildings, buildings
# not in any pool) has no chain depending on it, so those stay useful.
for _b in _game_data["buildings"]:
    _classification = (
        ItemClassification.progression
        if _b["enum"] in _POOLED_BUILDING_ENUMS
        else ItemClassification.useful
    )
    item_table[f"Blueprint: {_b['display']}"] = ItemData(_b["id"], _classification)

# Level Access items ARE required (they gate the "Complete Level" locations and the goal).
for _l in _game_data["levels"]:
    item_table[f"Level Access: {_l['display']}"] = ItemData(_l["id"], ItemClassification.progression)

for _name, _code in FILLER_ITEM_IDS.items():
    item_table[_name] = ItemData(_code, ItemClassification.filler)

character_item_names = [f"Character: {c['display']}" for c in _game_data["characters"]]
blueprint_item_names = [f"Blueprint: {b['display']}" for b in _game_data["buildings"]]
level_access_item_names = [f"Level Access: {l['display']}" for l in _game_data["levels"]]

# BuildingType enum token -> display name, needed to translate BlueprintPools.py's enum
# tokens (ground truth captured from the mod's own debug logging) into real item/location
# names.
building_enum_to_display = {b["enum"]: b["display"] for b in _game_data["buildings"]}

# Buildings nudged toward early placement (soft bias via World.generate_early - not a
# guarantee, the fill algorithm can still push one later if other constraints crowd it out)
# - confirmed with the user live: the early-tier economy/warfare buildings in Boneyard and
# Snowy's resource buildings, so a player with average luck isn't stuck too long without
# early-game economy options while the rest of the pool stays fully randomized.
_EARLY_BUILDING_ENUMS = [
    "kConsulate", "kSchoolhouse", "kShoemaker", "kGunsmith", "kBarracks", "kClinic",
    "kIdleFarm", "kIdleLumberyard", "kIdleStoneMine",
]
early_blueprint_item_names = [f"Blueprint: {building_enum_to_display[b]}" for b in _EARLY_BUILDING_ENUMS]

# Padding to keep the item pool exactly matching the location count (see __init__.py) -
# the "Elevator Upgrade #n" locations don't have a matching item category of their own
# (they're new checks on an existing vanilla action, not gating anything), so filler covers
# the gap. Cycled rather than all-one-type for a little variety.
_filler_names_cycle = list(FILLER_ITEM_IDS.keys())
elevator_upgrade_filler_item_names = [
    _filler_names_cycle[i % len(_filler_names_cycle)] for i in range(ELEVATOR_UPGRADE_COUNT)
]

# "Land Expansion #n" locations don't have a matching item category of their own either
# (purchases stay unrestricted vanilla - see ConfirmExpansionLocationPatch in the mod, so
# there's nothing real left for an item to gate) - these used to be a dedicated "Land
# Expansion (No Effect)" filler item that genuinely did nothing on receipt, which reads as
# no fun at all for the player. Cycling real Wood/Stone/Wheat grants instead keeps the
# same "nothing is logically gated here" honesty while still being a small treat to
# receive. Gold deliberately excluded, unlike the elevator upgrade filler cycle above -
# land expansion is themed around base-building resources, not currency.
_land_expansion_filler_names_cycle = ["Wood", "Stone", "Wheat"]
land_expansion_filler_item_names = [
    _land_expansion_filler_names_cycle[i % len(_land_expansion_filler_names_cycle)]
    for i in range(LAND_EXPANSION_COUNT)
]
