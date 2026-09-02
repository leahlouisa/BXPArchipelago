# The real, unshuffled InfoDB.I.CharHousing table (character -> the housing building that
# unlocks them) - confirmed live via a temporary debug dump (see project memory), captured
# BEFORE the mod's old runtime shuffle (BlueprintShuffle.ApplyCharHousingOnce, now removed)
# ever touched it. kDefault and kInfluencer have no housing entry in vanilla (Influencer is
# deliberately excluded from the randomizer entirely - see Items.py/Locations.py) and are
# correctly absent here.
CHAR_HOUSING = {
    "kRecaller": "kHauntedHouse",
    "kItchyFinger": "kSheriffOffice",
    "kTunneller": "kStoneDomain",
    "kTiptoer": "kHiddenTemple",
    "kCogitator": "kVilla",
    "kTactician": "kCaptainQuarters",
    "kSpendthrift": "kMansion",
    "kEmbedded": "kVeteranHut",
    "kRadicalAI": "kCampground",
    "kEmptyNester": "kSingleFamilyHome",
    "kShade": "kMausoleum",
    "kCohabitants": "kCozyHome",
    "kPhysicist": "kLab",
    "kBrickHead": "kBrickHouse",
    "kSisyphus": "kRockyHill",
    "kFlagellant": "kMonastery",
    "kWimp": "kHovel",
    "kPackRat": "kUnstableTower",
    "kFalconer": "kFalconryHut",
    "kCarouser": "kPartyHouse",
    "kBackpacker": "kLogCabin",
}

# Of the 21 real housing buildings above, these 11 were confirmed live (same debug session)
# to NOT appear anywhere in BLUEPRINT_POOLS_BY_LEVEL (see BlueprintPools.py) - vanilla grants
# them through a separate, still-unidentified trigger, not the normal per-level sequential
# blueprint walk. The other 10 (kHauntedHouse, kSheriffOffice, kStoneDomain, kHiddenTemple,
# kMansion, kVeteranHut, kUnstableTower, kFalconryHut, kPartyHouse, kLogCabin) ARE in the
# pool and need no special handling - they're ordinary pooled blueprints that also happen to
# unlock a character once built.
CHAR_HOUSING_NONPOOLED = {
    "kVilla", "kCaptainQuarters", "kCampground", "kSingleFamilyHome", "kMausoleum",
    "kCozyHome", "kLab", "kBrickHouse", "kRockyHill", "kMonastery", "kHovel",
}

# Best-effort home level for 10 of the 11 non-pooled buildings, sourced from
# https://ballxpit.wiki.gg/wiki/Buildings and https://ballxpit.wiki.gg/wiki/Characters -
# NOT live-confirmed. Two live-verification attempts this session both hit dead ends:
# SaveMgr.HasBlueprint throws inside vanilla's own code for all 11 (so ownership can't be
# read), and their BuildingInfo flavor text is generic ("A house for The X.") with no level
# mentioned. This project has a documented history of the wiki being wrong about per-level
# *placement* specifically (see BlueprintPools.py's header comment) - these entries are used
# ONLY for a soft, non-binding access_rule hint (see Rules.py's _set_char_housing_rules) and
# every one of these locations also gets LocationProgressType.EXCLUDED regardless, so a wrong
# guess here can misdirect a hint but can never strand a progression item or break
# completability. kHovel (Theater, unlocks the Juggler) has no wiki-sourced level at all - it
# gets no soft access_rule, just the EXCLUDED protection.
CHAR_HOUSING_HOME_LEVEL_GUESS = {
    "kVilla": "kSnowy",
    "kCaptainQuarters": "kHell",
    "kCampground": "kClouds",
    "kSingleFamilyHome": "kSnowy",
    "kMausoleum": "kDesert",
    "kCozyHome": "kGraveyard",
    "kLab": "kShroom",
    "kBrickHouse": "kShroom",
    "kRockyHill": "kDesert",
    "kMonastery": "kSavanna",
}
