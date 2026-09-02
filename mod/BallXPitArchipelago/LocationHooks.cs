using HarmonyLib;
using Il2Cpp;
using Il2CppI2.Loc;
using MelonLoader;

namespace BallXPitArchipelago;

/// <summary>
/// Converts vanilla progression triggers into Archipelago location checks. Character unlock,
/// level access, and blueprints for Trophies/the generation-time pool chain/CharHousing
/// buildings are all suppressed (the vanilla grant is blocked, a check is sent instead, and
/// the real grant only ever happens via ItemReceiver applying the matching AP item) - land
/// expansion and elevator upgrades are the only systems left non-suppressed, since purchases/
/// upgrades stay unrestricted vanilla and just report a check afterward. See
/// GainBlueprintLocationPatch/SuppressibleBuildingTypes for exactly which buildings get
/// suppressed and why (some categories genuinely can't be, safely).
///
/// Location name convention (must match the ballxpit apworld's location table):
///   "Character: {display}"    - vanilla SaveMgr.UnlockChar call site (suppressed unless
///                                 the call came from ItemReceiver applying an AP item)
///   "Blueprint: {display}"    - vanilla SaveMgr.GainBlueprint call site, for buildings
///                                 whose location keeps its real name (currently just the
///                                 7 remaining Trophies)
///   "{Level} pooled blueprint #{n}" / "{Level} char-housing blueprint #{n}" - the same
///                                 GainBlueprint call site, but for buildings whose location
///                                 was renamed positionally (see Rules.py/Locations.py's
///                                 redesign) - BlueprintShuffle.LocationNameFor(bt) is the
///                                 single source of truth for which name a given building
///                                 actually needs; never hardcode "Blueprint: {display}"
///                                 for a blueprint check, always go through that helper.
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

        // CompleteLocationChecks (the sync variant) blocks the calling thread - confirmed by
        // decompiling Archipelago.MultiClient.Net: it calls socket.SendPacket(...), which
        // internally does SendMultiplePacketsAsync(...).Wait(). Every call site here runs on
        // Unity's main thread (Harmony patches execute inline with vanilla's own call stack;
        // Mod.OnUpdate is the per-frame Update loop) - a network hiccup during that .Wait()
        // freezes the ENTIRE game, not just AP features. Confirmed live: a real, otherwise
        // unexplained full freeze at the exact moment a check should have been sent, no
        // exception, no further log output, required a forced quit. The async variant plus a
        // non-blocking continuation avoids this - logging in the continuation is safe off the
        // main thread since it's plain text I/O, not an IL2CPP/Unity call.
        session.Locations.CompleteLocationChecksAsync(id).ContinueWith((System.Threading.Tasks.Task t) =>
        {
            if (t.IsFaulted)
            {
                var ex = t.Exception == null ? "unknown error" : t.Exception.GetBaseException().Message;
                Log?.Warning($"Failed to send location check '{locationName}': {ex}");
            }
            else
            {
                Log?.Msg($"Sent location check: {locationName}");
            }
        });
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

        // SetGoalAchieved() -> SetClientState() -> Socket.SendPacket(...) is ALSO a blocking
        // call under the hood (same class of bug as SendCheck above, confirmed the same way -
        // decompiling the client library) - it has no async variant to call instead, so the
        // fix here is to run it on a background thread rather than block Mod.OnUpdate's
        // per-frame main-thread tick. Fire-and-forget is fine: nothing needs its result, and
        // _goalReported already guards against ever calling it twice.
        var session = ApConnection.Session;
        System.Threading.Tasks.Task.Run(() => session.SetGoalAchieved());
        Log?.Msg("All levels complete - reported goal achieved to Archipelago.");
    }
}

