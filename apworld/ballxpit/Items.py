import json
from dataclasses import dataclass
from importlib import resources
from typing import Dict

from BaseClasses import Item, ItemClassification

from .BlueprintPools import BLUEPRINT_POOLS_BY_LEVEL
from .CharHousing import CHAR_HOUSING_NONPOOLED

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

# Most characters aren't required by any access rule (nothing about reaching a location or
# the goal depends on having a SPECIFIC character - the game's own resource/skill
# requirements aren't modeled), so they're "useful" rather than "progression": nice to
# receive, not logically load-bearing.
#
# kInfluencer (The Influencer / The False Messiah) is deliberately excluded: vanilla unlocks
# it only via Twitch Extension integration (linking a Twitch account, audience voting on
# events), not through any earnable progression. Randomizing it would force players to
# engage with that system to get a "real" unlock, which the user explicitly doesn't want -
# so it's left completely untouched, same as vanilla, with no item and no location.
#
# 5 of the 21 ARE progression, though (user caught this gap live, then confirmed the exact
# mechanism from real play experience): Elevator Upgrade locations require having played
# with more than just the starting character - gears for a given upgrade can ONLY be earned
# in the one specific preceding level (not farmed from any already-unlocked level - e.g. the
# final upgrade's 5 gears can only come from beating Clouds, never from replaying
# Graveyard), so the final upgrade genuinely needs 4 received characters (5 total, including
# the always-free starting one) per the wiki's escalating cost schedule - this is an exact
# requirement, not an approximation. See Rules.py's _set_elevator_upgrade_rules for the full
# reasoning - it gates on state.has_from_list(character_item_names, player, K), which only
# the fill algorithm can honor if at least K of the 21 are guaranteed reachable via
# progression placement. Which 5 doesn't matter (nothing else distinguishes them) - 4 needed
# + 1 margin, matching this apworld's established practice of shipping a small buffer above
# exact break-even (see _set_land_expansion_rules).
_ELEVATOR_GATING_CHARACTER_ENUMS = {"kRecaller", "kItchyFinger", "kTunneller", "kTiptoer", "kCogitator"}

for _c in _game_data["characters"]:
    if _c["enum"] == "kInfluencer":
        continue
    _char_classification = (
        ItemClassification.progression
        if _c["enum"] in _ELEVATOR_GATING_CHARACTER_ENUMS
        else ItemClassification.useful
    )
    item_table[f"Character: {_c['display']}"] = ItemData(_c["id"], _char_classification)

# Blueprints in BLUEPRINT_POOLS_BY_LEVEL are progression, not useful: Rules.py chains each
# level's pool into a dependency ladder (position N requires position N-1's item, since the
# mod suppresses the vanilla grant and only the matching AP item flips it), and AP's fill
# algorithm only guarantees progression items are placed early enough to satisfy logic that
# depends on them - useful items are placed in a later, unconstrained pass that doesn't
# respect access rules. Getting this wrong causes real generation failures (confirmed: it
# did, before this fix).
#
# The 11 CharHousing-only buildings (CHAR_HOUSING_NONPOOLED - never part of any level's
# pool) are ALSO progression, for the exact same reason: Rules.py's _set_char_housing_rules
# gates each "Character: X" location on receiving the matching "Blueprint: <building>" item,
# so that item needs the same placement guarantee. Confirmed live (real
# ArchipelagoGenerate.exe run) that leaving these as `useful` fails generation outright -
# "Could not access required locations for accessibility check" for exactly these 11
# characters - since a `useful` item's own placement isn't accessibility-verified, so
# nothing can safely depend on it being reachable. Trophies and any other building not in
# either set has no chain depending on it, so those stay useful.
for _b in _game_data["buildings"]:
    _classification = (
        ItemClassification.progression
        if _b["enum"] in _POOLED_BUILDING_ENUMS or _b["enum"] in CHAR_HOUSING_NONPOOLED
        else ItemClassification.useful
    )
    item_table[f"Blueprint: {_b['display']}"] = ItemData(_b["id"], _classification)

# Level Access items ARE required (they gate the "Complete Level" locations and the goal).
for _name, _code in FILLER_ITEM_IDS.items():
    item_table[_name] = ItemData(_code, ItemClassification.filler)

# Progressive item, not one item per level: every copy received unlocks whichever level is
# next in the receiving player's own real difficulty order (Rules.py's LEVEL_UNLOCK_ORDER),
# regardless of which level's placement in the multiworld actually delivered it. Confirmed
# live this matters - with one distinct "Level Access: X" item per level, a late-order
# level's item could (and did) arrive before an earlier one, and just sat in inventory doing
# nothing until every earlier level's item had also arrived: a real AP grant with no visible
# in-game effect, confusing to a player who doesn't know the internal ordering logic. One
# copy per level except the starting one (already free from a fresh save, no item needed).
PROGRESSIVE_LEVEL_ACCESS_ITEM_NAME = "Progressive Level Access"
PROGRESSIVE_LEVEL_ACCESS_ITEM_ID = 900700
PROGRESSIVE_LEVEL_ACCESS_COUNT = len(_game_data["levels"]) - 1
item_table[PROGRESSIVE_LEVEL_ACCESS_ITEM_NAME] = ItemData(
    PROGRESSIVE_LEVEL_ACCESS_ITEM_ID, ItemClassification.progression
)

character_item_names = [
    f"Character: {c['display']}" for c in _game_data["characters"] if c["enum"] != "kInfluencer"
]
blueprint_item_names = [f"Blueprint: {b['display']}" for b in _game_data["buildings"]]

# BuildingType enum token -> display name, needed to translate BlueprintPools.py's enum
# tokens (ground truth captured from the mod's own debug logging) into real item names -
# items always keep their real building name, even where the matching location has been
# renamed positionally (see Locations.py/Rules.py).
building_enum_to_display = {b["enum"]: b["display"] for b in _game_data["buildings"]}

# BuildingType enum token -> numeric id, needed by Locations.py to give a positionally-named
# location the same id its building has always had (only the display string moves, not the
# id - so this doesn't disturb the frozen id registry game_data.json's header describes).
building_enum_to_id = {b["enum"]: b["id"] for b in _game_data["buildings"]}

# CharType enum token -> display name, needed by Rules.py to gate each "Character: X"
# location on the matching housing blueprint (see CharHousing.py/Rules.py's
# _set_char_housing_rules) without hand-duplicating the character name table again.
character_enum_to_display = {c["enum"]: c["display"] for c in _game_data["characters"]}

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

# One more padding item, for the same reason as the two filler lists above: switching from
# 8 distinct "Level Access: X" items to PROGRESSIVE_LEVEL_ACCESS_COUNT (7) copies of one
# progressive item dropped the pool by exactly 1 (there's no item for the starting level
# any more - it was never actually needed by anything, just previously placed to fill out
# the per-level item set 1:1). Location count is unaffected, so one more filler item keeps
# the pool balanced.
padding_filler_item_names = ["Gold"]
