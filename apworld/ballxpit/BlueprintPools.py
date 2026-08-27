# Ground truth for InfoDB.I.BlueprintsByLevel, captured live via temporary debug logging
# (see project memory) - the real, per-level pool of buildings vanilla's boss-drop logic
# offers as you play each biome. Confirmed live twice now that the wiki cannot be trusted
# for per-level placement (it disagreed with a live dump on Unstable Tower, Spa, Road
# Keeper, Gatherer's Hut, and the six stat buildings) - only a direct dump of
# InfoDB.I.BlueprintsByLevel is authoritative here.
#
# All 62 buildings vanilla actually offers across the 8 levels are listed below - full
# coverage, confirmed live. The apworld no longer preserves each level's original count or
# order (see Rules.py's _set_blueprint_pool_rules): the pool below is just the flat set of
# real, obtainable buildings, pooled together and re-partitioned across levels at
# generation time with a randomized size per level. What building ends up where, and how
# many positions each level gets, is entirely up to that per-seed shuffle.
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

# The 7 level-completion Trophy buildings that stay part of the randomizer - one-time
# events (not a sequential pool to walk), so they're safe to suppress with no dependency
# chain needed at all. kMoonIdol (Void Trophy) is deliberately NOT here - it's permanently
# suppressed and repurposed as BlueprintShuffle's placeholder sentinel instead of being a
# real check/item (see BlueprintShuffle.cs and LocationHooks.cs).
TROPHY_BUILDINGS = [
    "kGraveyardIdol", "kBattlefieldIdol", "kSavannaIdol", "kHellIdol", "kHeavenIdol",
    "kShroomIdol", "kDesertIdol",
]
