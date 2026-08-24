using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace BallXPitArchipelago;

/// <summary>
/// Converts vanilla progression triggers into Archipelago location checks. Character unlock
/// and level access are the only systems still gated on AP items (suppressing the vanilla
/// grant, converting it into a check instead) - everything else, including blueprints, land
/// expansion, and elevator upgrades, lets the real vanilla action happen unconditionally and
/// just reports it as a check afterward. Blueprint grants moved into this second group after
/// suppression was found to break vanilla's own "next undiscovered blueprint" bookkeeping
/// for a level (see GainBlueprintLocationPatch).
///
/// Location name convention (must match the ballxpit apworld's location table):
///   "Character: {display}"    - vanilla SaveMgr.UnlockChar call site (suppressed unless
///                                 the call came from ItemReceiver applying an AP item)
///   "Blueprint: {display}"    - vanilla SaveMgr.GainBlueprint call site (not suppressed -
///                                 see GainBlueprintLocationPatch for why)
///   "Complete Level: {display}" - LevelData[i].DidComplete flips false -> true
///   "Elevator Upgrade #{n}"   - vanilla BaseMgr.RunElevatorUpgrade call site (not
///                                 suppressed - the upgrade still happens normally)
///   "Land Expansion #{n}"     - vanilla BaseGridMgr.ConfirmExpansion call site (not
///                                 suppressed either, same reasoning as Elevator Upgrade -
///                                 see ConfirmExpansionLocationPatch for why it used to
///                                 block and no longer does)
///
/// See Phase 2 findings (project memory / plan doc) for why levels/chunks need polling
/// instead of a direct property-setter patch: LevelData.DidComplete and
/// BaseChunkInst.IsPurchased are raw IL2CPP field accessors with no backing method.
/// </summary>
internal static class LocationHooks
{
    /// <summary>Must match the apworld's Items.py ELEVATOR_UPGRADE_COUNT.</summary>
    internal const int ElevatorUpgradeCount = 7;

    /// <summary>
    /// Must match the apworld's Items.py LAND_EXPANSION_COUNT. Confirmed real: 25 total
    /// land tracts, 1 already owned at the start, leaving 24 purchasable.
    /// </summary>
    internal const int LandExpansionCount = 24;

    /// <summary>Number of real biomes - matches shared/game_data.json's "levels" entries.</summary>
    internal const int LevelCount = 8;

    internal static MelonLogger.Instance Log;

    private static readonly Dictionary<LevelType, bool> LastLevelComplete = new();
    private static bool _establishedLevelBaseline;
    private static bool _goalReported;

    internal static void SendCheck(string locationName)
    {
        var session = ApConnection.Session;
        if (session == null)
        {
            Log?.Warning($"Would send location check '{locationName}' but not connected to Archipelago.");
            return;
        }

        var id = session.Locations.GetLocationIdFromName(ApConnection.GameName, locationName);
        if (id < 0)
        {
            Log?.Warning($"Unknown location '{locationName}' (not in this slot's data package).");
            return;
        }

        // A vanilla trigger can legitimately fire more than once for the same location (e.g.
        // replaying a level can re-offer an already-discovered blueprint) - re-sending an
        // already-checked location is a harmless no-op server-side, but logging it as if it
        // were new is misleading when diagnosing "nothing happened" reports.
        if (session.Locations.AllLocationsChecked.Contains(id))
        {
            Log?.Msg($"Location '{locationName}' already checked - vanilla re-trigger, no new check sent.");
            return;
        }

        session.Locations.CompleteLocationChecks(id);
        Log?.Msg($"Sent location check: {locationName}");
    }

