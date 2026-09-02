# Ground truth for InfoDB.I.BlueprintsByLevel, captured live via temporary debug logging
# (see project memory) - the real, per-level pool of buildings vanilla's boss-drop logic
# offers as you play each biome. Confirmed live multiple times now that the wiki cannot be
# trusted for per-level placement (it disagreed with a live dump on Unstable Tower, Spa,
# Road Keeper, Gatherer's Hut, and the six stat buildings) - only a direct dump of
# InfoDB.I.BlueprintsByLevel is authoritative here.
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
