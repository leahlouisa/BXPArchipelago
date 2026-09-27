#if DEBUG
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace BallXPitArchipelago;

/// <summary>
/// Dumps vanilla's real InfoDB.I.BlueprintsByLevel and InfoDB.I.CharHousing, to settle whether the
/// 11 "non-pooled CharHousing" buildings are still genuinely absent from every level's blueprint
/// list on the current game version.
///
/// WHY: a player hit a hard progression stall on Clouds - 6 blueprints remaining, with vanilla
/// re-offering Campground (kCampground, one of the 11) on every visit and never advancing to the
/// Void Trophy placeholder behind it. That symptom requires kCampground to be sitting in a list
/// vanilla walks with its "skip already-owned" HasBlueprint filter, which contradicts the premise
/// those 11 were classified on ("confirmed live to appear nowhere in BlueprintsByLevel").
///
/// The likely explanation is that the classification has gone stale. InfoDB has
/// AddPostLaunchBlueprints() / RemovePostLaunchBlueprints() / InsertLvlBlueprint(lvl, idx, type) -
/// the game demonstrably injects blueprints into level lists as post-launch content, and the dump
/// those 11 were classified from is months old. If kCampground now lives in Clouds' list, it isn't
/// non-pooled at all: it's an ordinary pooled building we're misclassifying, and the fix is to give
/// it a real chain position like every other pooled building rather than to special-case it.
///
/// MUST BE CAPTURED BEFORE CONNECTING TO ARCHIPELAGO. BlueprintShuffle rewrites these very lists on
/// connect - StripGlobalList removes every pool-eligible entry and Void Trophy placeholders get
/// inserted - so a post-connect dump shows our own modifications, not vanilla's data. The dump
/// refuses to run while a session is active, and says so, rather than producing a misleading answer.
///
/// InfoDB is static content data, not save state, so any save on the right game VERSION will do -
/// it does not have to be the save that hit the bug.
///
/// Every line is prefixed [BPDUMP] and pipe-delimited for lifting straight out of the log.
/// Press F8 to re-dump on demand (e.g. after entering the base, in case the post-launch blueprints
/// land later than the first dump).
/// </summary>
internal static class DebugBlueprintListDump
{
    private const string Tag = "[BPDUMP]";

    private static bool _dumped;

    /// <summary>
    /// The 11 buildings CharHousing.py classifies as non-pooled - the claim under test. Hardcoded
    /// rather than read from slot data because this dump deliberately runs with no AP connection.
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<BuildingType> ClaimedNonPooled = new()
    {
        BuildingType.kVilla, BuildingType.kCaptainQuarters, BuildingType.kCampground,
        BuildingType.kSingleFamilyHome, BuildingType.kMausoleum, BuildingType.kCozyHome,
        BuildingType.kLab, BuildingType.kBrickHouse, BuildingType.kRockyHill,
        BuildingType.kMonastery, BuildingType.kHovel,
    };

    /// <summary>Called unconditionally from Mod.OnUpdate - deliberately NOT behind the session gate.</summary>
    internal static void Tick()
    {
        if (_dumped || InfoDB.I == null)
            return;

        _dumped = true;
        Dump("first sight of InfoDB");
    }

    internal static void HandleHotkeys(Event e)
    {
        if (e == null || e.type != EventType.KeyDown || e.keyCode != KeyCode.F8)
            return;

        Dump("F8 pressed");
        ApGui.ShowToast("[DBG] Blueprint lists dumped to log");
        e.Use();
    }

    internal static void Dump(string why)
    {
        var log = LocationHooks.Log;

        if (InfoDB.I == null)
        {
            log?.Msg($"{Tag} skipped ({why}): InfoDB not ready yet.");
            return;
        }

        // The whole point of the dump is vanilla's UNMODIFIED data. Once connected, what's in these
        // lists is ours, so reporting it would answer the wrong question convincingly.
        if (ApConnection.Session != null)
        {
            log?.Warning(
                $"{Tag} REFUSING to dump ({why}): connected to Archipelago, so BlueprintsByLevel has " +
                "already been rewritten by BlueprintShuffle (pool-eligible entries stripped, Void " +
                "Trophy placeholders inserted). Relaunch and dump BEFORE connecting.");
            return;
        }

        log?.Msg($"{Tag} === begin === reason={why}");
        DumpBlueprintsByLevel();
        DumpCharHousing();
        log?.Msg($"{Tag} === end ===");
    }

