import json
from dataclasses import dataclass
from importlib import resources
from typing import Dict

from BaseClasses import Item, ItemClassification

# .apworld files are loaded directly as zip archives via zipimport, so game_data.json
# isn't a real filesystem path - os.path.dirname(__file__) + open() doesn't work here.
# importlib.resources is zip-safe.
_game_data = json.loads(
    (resources.files(__package__) / "game_data.json").read_text(encoding="utf-8-sig")
)

# Number of purchasable base-expansion chunks the randomizer models. This is a guessed
# round number, not the game's true physical cap (which we don't know exactly) - purchases
# are unrestricted vanilla (see ConfirmExpansionLocationPatch in the mod), this number just
# needs to match between Items.py and Locations.py so "Land Expansion #n" locations exist
# for however many chunks a player could plausibly buy. Safe to tune later once real
# playtesting shows the actual max is meaningfully different.
LAND_EXPANSION_COUNT = 15
LAND_EXPANSION_ITEM_ID = 900400

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

# Characters/Blueprints aren't required by any access rule in this world (nothing about
# reaching a location or the goal depends on having a specific character or building - the
# game's own resource/skill requirements aren't modeled), so they're "useful" rather than
# "progression": nice to receive, not logically load-bearing.
for _c in _game_data["characters"]:
    item_table[f"Character: {_c['display']}"] = ItemData(_c["id"], ItemClassification.useful)

for _b in _game_data["buildings"]:
    item_table[f"Blueprint: {_b['display']}"] = ItemData(_b["id"], ItemClassification.useful)

# Level Access items ARE required (they gate the "Complete Level" locations and the goal).
for _l in _game_data["levels"]:
    item_table[f"Level Access: {_l['display']}"] = ItemData(_l["id"], ItemClassification.progression)

# Genuinely has no gameplay effect when received - land expansion purchases are
# unrestricted vanilla (see ConfirmExpansionLocationPatch in the mod), so there's nothing
# left for this item to grant. filler (not useful) is the honest AP classification for
# that, and the name says so explicitly - a plain "Progressive Land Expansion" name looked
# exactly like every other real reward, which was confusing (a check that visibly "does
# nothing" reads as broken, not as a deliberate no-op).
LAND_EXPANSION_ITEM_NAME = "Land Expansion (No Effect)"
item_table[LAND_EXPANSION_ITEM_NAME] = ItemData(LAND_EXPANSION_ITEM_ID, ItemClassification.filler)

for _name, _code in FILLER_ITEM_IDS.items():
    item_table[_name] = ItemData(_code, ItemClassification.filler)

character_item_names = [f"Character: {c['display']}" for c in _game_data["characters"]]
blueprint_item_names = [f"Blueprint: {b['display']}" for b in _game_data["buildings"]]
level_access_item_names = [f"Level Access: {l['display']}" for l in _game_data["levels"]]

# Padding to keep the item pool exactly matching the location count (see __init__.py) -
# the "Elevator Upgrade #n" locations don't have a matching item category of their own
# (they're new checks on an existing vanilla action, not gating anything), so filler covers
# the gap. Cycled rather than all-one-type for a little variety.
_filler_names_cycle = list(FILLER_ITEM_IDS.keys())
elevator_upgrade_filler_item_names = [
    _filler_names_cycle[i % len(_filler_names_cycle)] for i in range(ELEVATOR_UPGRADE_COUNT)
]
