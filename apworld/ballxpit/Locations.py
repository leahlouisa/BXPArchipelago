import json
from dataclasses import dataclass
from importlib import resources
from typing import Dict

from BaseClasses import Location

from .Items import ELEVATOR_UPGRADE_COUNT, LAND_EXPANSION_COUNT

_game_data = json.loads(
    (resources.files(__package__) / "game_data.json").read_text(encoding="utf-8-sig")
)

LAND_EXPANSION_LOCATION_BASE_ID = 900500
ELEVATOR_UPGRADE_LOCATION_BASE_ID = 900600


class BallXPitLocation(Location):
    game = "Ball x Pit"


@dataclass(frozen=True)
class LocationData:
    code: int


location_table: Dict[str, LocationData] = {}

# Character/Blueprint locations use the exact same name (and even the same numeric id,
# safe since items and locations live in separate namespaces) as their matching item -
# it's the same underlying game action viewed from two sides: your world reaching that
# vanilla trigger sends a check, and receiving the item is what actually grants it.
for _c in _game_data["characters"]:
    location_table[f"Character: {_c['display']}"] = LocationData(_c["id"])

for _b in _game_data["buildings"]:
    location_table[f"Blueprint: {_b['display']}"] = LocationData(_b["id"])

for _l in _game_data["levels"]:
    location_table[f"Complete Level: {_l['display']}"] = LocationData(_l["id"])

land_expansion_location_names = []
for _n in range(1, LAND_EXPANSION_COUNT + 1):
    _name = f"Land Expansion #{_n}"
    location_table[_name] = LocationData(LAND_EXPANSION_LOCATION_BASE_ID + _n - 1)
    land_expansion_location_names.append(_name)

# Clicking the elevator to upgrade it stays a completely normal, unmodified gameplay
# action (earn gears from level completions as usual, spend them here as usual) - it's
# just that what you get for doing it is a check into the multiworld instead of the
# vanilla "next biome unlocked" effect. Biome access itself still comes from receiving a
# "Level Access: X" item (see Rules.py) - these locations don't grant that directly.
elevator_upgrade_location_names = []
for _n in range(1, ELEVATOR_UPGRADE_COUNT + 1):
    _name = f"Elevator Upgrade #{_n}"
    location_table[_name] = LocationData(ELEVATOR_UPGRADE_LOCATION_BASE_ID + _n - 1)
    elevator_upgrade_location_names.append(_name)

character_location_names = [f"Character: {c['display']}" for c in _game_data["characters"]]
blueprint_location_names = [f"Blueprint: {b['display']}" for b in _game_data["buildings"]]
complete_level_location_names = [f"Complete Level: {l['display']}" for l in _game_data["levels"]]
