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

# Larger-denomination versions of the four above, granting filler_bundle_multiplier times as
# much (see Options.py). They exist for variety rather than for balance: evosanity adds up to 90
# locations while adding no items of its own, so those pool slots are filler either way, and
# cycling eight names instead of four keeps the received-items feed from reading like a stuck
# record. The mod maps each name back to its resource and multiplies - see ItemReceiver.cs.
BUNDLE_FILLER_ITEM_IDS = {
    "Wood Crate": 900405,
    "Stone Crate": 900406,
    "Wheat Crate": 900407,
    "Gold Cache": 900408,
}

# Run-scoped filler: helps only the run you're currently playing (or the next one you enter),
# rather than adding permanently to the base economy. Added after a real report from live play -
# an evosanity seed is mostly filler by construction, and a steady stream of resource grants was
# both boring and genuinely inflationary, leaving the player far too rich to feel the building
# economy at all.
#
# Both map onto ordinary vanilla operations, NOT GameCheatMgr.ApplyCheat (which has kLevelUp and
# kGainFuser entries that would have been a one-line implementation, but almost certainly set
# BattleSaveData.Cheated - AchMgr.ShouldCheckAch is the obvious consumer - and would suppress
# Steam achievements for that run):
#
#   Free Level Up   -> PickupMgr.AddXP(TgtXP - CurXP), i.e. exactly one level, not an estimate
#   Fusion Reactor  -> PickupMgr.DropPickup(playerPos, PickupType.kFuser)
#
# The Fusion Reactor is especially apt for evosanity: a fuser's three options are
# FuserOptionType {kCombo, kEvo, kFreeUpgrades}, so one of them is literally the evolution
# option this whole mode is about.
#
# Received while not in a run, these queue until the next one starts - see the mod's
# RunScopedRewards.cs. They deliberately do NOT use ItemReceiver's retry path, which stops the
# whole item queue at the first item that won't apply.
RUN_SCOPED_FILLER_ITEM_IDS = {
    "Free Level Up": 900409,
    "Fusion Reactor": 900410,
}

# The buildings evosanity_jumpstart hands over at the start. Confirmed against game_data.json
# that all 12 are ordinary pooled blueprints and none is a CharHousing building, so precollecting
# them can't unlock a character early - the only thing they affect is how fast evolutions come.
# Confirmed with the user that none of them changes which evolutions are POSSIBLE (the Evolution
# Chamber just starts new balls at level 2, etc.), so this option can never affect completability
# and needs no access-rule handling at all.
JUMPSTART_BUILDING_ENUMS = [
    "kJeweler", "kNecromancer", "kMatchMaker", "kCandleMaker", "kGamblersDen", "kCasino",
    "kWishingWell", "kEvolutionChamber", "kExorcist", "kGemsmith", "kBagMaker",
    "kAdventurersGuild",
]


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
# ELEVATOR_GATING_CHARACTER_COUNT of the 21 need to be progression, though (user caught this
# gap live, then confirmed the exact mechanism from real play experience): Elevator Upgrade
# locations require having played with more than just the starting character - gears for a
# given upgrade can ONLY be earned in the one specific preceding level (not farmed from any
# already-unlocked level - e.g. the final upgrade's 5 gears can only come from beating
# Clouds, never from replaying Graveyard), so the final upgrade genuinely needs 4 received
# characters (5 total, including the always-free starting one) per the wiki's escalating
# cost schedule - this is an exact requirement, not an approximation. See Rules.py's
# _set_elevator_upgrade_rules for the full reasoning - it gates on
# state.has_from_list(character_item_names, player, K), which only the fill algorithm can
# honor if at least K of the 21 are guaranteed reachable via progression placement.
#
# WHICH 5 doesn't matter to that rule (nothing else distinguishes them, it just checks "any K
# of the full 21-name list") - so which characters get the progression slots is chosen
# per-seed, not fixed here. Every character starts "useful" in this static table;
# BallXPitWorld.generate_early() promotes a random ELEVATOR_GATING_CHARACTER_COUNT of them to
# progression per player (self.random, so different seeds - even for the same player - land
# on a different set), and create_item() applies that override. 4 needed + 1 margin, matching
# this apworld's established practice of shipping a small buffer above exact break-even (see
# _set_land_expansion_rules).
ELEVATOR_GATING_CHARACTER_COUNT = 5

for _c in _game_data["characters"]:
    if _c["enum"] == "kInfluencer":
        continue
    item_table[f"Character: {_c['display']}"] = ItemData(_c["id"], ItemClassification.useful)

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

for _name, _code in BUNDLE_FILLER_ITEM_IDS.items():
    item_table[_name] = ItemData(_code, ItemClassification.filler)

for _name, _code in RUN_SCOPED_FILLER_ITEM_IDS.items():
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

