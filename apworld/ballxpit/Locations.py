import json
from dataclasses import dataclass
from importlib import resources
from typing import Dict

from BaseClasses import Location

from .BlueprintPools import BLUEPRINT_POOLS_BY_LEVEL
from .CharHousing import CHAR_HOUSING_HOME_LEVEL_GUESS, CHAR_HOUSING_NONPOOLED
from .Items import ELEVATOR_UPGRADE_COUNT, LAND_EXPANSION_COUNT, building_enum_to_id

_game_data = json.loads(
    (resources.files(__package__) / "game_data.json").read_text(encoding="utf-8-sig")
)

LAND_EXPANSION_LOCATION_BASE_ID = 900500
ELEVATOR_UPGRADE_LOCATION_BASE_ID = 900600

_level_display = {l["enum"]: l["display"] for l in _game_data["levels"]}


class BallXPitLocation(Location):
    game = "Ball x Pit"


@dataclass(frozen=True)
class LocationData:
    code: int


location_table: Dict[str, LocationData] = {}

# Character locations use the exact same name (and even the same numeric id, safe since
# items and locations live in separate namespaces) as their matching item - it's the same
# underlying game action viewed from two sides: your world reaching that vanilla trigger
# sends a check, and receiving the item is what actually grants it. Unlike blueprints below,
# characters keep real names on both sides - there's no sequential "position" to a character
# unlock the way there is for a level's blueprint-discovery order, so positional naming
# wouldn't add any information a real name doesn't already give.
#
# kInfluencer excluded here too - see the matching comment in Items.py. No location either,
# since UnlockCharLocationPatch never intercepts its vanilla (Twitch-driven) unlock trigger.
for _c in _game_data["characters"]:
    if _c["enum"] == "kInfluencer":
        continue
    location_table[f"Character: {_c['display']}"] = LocationData(_c["id"])

# Blueprint locations: items always keep the building's real name (see Items.py), but
# locations are split by category - this is the whole point of the redesign that produced
# this file's current shape (see project memory's "Major design reconsideration" and
# follow-up entries). A location's real building name only tells a player anything useful if
# they already know vanilla's building-to-level mapping by heart; a positional name is
# self-describing to anyone, and directly encodes real sequence info a name never could
# (chain position N literally means "N-1 prerequisites already found").
#
# Pooled blueprints (BLUEPRINT_POOLS_BY_LEVEL, fixed real vanilla per-level composition - no
# more cross-level shuffling): "{level} pooled blueprint #{position}", numbered per level in
# the same order vanilla's own sequential discovery walks that level's list.
blueprint_pool_location_names: Dict[str, str] = {}  # building_enum -> location name
for _level_enum, _buildings in BLUEPRINT_POOLS_BY_LEVEL.items():
    for _i, _building_enum in enumerate(_buildings):
        _loc_name = f"{_level_display[_level_enum]} pooled blueprint #{_i + 1}"
        location_table[_loc_name] = LocationData(building_enum_to_id[_building_enum])
        blueprint_pool_location_names[_building_enum] = _loc_name

# CharHousing-only blueprints (never part of any level's pool - see CharHousing.py):
# "{level} char-housing blueprint #{position}" for the 10 with a wiki-sourced home level
# (position numbered among just the housing buildings guessed for that level), or a single
# dedicated non-level-scoped name for kHovel (Theater/Juggler), whose home level is unknown.
char_housing_location_names: Dict[str, str] = {}  # building_enum -> location name
_char_housing_by_level: Dict[str, list] = {}
for _building_enum in sorted(CHAR_HOUSING_NONPOOLED):
    _level_enum = CHAR_HOUSING_HOME_LEVEL_GUESS.get(_building_enum)
    if _level_enum is not None:
        _char_housing_by_level.setdefault(_level_enum, []).append(_building_enum)

for _level_enum, _buildings in _char_housing_by_level.items():
    for _i, _building_enum in enumerate(_buildings):
        _loc_name = f"{_level_display[_level_enum]} char-housing blueprint #{_i + 1}"
        location_table[_loc_name] = LocationData(building_enum_to_id[_building_enum])
        char_housing_location_names[_building_enum] = _loc_name

for _building_enum in CHAR_HOUSING_NONPOOLED - set(CHAR_HOUSING_HOME_LEVEL_GUESS):
    _loc_name = "Char-housing blueprint (unconfirmed level)"
    location_table[_loc_name] = LocationData(building_enum_to_id[_building_enum])
    char_housing_location_names[_building_enum] = _loc_name

# Every other building (currently just the 7 remaining level-completion Trophies - kMoonIdol
# is deliberately excluded from game_data.json entirely, see BlueprintPools.py) keeps its
# real name on both the item and the location - it's a single one-time reward with an
# already-unambiguous real name, no benefit to renaming it positionally.
_positional_building_enums = set(blueprint_pool_location_names) | set(char_housing_location_names)
for _b in _game_data["buildings"]:
    if _b["enum"] in _positional_building_enums:
        continue
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

character_location_names = [
    f"Character: {c['display']}" for c in _game_data["characters"] if c["enum"] != "kInfluencer"
]
complete_level_location_names = [f"Complete Level: {l['display']}" for l in _game_data["levels"]]
