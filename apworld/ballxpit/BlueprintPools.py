# !!! THE LEVEL KEYS BELOW ARE WRONG FOR SIX OF THE EIGHT LEVELS. Contents and counts are
# right; only the level each list is filed under is wrong. Fixing it needs a new seed (it
# changes per-level position counts and therefore location naming), so it is deliberately
# NOT fixed here yet - see below before trusting any level label in this file.
#
# InfoDB.I.BlueprintsByLevel is indexed by the level's position in the real DIFFICULTY order
# (Graveyard, Snowy, Desert, Shroom, Savanna, Hell, Clouds, Moon), not by LevelType ordinal.
# The dump these keys came from read it by ordinal, so every level whose position differs from
# its enum value got someone else's list. Only kGraveyard and kSnowy coincide. The true owner
# of each list below is the level at that difficulty position:
#
#   filed as kSavanna -> really kDesert      filed as kClouds -> really kSavanna
#   filed as kHell    -> really kShroom      filed as kMoon   -> really kHell
#   filed as kShroom  -> really kClouds      filed as kDesert -> really kMoon
#
# Established live 2026-09-26 via BuildingMgr.GetAvailBlueprintsForLevel, which answers from
# BlueprintsByLevel[difficulty position] for all 8 levels - including two results that rule out
# the ordinal reading outright (kHell and kDesert reported a blueprint available while their
# ordinal slots were empty; kClouds and kMoon reported none while a placeholder sat in theirs).
# The same mistake in the mod cost a player a hard progression stall on Clouds and is fixed
# there - see BlueprintShuffle.SlotIndexFor.
#
# AND THE WIKI WAS RIGHT. The retracted claim that used to head this file said the wiki "cannot
# be trusted for per-level placement" because it disagreed with a live dump on Unstable Tower,
# Spa, Road Keeper, Gatherer's Hut and the six stat buildings. Every one of those five sits at an
# index where position != ordinal, and there was no disagreement at either index where they
# coincide. The wiki was describing real placement; the dump was mislabeling it. Treat the wiki
# as corroborating evidence again, and treat "confirmed live" as only as strong as the indexing
# assumption underneath it.
#
# Ground truth for the CONTENTS of InfoDB.I.BlueprintsByLevel, captured live via temporary debug
# logging (see project memory) - the real pool of buildings vanilla's boss-drop logic offers,
# in vanilla's own order within each list.
#
# All 62 buildings vanilla actually offers across the 8 levels are listed below, in their
# real vanilla order - full coverage, confirmed live. As of the "major design
# reconsideration" redesign (see project memory), this composition is FIXED: no more
# cross-level pooling/reshuffling at generation time. Each level keeps its true vanilla
# blueprint set, count, and discovery order; what's randomized is only what item ends up
# behind each position (see Rules.py's _set_blueprint_pool_rules), not which level a
# building is assigned to or the count of positions per level. This exists specifically so a
# hint naming a building tells the player something true and discoverable about vanilla
# play, instead of a randomized reassignment only the generator's own logs know about.
BLUEPRINT_POOLS_BY_LEVEL = {
    "kGraveyard": [
        "kSheriffOffice", "kGunsmith", "kHauntedHouse", "kClinic", "kBarracks",
        "kShoemaker", "kSchoolhouse", "kConsulate", "kExorcist",
    ],
    "kSnowy": [
        "kVeteranHut", "kIdleFarm", "kAlchemist", "kAdventurersGuild", "kWheelwright",
        "kIdleLumberyard", "kUniversity", "kIdleStoneMine", "kWatchTower",
    ],
    "kSavanna": [
        "kMilitaryAcademy", "kGoldMine", "kDiplomacyHall", "kGuildHall", "kArcheryRange",
        "kMarket", "kMasseuse", "kMagnetFactory", "kBank",
    ],
    "kHell": [
        "kMatchMaker", "kJeweler", "kCobbler", "kAbbey", "kCasino", "kMansion",
        "kNecromancer",
    ],
    "kClouds": [
        "kDenseWheat", "kIdleLauncher", "kGamblersDen", "kGrandTree", "kAntiqueShop",
        "kGemsmith",
    ],
    "kMoon": [
        "kStoneDomain", "kGraniteSlab", "kCandleMaker", "kHiddenTemple", "kWishingWell",
        "kWarRoom", "kMeditationTent",
    ],
    "kShroom": [
        "kUnstableTower", "kCarpenter", "kBagMaker", "kEvolutionChamber", "kFalconryHut",
        "kRelicCollector", "kIdleManagement",
    ],
    "kDesert": [
        "kPartyHouse", "kStrengthStatue", "kEnduranceStatue", "kLogCabin",
        "kDexterityStatue", "kIntelligenceStatue", "kSpeedStatue", "kLeadershipStatue",
    ],
}

# The 7 level-completion Trophy buildings that stay part of the randomizer, keyed by the
# level whose first completion grants them - one-time events (not a sequential pool to
# walk), so they're safe to suppress with no dependency chain needed at all. kMoonIdol
# (Void Trophy) is deliberately NOT here - it's permanently suppressed and repurposed as
# BlueprintShuffle's placeholder sentinel instead of being a real check/item (see
# BlueprintShuffle.cs and LocationHooks.cs).
TROPHY_BUILDING_BY_LEVEL = {
    "kGraveyard": "kGraveyardIdol",
    "kSnowy": "kBattlefieldIdol",
    "kSavanna": "kSavannaIdol",
    "kHell": "kHellIdol",
    "kClouds": "kHeavenIdol",
    "kShroom": "kShroomIdol",
    "kDesert": "kDesertIdol",
}