    /// <summary>
    /// Each level's list in order. The IS_CLAIMED_NONPOOLED marker is the actual finding: any entry
    /// flagged there is a building we currently believe isn't in any level list, appearing in one.
    /// </summary>
    private static void DumpBlueprintsByLevel()
    {
        var log = LocationHooks.Log;
        var byLevel = InfoDB.I.BlueprintsByLevel;
        if (byLevel == null)
        {
            log?.Msg($"{Tag} BLUEPRINTSBYLEVEL|<null>");
            return;
        }

        log?.Msg($"{Tag} BLUEPRINTSBYLEVEL|levels={byLevel.Length}|LevelType.kNum={(int)LevelType.kNum}");

        for (var i = 0; i < byLevel.Length; i++)
        {
            var levelName = i < (int)LevelType.kNum ? ((LevelType)i).ToString() : $"<out of range {i}>";
            var list = byLevel[i];
            if (list == null)
            {
                log?.Msg($"{Tag} LEVEL|idx={i}|level={levelName}|<null>");
                continue;
            }

            log?.Msg($"{Tag} LEVEL|idx={i}|level={levelName}|count={list.Count}");

            for (var j = 0; j < list.Count; j++)
            {
                var info = list[j];
                if (info == null)
                {
                    log?.Msg($"{Tag}   ENTRY|level={levelName}|pos={j}|<null>");
                    continue;
                }

                var bt = info.Type;
                var flagged = ClaimedNonPooled.Contains(bt) ? "|IS_CLAIMED_NONPOOLED=YES" : "";
                log?.Msg(
                    $"{Tag}   ENTRY|level={levelName}|pos={j}|type={bt}|name={Safe(() => info.Name)}" +
                    $"|isInGame={Safe(() => info.IsInGame.ToString())}{flagged}");
            }
        }
    }

    /// <summary>
    /// The character -> housing building mapping, and which of those buildings turned up in a level
    /// list above. Confirms the set of 11 is still the right set, independent of placement.
    /// </summary>
    private static void DumpCharHousing()
    {
        var log = LocationHooks.Log;
        var housing = InfoDB.I.CharHousing;
        if (housing == null)
        {
            log?.Msg($"{Tag} CHARHOUSING|<null>");
            return;
        }

        log?.Msg($"{Tag} CHARHOUSING|count={housing.Length}");

        for (var i = 0; i < housing.Length; i++)
        {
            var info = housing[i];
            if (info == null)
            {
                log?.Msg($"{Tag}   HOUSING|idx={i}|<null>");
                continue;
            }

            var bt = info.Type;
            var claimed = ClaimedNonPooled.Contains(bt) ? "YES" : "no";
            log?.Msg(
                $"{Tag}   HOUSING|idx={i}|type={bt}|name={Safe(() => info.Name)}" +
                $"|claimedNonPooled={claimed}|inSomeLevelList={InSomeLevelList(bt)}");
        }
    }

    private static string InSomeLevelList(BuildingType bt)
    {
        var byLevel = InfoDB.I.BlueprintsByLevel;
        if (byLevel == null)
            return "<unknown>";

        for (var i = 0; i < byLevel.Length; i++)
        {
            var list = byLevel[i];
            if (list == null)
                continue;

            for (var j = 0; j < list.Count; j++)
            {
                if (list[j] != null && list[j].Type == bt)
                    return $"{(LevelType)i}@{j}";
            }
        }

        return "no";
    }

    private static string Safe(System.Func<string> f)
    {
        try
        {
            return f() ?? "<null>";
        }
        catch (System.Exception e)
        {
            return $"<ERR {e.GetType().Name}>";
        }
    }
}

// DISABLED (2026-09-26): these two patches watched for vanilla's post-launch blueprint injection.
// They already did their job - confirmed across two runs that neither AddPostLaunchBlueprints nor
// RemovePostLaunchBlueprints ever fires, so the stale-classification theory they were testing is
// dead and they have nothing left to tell us. Switched off rather than deleted because we're up
// against Il2CppInterop's finite patch budget ("Class::Init signatures have been exhausted"):
// crossing it breaks injected-type registration and the mod fails to start. Every patch freed here
// is one DebugBlueprintDropProbe can spend.
#if DEBUG && ENABLE_POST_LAUNCH_BLUEPRINT_WATCH
[HarmonyPatch(typeof(InfoDB), nameof(InfoDB.AddPostLaunchBlueprints))]
internal static class Debug_InfoDB_AddPostLaunchBlueprints
{
    private static void Postfix()
    {
        LocationHooks.Log?.Msg("[BPDUMP] InfoDB.AddPostLaunchBlueprints() RAN - re-dumping to capture the post-injection lists.");
        DebugBlueprintListDump.Dump("after AddPostLaunchBlueprints");
    }
}

[HarmonyPatch(typeof(InfoDB), nameof(InfoDB.RemovePostLaunchBlueprints))]
internal static class Debug_InfoDB_RemovePostLaunchBlueprints
{
    private static void Postfix()
    {
        LocationHooks.Log?.Msg("[BPDUMP] InfoDB.RemovePostLaunchBlueprints() RAN - re-dumping.");
        DebugBlueprintListDump.Dump("after RemovePostLaunchBlueprints");
    }
}
#endif
#endif