    /// <summary>Call periodically from Mod.OnUpdate() once SaveMgr/MetaSaveData exist.</summary>
    internal static void PollForChanges()
    {
        if (Log == null || ApConnection.Session == null || SaveMgr.I == null || MetaSaveData.I == null)
            return;

        var lvlData = MetaSaveData.I.LvlData;
        if (lvlData == null)
            return;

        // First pass just establishes the baseline (the game's starting/loaded state isn't
        // a "new" completion) - only report transitions from the second pass onward. Keyed
        // by LevelType, not array position: MetaSaveData.LvlData's array ordering/contents
        // are not guaranteed stable (e.g. across a reconnect within the same process), and
        // indexing by position previously caused a burst of false "just completed" reports
        // for every level on reconnect once the array looked different than before.
        var firstPass = !_establishedLevelBaseline;
        _establishedLevelBaseline = true;

        foreach (var lvl in lvlData)
        {
            if (lvl == null)
                continue;

            var wasComplete = LastLevelComplete.TryGetValue(lvl.Type, out var prev) && prev;
            if (lvl.DidComplete && !wasComplete && !firstPass)
                SendCheck($"Complete Level: {GameNames.LevelDisplay(lvl.Type)}");

            LastLevelComplete[lvl.Type] = lvl.DidComplete;
        }

        ReportGoalIfComplete();
    }

    /// <summary>
    /// Tells the Archipelago server the player has won once all 8 biomes are genuinely
    /// beaten in-game (not just access-granted) - the apworld's completion_condition only
    /// covers what the generator needs to guarantee a solvable seed, it can't detect real
    /// in-game completion itself. SetGoalAchieved cannot be un-sent, so _goalReported guards
    /// against calling it more than once per process (same lifetime as _establishedLevelBaseline).
    /// </summary>
    private static void ReportGoalIfComplete()
    {
        if (_goalReported || LastLevelComplete.Count < LevelCount)
            return;

        foreach (var complete in LastLevelComplete.Values)
        {
            if (!complete)
                return;
        }

        _goalReported = true;
        ApConnection.Session.SetGoalAchieved();
        Log?.Msg("All levels complete - reported goal achieved to Archipelago.");
    }
}

[HarmonyPatch(typeof(SaveMgr), nameof(SaveMgr.UnlockChar))]
internal static class UnlockCharLocationPatch
{
    private static bool Prefix(CharType ct)
    {
        // Not connected (mod installed but unconfigured, or mid-setup): behave as vanilla.
        if (ApConnection.Session == null || ItemReceiver.IsApplyingItem)
            return true;

        LocationHooks.SendCheck($"Character: {GameNames.CharacterDisplay(ct)}");
        return false;
    }
}

/// <summary>
/// Whether suppressing this building's grant is safe. A level's "undiscovered blueprints"
/// pool is walked sequentially by SaveMgr.HasBlueprint(bt) - suppressing unconditionally
/// (the original design) left HasBlueprint permanently false, so vanilla kept re-offering
/// the *same* blueprint forever instead of progressing (confirmed live). Two categories are
/// safe to suppress despite that: the 8 Trophy (kXIdol) buildings, since level-completion
/// trophies are one-time events with no pool to get stuck on; and BlueprintShuffle's ~48
/// pool-eligible buildings, now that Rules.py encodes the real per-level dependency chain
/// (position N needs position N-1's item) so the generator never places something
/// unreachable behind it. Everything else (CharHousing-only buildings, buildings not yet
/// tracked by BlueprintPools.py) has no such chain, so it stays non-suppressing - real grant
/// plus a check layered on top, same as the interim design.
/// </summary>
internal static class SuppressibleBuildingTypes
{
    private static readonly System.Collections.Generic.HashSet<BuildingType> Trophies = new()
    {
        BuildingType.kGraveyardIdol, BuildingType.kBattlefieldIdol, BuildingType.kSavannaIdol,
        BuildingType.kHellIdol, BuildingType.kHeavenIdol, BuildingType.kShroomIdol,
        BuildingType.kDesertIdol, BuildingType.kMoonIdol,
    };

    internal static bool Contains(BuildingType bt) => Trophies.Contains(bt) || BlueprintShuffle.PoolEligibleBuildings.Contains(bt);
}

[HarmonyPatch(typeof(SaveMgr), nameof(SaveMgr.GainBlueprint))]
internal static class GainBlueprintLocationPatch
{
    private static bool Prefix(BuildingType bt)
    {
        if (ApConnection.Session == null || ItemReceiver.IsApplyingItem)
            return true;

        // BuildingType has enum values with no confirmed real building behind them (unused/
        // cut content - see GameNames.cs). Only intercept the ones we've verified are real
        // and part of the randomizer; anything else is ignored (vanilla already runs as-is).
        if (!GameNames.BuildingNames.ContainsKey(bt))
            return true;

        LocationHooks.SendCheck($"Blueprint: {GameNames.BuildingDisplay(bt)}");

        return !SuppressibleBuildingTypes.Contains(bt);
    }
}

