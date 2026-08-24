# Ground truth for InfoDB.I.BlueprintsByLevel, captured live via temporary debug logging
# (see project memory) - the real, per-level, ORDER-SENSITIVE pool of buildings vanilla
# reveals as you play each biome. Confirmed live that list *position* (not the
# BuildingInfo.UnlockOrder field, which is unrelated) is what determines reveal order,
# walked sequentially, skipping already-owned entries.
#
# Only the buildings already confirmed in game_data.json are listed here - the real
# per-level pools also contain ~14 additional BuildingType values (mostly concentrated in
# Desert, which is almost entirely uncovered by this list) that show up in this same
# structured game data but were missed by the wiki cross-reference that built
# game_data.json, similar to how the Trophy buildings were originally missed. Worth a
# future follow-up using BuildingInfo.IsInGame as ground truth, but out of scope here -
# those ~14 aren't part of the apworld's item/location pool, so the mod lets them pass
# through as untouched vanilla regardless of what this file does.
#
# Order matters: this is each level's ORIGINAL vanilla reveal order, not shuffled. The
# apworld pools these across all 8 levels and redistributes them (see Rules.py), preserving
# each level's *count* of confirmed entries (the numbers in parentheses below).
BLUEPRINT_POOLS_BY_LEVEL = {
    "kGraveyard": [  # (9)
        "kSheriffOffice", "kGunsmith", "kHauntedHouse", "kClinic", "kBarracks",
        "kShoemaker", "kSchoolhouse", "kConsulate", "kExorcist",
    ],
    "kSnowy": [  # (6 of 9 - kIdleFarm/kIdleLumberyard/kIdleStoneMine not yet tracked)
        "kVeteranHut", "kAlchemist", "kAdventurersGuild", "kWheelwright", "kUniversity",
        "kWatchTower",
    ],
    "kSavanna": [  # (8 of 9 - kMasseuse not yet tracked)
        "kMilitaryAcademy", "kGoldMine", "kDiplomacyHall", "kGuildHall", "kArcheryRange",
        "kMarket", "kMagnetFactory", "kBank",
    ],
    "kHell": [  # (6 of 7 - kCobbler not yet tracked)
        "kMatchMaker", "kJeweler", "kAbbey", "kCasino", "kMansion", "kNecromancer",
    ],
    "kClouds": [  # (5 of 6 - kIdleLauncher not yet tracked)
        "kDenseWheat", "kGamblersDen", "kGrandTree", "kAntiqueShop", "kGemsmith",
    ],
    "kMoon": [  # (7 of 7 - fully tracked)
        "kStoneDomain", "kGraniteSlab", "kCandleMaker", "kHiddenTemple", "kWishingWell",
        "kWarRoom", "kMeditationTent",
    ],
    "kShroom": [  # (6 of 7 - kIdleManagement not yet tracked)
        "kUnstableTower", "kCarpenter", "kBagMaker", "kEvolutionChamber", "kFalconryHut",
        "kRelicCollector",
    ],
    "kDesert": [  # (1 of 8 - kStrengthStatue/kEnduranceStatue/kLogCabin/kDexterityStatue/
                  #  kIntelligenceStatue/kSpeedStatue/kLeadershipStatue not yet tracked)
        "kPartyHouse",
    ],
}

# The 8 level-completion Trophy buildings - one-time events (not a sequential pool to walk),
# so they're safe to suppress with no dependency chain needed at all.
TROPHY_BUILDINGS = [
    "kGraveyardIdol", "kBattlefieldIdol", "kSavannaIdol", "kHellIdol", "kHeavenIdol",
    "kShroomIdol", "kDesertIdol", "kMoonIdol",
]