[HarmonyPatch(typeof(SaveMgr), nameof(SaveMgr.UnlockChar))]
internal static class UnlockCharLocationPatch
{
    private static bool Prefix(CharType ct)
    {
        // The Influencer/False Messiah unlocks via vanilla's Twitch Extension integration,
        // not earnable progression - deliberately left untouched by the randomizer (no
        // item, no location - see Items.py/Locations.py), so never intercept its unlock.
        if (ct == CharType.kInfluencer)
            return true;

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
/// the *same* blueprint forever instead of progressing (confirmed live). Three categories
/// are safe to suppress despite that: the 7 remaining Trophy (kXIdol) buildings, since
/// level-completion trophies are one-time events with no pool to get stuck on;
/// BlueprintShuffle's ~62 pool-eligible buildings, now that Rules.py encodes the real
/// per-level dependency chain (position N needs position N-1's item) so the generator
/// never places something unreachable behind it; and BlueprintShuffle's CharHousing
/// buildings (e.g. Cozy Home), also a one-shot-per-character event with no pool to get
/// stuck in - confirmed live these were being granted for real (a bug, not the intended
/// design: getting the real Cozy Home from beating a level with a new character, in
/// addition to an unrelated randomized check reward). kMoonIdol (Void Trophy) is handled
/// separately in GainBlueprintLocationPatch, not here - see BlueprintShuffle.cs. Buildings
/// not in any of these three sets (not-yet-tracked ones) still stay non-suppressing - real
/// grant plus a check layered on top - since there's no evidence suppressing those is safe.
/// </summary>
internal static class SuppressibleBuildingTypes
{
    private static readonly System.Collections.Generic.HashSet<BuildingType> Trophies = new()
    {
        BuildingType.kGraveyardIdol, BuildingType.kBattlefieldIdol, BuildingType.kSavannaIdol,
        BuildingType.kHellIdol, BuildingType.kHeavenIdol, BuildingType.kShroomIdol,
        BuildingType.kDesertIdol,
    };

    internal static bool Contains(BuildingType bt) =>
        Trophies.Contains(bt) ||
        BlueprintShuffle.PoolEligibleBuildings.Contains(bt) ||
        BlueprintShuffle.CharHousingBuildings.Contains(bt);
}

[HarmonyPatch(typeof(SaveMgr), nameof(SaveMgr.GainBlueprint))]
internal static class GainBlueprintLocationPatch
{
    private static bool Prefix(BuildingType bt)
    {
        // Void Trophy is deliberately excluded from the randomizer entirely and permanently
        // repurposed as BlueprintShuffle's placeholder sentinel for the per-level discovery
        // chain (see BlueprintShuffle.cs) - its ownership flag must never be set by
        // anything else, including its own original level-completion trigger, EVER, even
        // while not connected to an AP session. Unlike every other suppression in this mod
        // (which only applies while connected, since there's no session to send a check to
        // otherwise), this one has to be checked before the connection gate: a single real
        // grant here - e.g. completing Vast Void for the first time during an offline play
        // session with the mod merely installed - would permanently flip HasBlueprint(kMoonIdol)
        // to true, and since vanilla skips anything already-owned, every level's discovery
        // chain would desync at once, forever, the next time AP is actually used. No check
        // is sent for it either way; it isn't a real location any more.
        if (bt == BuildingType.kMoonIdol)
        {
            if (ApConnection.Session != null)
                BlueprintShuffle.HandleVoidTrophyGrant();
            return false;
        }

        if (ApConnection.Session == null || ItemReceiver.IsApplyingItem)
            return true;

        // BuildingType has enum values with no confirmed real building behind them (unused/
        // cut content - see GameNames.cs). Only intercept the ones we've verified are real
        // and part of the randomizer; anything else is ignored (vanilla already runs as-is).
        if (!GameNames.BuildingNames.ContainsKey(bt))
            return true;

        LocationHooks.SendCheck(BlueprintShuffle.LocationNameFor(bt));

        var suppress = SuppressibleBuildingTypes.Contains(bt);

        // This building came from something other than the Void Trophy chain (that's the
        // only path that reaches GainBlueprintLocationPatch with bt == kMoonIdol) - if it's
        // one of BlueprintShuffle's managed positions, tell it so the chain doesn't later
        // waste a pickup re-discovering something already resolved this way. See
        // BlueprintShuffle.HandleSideChannelGrant for why this exists generically rather
        // than chasing each individual side channel.
        if (suppress)
            BlueprintShuffle.HandleSideChannelGrant(bt);

        return !suppress;
    }
}

/// <summary>
/// Gates which biomes are selectable: vanilla decides via LevelSelectItem.Init (unlocked)
/// vs InitLocked (locked), so redirecting InitLocked -> Init when the player has received
/// the matching "Level Access" item is enough to override vanilla's ElevatorLvl-based
/// unlock decision - PROVIDED vanilla actually calls InitLocked in the first place. See
/// LevelSelectItemInitPatch below for the other half of this: vanilla picks which of the
/// two to call based on its own ElevatorLvl state, not ours, so this patch alone only ever
/// closes the gap in one direction.
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

        var unlocked = LevelUnlockOrder.IsReachable(inf.Type);
        LocationHooks.Log?.Msg(
            $"LevelSelectItem.InitLocked({inf.Type}): AP-unlocked={unlocked}, " +
            $"ProgressiveLevelAccessCount={ItemReceiver.ProgressiveLevelAccessCount}");

        if (!unlocked)
            return true;

        // Forcing vanilla's own Init to run here is a cross-call vanilla never makes on its
        // own (it only ever calls Init for a level IT already considers unlocked) - confirmed
        // live that the symmetric case (forcing InitLocked below) can throw a
        // NullReferenceException inside vanilla's own code for a level vanilla itself
        // considers unlocked, presumably because InitLocked assumes state that's only valid
        // while genuinely vanilla-locked. Guard defensively here too: an uncaught exception
        // here would propagate out of LevelSelectUI.Activate() entirely, aborting Init/
        // InitLocked for every OTHER level tile queued after this one in the same pass -
        // confirmed live as the actual cause of an unrelated level's blueprint-count display
        // going blank, collateral damage from a crash on a completely different level.
        try
        {
            __instance.Init(inf, ngPlus);
        }
        catch (Exception e)
        {
            LocationHooks.Log?.Warning($"[LevelSelectItemInitLockedPatch] vanilla Init threw for {inf.Type} - leaving this tile as-is rather than letting the exception break every other level's init: {e.Message}");
        }

        return false;
    }
}