/// <summary>
/// Gates which biomes are selectable: vanilla decides via LevelSelectItem.Init (unlocked)
/// vs InitLocked (locked), so redirecting InitLocked -> Init when the player has received
/// the matching "Level Access" item is enough to override vanilla's ElevatorLvl-based
/// unlock decision without needing to touch the elevator/gear economy at all.
/// </summary>
[HarmonyPatch(typeof(LevelSelectItem), nameof(LevelSelectItem.InitLocked))]
internal static class LevelSelectItemInitLockedPatch
{
    private static bool Prefix(LevelSelectItem __instance, LevelInfo inf, int ngPlus)
    {
        if (ApConnection.Session == null)
            return true;

        if (ngPlus != 0 || inf == null)
            return true; // NG+ levels are out of v1 scope - leave vanilla behavior alone.

        var unlocked = ItemReceiver.UnlockedLevels.Contains(inf.Type);
        LocationHooks.Log?.Msg(
            $"LevelSelectItem.InitLocked({inf.Type}): AP-unlocked={unlocked}, " +
            $"UnlockedLevels=[{string.Join(", ", ItemReceiver.UnlockedLevels)}]");

        if (!unlocked)
            return true;

        __instance.Init(inf, ngPlus);
        return false;
    }
}

/// <summary>
/// Clicking the elevator to upgrade it stays completely normal vanilla gameplay (earn
/// gears as usual, spend them here as usual, ElevatorLvl still increments as usual so the
/// escalating gear costs for later upgrades still work) - it's a Postfix, not a suppressing
/// Prefix, specifically so none of that changes. The only difference from vanilla is that
/// the upgrade no longer directly unlocks the next biome (LevelSelectItemInitLockedPatch
/// already ignores ElevatorLvl entirely and only looks at AP-received "Level Access" items)
/// - instead it sends a location check, so what you get back is "a random unlock" rather
/// than always specifically the next biome.
/// </summary>
[HarmonyPatch(typeof(BaseMgr), nameof(BaseMgr.RunElevatorUpgrade))]
internal static class RunElevatorUpgradeLocationPatch
{
    private static void Postfix()
    {
        if (ApConnection.Session == null)
            return;

        var n = MetaSaveData.I?.ElevatorLvl ?? 0;
        if (n < 1 || n > LocationHooks.ElevatorUpgradeCount)
        {
            LocationHooks.Log?.Warning(
                $"BaseMgr.RunElevatorUpgrade fired but ElevatorLvl={n} is outside the expected 1..{LocationHooks.ElevatorUpgradeCount} range - no matching location, skipping.");
            return;
        }

        LocationHooks.SendCheck($"Elevator Upgrade #{n}");
    }
}

/// <summary>
/// Land expansion purchases stay completely vanilla (resources spent, chunk unlocked, no
/// gating) - originally this blocked the purchase until enough of the land-expansion item
/// had been received, but that meant clicking a chunk you couldn't afford *yet* under
/// AP looked identical to clicking one you could afford in-game but hadn't been granted -
/// confusingly silent either way. Postfix instead, same non-blocking pattern as
/// RunElevatorUpgradeLocationPatch: the real purchase always happens, and afterward sends a
/// check for whichever "Land Expansion #n" that purchase corresponds to.
/// </summary>
[HarmonyPatch(typeof(BaseGridMgr), nameof(BaseGridMgr.ConfirmExpansion))]
internal static class ConfirmExpansionLocationPatch
{
    private static void Postfix()
    {
        if (ApConnection.Session == null)
            return;

        var purchased = MetaSaveData.I?.GetNumPurchasedChunks() ?? 0;
        if (purchased < 1 || purchased > LocationHooks.LandExpansionCount)
        {
            LocationHooks.Log?.Warning(
                $"BaseGridMgr.ConfirmExpansion fired but purchased={purchased} is outside the expected 1..{LocationHooks.LandExpansionCount} range - no matching location, skipping.");
            return;
        }

        LocationHooks.SendCheck($"Land Expansion #{purchased}");
    }
}
