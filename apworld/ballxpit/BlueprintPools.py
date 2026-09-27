# The level keys below are CORRECT. A previous version of this comment claimed they were wrong
# for six of the eight levels; that claim was mistaken and is retracted. Do not "fix" them.
#
# InfoDB.I.BlueprintsByLevel is indexed by LevelType ordinal, so BlueprintsByLevel[4] really is
# kClouds's list and these keys mean what they say. Proven twice, 2026-09-27, both independent of
# any array indexing:
#   - InfoDB.I.Levels[i].Type == (LevelType)i for all 8 entries.
#   - HeroInfo.reqLevel is a PER-BALL field, and all 7 gated balls agree with UnlocksByLevel read
#     by ordinal: kCharm's own reqLevel is kClouds and it sits at UnlocksByLevel[4]; likewise
#     kDark/0, kStone/1, kFlesh/2, kTime/3, kCell/6, kLight/7.
#
# The wiki caveat that used to head this file therefore stands as originally written: it
# disagreed with a live dump on Unstable Tower, Spa, Road Keeper, Gatherer's Hut and the six stat
# buildings, and the dump was right. Only a direct dump of InfoDB.I.BlueprintsByLevel is
# authoritative for per-level placement.
#
# BUT DO NOT CONFUSE THAT WITH HOW VANILLA *READS* THE ARRAY, which is a separate and still
# unexplained fact: BuildingMgr.GetAvailBlueprintsForLevel(lt) answers from
# BlueprintsByLevel[position of lt in the difficulty order], not [(int)lt]. Confirmed live before
# and after a fix, including two results that rule out an ordinal read outright (kHell and
# kDesert reported a blueprint available while their own ordinal slots were EMPTY; kClouds and
# kMoon reported none while an unowned placeholder sat in theirs). That is why
# BlueprintShuffle.SlotIndexFor writes the mod's placeholder at the difficulty-position index -
# it has to put the sentinel where vanilla looks. It caused a real, permanent progression stall
# before it was fixed.
#
# So: this file's keys describe where buildings LIVE (ordinal, correct). SlotIndexFor describes
# where vanilla LOOKS (difficulty position, also correct). Both are true at once, and conflating
# them is what produced the retracted claim above.
#
# Ground truth for InfoDB.I.BlueprintsByLevel, captured live via temporary debug logging (see
# project memory) - the real, per-level pool of buildings vanilla's boss-drop logic offers as you
# play each biome, in vanilla's own order within each list.
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