/// <summary>
/// Symmetric counterpart to LevelSelectItemInitLockedPatch, closing a real bug (confirmed
/// live): vanilla decides which of Init/InitLocked to call based on its own ElevatorLvl
/// progress, not on AP-received "Level Access" items - so once real elevator-upgrade
/// progress (still 100% vanilla, un-suppressed - see RunElevatorUpgradeLocationPatch)
/// outpaces AP item delivery, vanilla starts calling Init directly for a level whose access
/// item hasn't actually been received yet, which LevelSelectItemInitLockedPatch never even
/// sees (it only patches the "locked" call). Reported live: a single early elevator upgrade
/// granted real, playable access to Snowy with no Level Access item received for it.
/// Redirecting Init -> InitLocked here whenever AP doesn't yet consider the level reachable
/// closes the loophole from the other side, regardless of which of the two vanilla decides
/// to call first.
/// </summary>
[HarmonyPatch(typeof(LevelSelectItem), nameof(LevelSelectItem.Init))]
internal static class LevelSelectItemInitPatch
{
    private static bool Prefix(LevelSelectItem __instance, LevelInfo inf, int ngPlus)
    {
        if (ApConnection.Session == null)
            return true;

        if (ngPlus != 0 || inf == null)
            return true;

        if (LevelUnlockOrder.IsReachable(inf.Type))
            return true;

        LocationHooks.Log?.Msg(
            $"LevelSelectItem.Init({inf.Type}): vanilla considers this unlocked but AP doesn't yet - redirecting to InitLocked.");

        // Confirmed live: vanilla's real InitLocked can throw a NullReferenceException here
        // for a level vanilla itself considers already unlocked (this is exactly that case -
        // we're forcing InitLocked specifically because vanilla thinks it's unlocked and we
        // disagree) - presumably InitLocked relies on "still locked" state that vanilla no
        // longer keeps once its own progress has crossed the threshold. An uncaught exception
        // here propagates out of LevelSelectUI.Activate() and aborts every OTHER level tile's
        // Init/InitLocked queued after this one in the same pass - confirmed live as the
        // actual cause of an unrelated level's blueprint-count display going blank. Swallow
        // it and leave this one tile uninitialized rather than corrupting every other level's
        // display over a single level's rendering glitch.
        try
        {
            __instance.InitLocked(inf, ngPlus);
        }
        catch (Exception e)
        {
            LocationHooks.Log?.Warning($"[LevelSelectItemInitPatch] vanilla InitLocked threw for {inf.Type} - leaving this tile as-is rather than letting the exception break every other level's init: {e.Message}");
        }

        return false;
    }
}

/// <summary>
/// Overrides vanilla's "Undiscovered Blueprints" count on the level-select screen with our
/// own ground truth (BlueprintShuffle's per-level pending-chain length) - see
/// BlueprintShuffle.GetRemainingCount for why vanilla's own counter can't be trusted once
/// items are flowing in from the wider multiworld instead of only from playing that level.
/// Harmony still runs this Postfix even when LevelSelectItemInitPatch's Prefix redirected
/// the call to InitLocked instead - the IsReachable recheck here skips it in that case, so
/// a locked tile never gets a stale "blueprints left" count applied to it.
/// </summary>
[HarmonyPatch(typeof(LevelSelectItem), nameof(LevelSelectItem.Init))]
internal static class LevelSelectItemBlueprintCountPatch
{
    private static void Postfix(LevelSelectItem __instance, LevelInfo inf, int ngPlus)
    {
        if (ApConnection.Session == null || inf == null || ngPlus != 0)
            return;

        if (!LevelUnlockOrder.IsReachable(inf.Type))
            return;

        var remaining = BlueprintShuffle.GetRemainingCount(inf.Type);
        if (remaining == null)
            return;

        __instance.ParamsBlueprintsLeft?.SetParameterValue("num", remaining.Value.ToString());
    }
}

/// <summary>
/// Clicking the elevator to upgrade it stays completely normal vanilla gameplay (earn
/// gears as usual, spend them here as usual, ElevatorLvl still increments as usual so the
/// escalating gear costs for later upgrades still work) - it's a Postfix, not a suppressing
/// Prefix, specifically so none of that changes. The upgrade itself no longer directly
/// unlocks the next biome - that's now enforced by BOTH LevelSelectItemInitLockedPatch and
/// LevelSelectItemInitPatch together (see the latter for why one alone wasn't enough: a
/// real elevator upgrade like this one can make vanilla call Init directly, which the
/// InitLocked-only patch never sees). Instead this sends a location check, so what you get
/// back is "a random unlock" rather than always specifically the next biome.
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