_RUN_SCOPED_CYCLE = list(RUN_SCOPED_FILLER_ITEM_IDS)


def _half_run_scoped(length: int, resource_cycle) -> list:
    """
    A filler list of exactly `length` names, alternating resource / run-scoped so half of it
    helps only the current run.

    Every filler list in this file goes through here (or through all_filler_item_names, which is
    built on the same principle), so "about half of all filler is run-scoped" holds globally
    rather than only for the filler evosanity adds. An earlier version interleaved only the
    evosanity padding, which left the real figure at ~38% because the 32 always-present filler
    items below were still all resources.

    Length is preserved exactly - these lists are load-bearing for the item/location balance
    (see __init__.py's create_items), so this only ever changes WHICH names appear, never how
    many.
    """
    out = []
    resource_idx = 0
    run_scoped_idx = 0
    for i in range(length):
        if i % 2:
            out.append(_RUN_SCOPED_CYCLE[run_scoped_idx % len(_RUN_SCOPED_CYCLE)])
            run_scoped_idx += 1
        else:
            out.append(resource_cycle[resource_idx % len(resource_cycle)])
            resource_idx += 1
    return out


# Padding to keep the item pool exactly matching the location count (see __init__.py) -
# the "Elevator Upgrade #n" locations don't have a matching item category of their own
# (they're new checks on an existing vanilla action, not gating anything), so filler covers
# the gap. Cycled rather than all-one-type for a little variety.
_filler_names_cycle = list(FILLER_ITEM_IDS.keys())
elevator_upgrade_filler_item_names = _half_run_scoped(ELEVATOR_UPGRADE_COUNT, _filler_names_cycle)

# "Land Expansion #n" locations don't have a matching item category of their own either
# (purchases stay unrestricted vanilla - see ConfirmExpansionLocationPatch in the mod, so
# there's nothing real left for an item to gate) - these used to be a dedicated "Land
# Expansion (No Effect)" filler item that genuinely did nothing on receipt, which reads as
# no fun at all for the player. Cycling real Wood/Stone/Wheat grants instead keeps the
# same "nothing is logically gated here" honesty while still being a small treat to
# receive. Gold deliberately excluded, unlike the elevator upgrade filler cycle above -
# land expansion is themed around base-building resources, not currency. That exclusion still
# holds for the resource half; the run-scoped half interleaved through it is thematically neutral
# (a free level-up isn't currency either).
_land_expansion_filler_names_cycle = ["Wood", "Stone", "Wheat"]
land_expansion_filler_item_names = _half_run_scoped(
    LAND_EXPANSION_COUNT, _land_expansion_filler_names_cycle
)

# One more padding item, for the same reason as the two filler lists above: switching from
# 8 distinct "Level Access: X" items to PROGRESSIVE_LEVEL_ACCESS_COUNT (7) copies of one
# progressive item dropped the pool by exactly 1 (there's no item for the starting level
# any more - it was never actually needed by anything, just previously placed to fill out
# the per-level item set 1:1). Location count is unaffected, so one more filler item keeps
# the pool balanced.
#
# Run-scoped rather than the Gold it used to be, purely as the tie-breaker that makes the
# always-present filler an exact 16/16 split: the 24 land-expansion items split 12/12 and the 7
# elevator ones 4/3, leaving resources one ahead until this one lands on the other side.
padding_filler_item_names = [_RUN_SCOPED_CYCLE[0]]

# Blueprint items evosanity_jumpstart precollects (and therefore removes from the pool - see
# __init__.py). Items keep their real building name, same as every other blueprint item.
jumpstart_blueprint_item_names = [
    f"Blueprint: {building_enum_to_display[_b]}" for _b in JUMPSTART_BUILDING_ENUMS
]

# The cycle used to pad the pool out to match a variable location count (evosanity's checks, plus
# replacements for any precollected blueprint).
#
# Every other entry is run-scoped, so about half of all filler helps the run you're playing rather
# than the permanent base economy - the fix for a real complaint from live play, where an
# all_balls seed's ~100 filler items were both monotonous and left the player so resource-rich
# that the building economy stopped mattering. Resource grants alternate plain/bundle within their
# half so a player still sees both denominations early.
#
# 16 entries: 4 plain + 4 bundle resources, 8 run-scoped. An exact 50/50 split, and the length
# means a long unbroken run of filler doesn't repeat the same item for a while.
_RESOURCE_FILLER_CYCLE = [
    name
    for pair in zip(FILLER_ITEM_IDS, BUNDLE_FILLER_ITEM_IDS)
    for name in pair
]

all_filler_item_names = _half_run_scoped(
    len(_RESOURCE_FILLER_CYCLE) * 2, _RESOURCE_FILLER_CYCLE
)
