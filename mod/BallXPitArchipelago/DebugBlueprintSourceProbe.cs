#if DEBUG
using Il2Cpp;
using UnityEngine;

namespace BallXPitArchipelago;

/// <summary>
/// Answers, at last, WHICH LIST vanilla draws a blueprint reward from - by simply calling vanilla's
/// own "what is available" queries and printing what comes back.
///
/// WHY THIS EXISTS: a player stalled on Clouds with vanilla re-offering Campground (kCampground, a
/// non-pooled CharHousing building) on every level completion. Two theories have now been killed by
/// evidence, not argument:
///
///   1. "The selector reads InfoDB.BlueprintsByLevel." Dead: at the moment of the bad pick, Clouds'
///      list held exactly one entry and it WAS an unowned Void Trophy placeholder at position 0.
///   2. "Our own StripGlobalList emptied BossDropBlueprints/FuserDropBlueprints, so vanilla fell
///      through to an any-unowned-building rule." Dead: EnsureVoidTrophyPlaceholder put an unowned
///      Void Trophy back into both lists (confirmed live, "bossList=1|fuserList=1"), and vanilla
///      STILL picked Campground.
///
/// So vanilla is not choosing from any list we have been modifying. BuildingMgr turns out to expose
/// the real queries - GetAvailBlueprintsForLevel(LevelType), GetAvailBossBlueprints(),
/// GetAvailFuserBlueprints(out int) - and whichever of those contains kCampground identifies the
/// path. Printing all three at once also shows whether the Void Trophy placeholder survives into
/// them, which is the other half of the answer: if the placeholder is filtered out where Campground
/// is not, the fix is about BuildingInfo.CanDropBlueprint(), not about list contents.
///
/// NO HARMONY PATCHES. Every line here is a field read or a vanilla query, called on demand from a
/// hotkey. That matters twice over: patching anything returning PickupObj breaks injected-type
/// registration and crashes mod startup (three dead builds), and a probe with no patches cannot
/// perturb what it measures.
///
/// RUNS FROM THE BASE - NO RUN REQUIRED. While the player is stalled, the stalled state is fully
/// visible standing in the base: whatever vanilla is about to offer is already "available" now.
/// Press F7. That turns a multi-minute play-a-level iteration into a keypress.
///
/// HOUSING-UPGRADE SIDE CHANNEL: CharMetaInst.ShouldGainHousingUpgrade(int lvl) / HasHousingUpgrade()
/// are also printed per character. The name is suggestive, but the surrounding API
/// (BuildingUtl.GetHousingUpgradeStat -> StatType, BuildingInst.ApplyHousingUpgradeDesc,
/// GameCharInfoUI.LocHousingUpgraded) says these are about an EXISTING housing building gaining a
/// stat bonus once its character's permanent level crosses a threshold - not about awarding the
/// blueprint. Printed to rule it out cheaply in the same pass, not because it is the theory.
/// </summary>
internal static class DebugBlueprintSourceProbe
{
    private const string Tag = "[BPSRC]";

    /// <summary>The 11 CharHousing buildings that appear in no level's list. Campground is one.</summary>
    private static readonly System.Collections.Generic.HashSet<BuildingType> NonPooledCharHousing = new()
    {
        BuildingType.kVilla, BuildingType.kCaptainQuarters, BuildingType.kCampground,
        BuildingType.kSingleFamilyHome, BuildingType.kMausoleum, BuildingType.kCozyHome,
        BuildingType.kLab, BuildingType.kBrickHouse, BuildingType.kRockyHill,
        BuildingType.kMonastery, BuildingType.kHovel,
    };

    internal static void HandleHotkeys(Event e)
    {
        if (e == null || e.type != EventType.KeyDown || e.keyCode != KeyCode.F7)
            return;

        Dump("F7 pressed");
        ApGui.ShowToast("[DBG] Blueprint availability dumped to log");
        e.Use();
    }

    internal static void Dump(string why)
    {
        var log = LocationHooks.Log;
        log?.Msg($"{Tag} === begin === reason={why}");

        DumpVanillaAvailability();
        DumpDroppability();
        DumpCharacters();

        log?.Msg($"{Tag} === end ===");
    }

    /// <summary>
    /// The heart of it: vanilla's own answers to "what blueprints are available right now". If
    /// kCampground shows up in exactly one of these, that names the path we have never intercepted.
    /// </summary>
    private static void DumpVanillaAvailability()
    {
        var log = LocationHooks.Log;

        log?.Msg($"{Tag} SAVEMGR|numBlueprintsAvailable={Str(() => SaveMgr.I.GetNumBlueprintsAvailable().ToString())}");

        if (BuildingMgr.I == null)
        {
            log?.Msg($"{Tag} BUILDINGMGR|<null - stand in the base and press F7 again>");
            return;
        }

        log?.Msg(
            $"{Tag} COUNTS|numBossAvailable={Str(() => BuildingMgr.I.GetNumBossBlueprintsAvailable().ToString())}" +
            $"|numFuserAvailable={Str(() => BuildingMgr.I.GetNumFuserBlueprintsAvailable().ToString())}");

        DumpList("AVAIL_BOSS", Obj(() => BuildingMgr.I.GetAvailBossBlueprints()));

        var fuserGot = -1;
        var fuserList = Obj(() =>
        {
            var l = BuildingMgr.I.GetAvailFuserBlueprints(out var n);
            fuserGot = n;
            return l;
        });
        log?.Msg($"{Tag} AVAIL_FUSER|numBlueprintsGot(out)={fuserGot}");
        DumpList("AVAIL_FUSER", fuserList);

        // Vanilla's global gating thresholds, as static data. These decide how many level
        // completions / bosses / fusers are needed before the Nth blueprint is offered at all, so
        // they bound any per-level explanation.
        LocationHooks.Log?.Msg(
            $"{Tag} THRESHOLDS|bossGot={Str(() => Ints(StatUtl.kBossBlueprintGotThresholds))}" +
            $"|bossMult={Str(() => Ints(StatUtl.kBossBlueprintMultipliers))}" +
            $"|fuserGot={Str(() => Ints(StatUtl.kFuserBlueprintGotThresholds))}" +
            $"|fuserMult={Str(() => Ints(StatUtl.kFuserBlueprintMultipliers))}");

        // Every level, not just the current one: the stall is level-shaped, so seeing all 8 side by
        // side shows whether Clouds is special or whether every level would behave this way.
        //
        // THE KEY COMPARISON. Three things go on one line per level so they can be read against
        // each other directly:
        //   listCount/entries - what InfoDB.BlueprintsByLevel ACTUALLY holds right now, post-connect.
        //                       The older [BPDUMP] refuses to run while connected (by design, so it
        //                       can't report our own edits as vanilla's data), which left exactly
        //                       this blind spot.
        //   pending           - how many chain positions the MOD believes the level still owes.
        //   avail             - what vanilla says is available.
        // A level with pending>0 and a placeholder in its list but avail=0 is the bug, and
        // NumBlueprintDropAttempts is the prime suspect for why: if vanilla treats it as a cursor
        // into the level's list, then a list we have shrunk to a single placeholder reads as
        // exhausted the moment that counter passes 1, no matter how much the chain still owes.
        for (var i = 0; i < (int)LevelType.kNum; i++)
        {
            var lt = (LevelType)i;
            var avail = Obj(() => BuildingMgr.I.GetAvailBlueprintsForLevel(lt));
            LocationHooks.Log?.Msg(
                $"{Tag} LEVEL[{lt}]|avail={(avail == null ? "<null>" : avail.Count.ToString())}" +
                $"|pending={Str(() => BlueprintShuffle.GetRemainingCount(lt)?.ToString() ?? "<not in chain>")}" +
                $"|{DescribeLevelList(lt)}|{DescribeLevelData(lt)}");

            if (avail != null && avail.Count > 0)
                DumpList($"  AVAIL_LEVEL[{lt}]", avail);
        }
    }

    /// <summary>
    /// What InfoDB.BlueprintsByLevel holds in THE SLOT VANILLA READS for this level, right now,
    /// after our rewrites. Indexed by difficulty position, deliberately - reading it by enum ordinal
    /// is the bug this probe found, and a verification pass that reproduced the bug's own indexing
    /// would agree with itself no matter what. The slot index is printed so the two can be compared.
    /// </summary>
    private static string DescribeLevelList(LevelType lt)
    {
        return Str(() =>
        {
            var byLevel = InfoDB.I?.BlueprintsByLevel;
            if (byLevel == null)
                return "list=<no BlueprintsByLevel>";

            var position = LevelUnlockOrder.PositionOf(lt);
            if (position == null)
                return "list=<difficulty order not loaded>";

            var idx = position.Value;
            if (idx < 0 || idx >= byLevel.Length)
                return "list=<index out of range>";

            var list = byLevel[idx];
            if (list == null)
                return "list=<null - not yet populated>";

            var parts = new System.Collections.Generic.List<string>();
            for (var j = 0; j < list.Count; j++)
                parts.Add(list[j] == null ? "<null>" : list[j].Type.ToString());

            return $"slotIdx={idx}(ordinal={(int)lt})|listCount={list.Count}|entries={string.Join(",", parts)}";
        });
    }

    /// <summary>
    /// Per-level save state. NumBlueprintDropAttempts is the field to watch: it is the only
    /// per-level counter that could make two levels holding the identical placeholder answer
    /// differently.
    /// </summary>
    private static string DescribeLevelData(LevelType lt)
    {
        return Str(() =>
        {
            var all = MetaSaveData.I?.LvlData;
            if (all == null)
                return "lvlData=<none>";

            for (var i = 0; i < all.Length; i++)
            {
                var d = all[i];
                if (d == null || d.Type != lt)
                    continue;

                return $"ngPlus={d.NGPlusLvl}|didComplete={d.DidComplete}" +
                       $"|dropAttempts={d.NumBlueprintDropAttempts}|attempts={d.NumAttempts}" +
                       $"|sawUnlocks={d.SawCompletionUnlocks}";
            }

            return "lvlData=<no entry for this level>";
        });
    }

    private static string Ints(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> arr)
    {
        if (arr == null)
            return "<null>";

        var parts = new System.Collections.Generic.List<string>();
        for (var i = 0; i < arr.Length; i++)
            parts.Add(arr[i].ToString());

        return string.Join(",", parts);
    }

    private static void DumpList(string label, Il2CppSystem.Collections.Generic.List<BuildingInfo> list)
    {
        var log = LocationHooks.Log;

        if (list == null)
        {
            log?.Msg($"{Tag} {label}|<null>");
            return;
        }

        var entries = new System.Collections.Generic.List<string>();
        var flagged = 0;

        for (var i = 0; i < list.Count; i++)
        {
            var info = list[i];
            if (info == null)
            {
                entries.Add("<null>");
                continue;
            }

            var bt = info.Type;
            var isNpch = NonPooledCharHousing.Contains(bt);
            if (isNpch)
                flagged++;

            entries.Add(isNpch ? $"*{bt}*" : bt.ToString());
        }

        var marker = flagged > 0 ? $"|NONPOOLED_CHARHOUSING_PRESENT={flagged}" : "";
        log?.Msg($"{Tag} {label}|count={list.Count}{marker}|{string.Join(",", entries)}");
    }

    /// <summary>
    /// Per-building droppability. The decisive comparison is kMoonIdol vs kCampground: if the Void
    /// Trophy placeholder reports CanDropBlueprint()==false while Campground reports true, then no
    /// amount of putting placeholders into lists can ever work, and the fix has to target this
    /// predicate (or stop suppressing, or pre-grant) instead.
    /// </summary>
    private static void DumpDroppability()
    {
        Report(BuildingType.kMoonIdol, "VOID_TROPHY_PLACEHOLDER");
        foreach (var bt in NonPooledCharHousing)
            Report(bt, "NONPOOLED_CHARHOUSING");
    }

    private static void Report(BuildingType bt, string role)
    {
        var info = FindBuildingInfo(bt);
        LocationHooks.Log?.Msg(
            $"{Tag} BLD|type={bt}|role={role}" +
            $"|hasBlueprint={Str(() => SaveMgr.I.HasBlueprint(bt).ToString())}" +
            $"|canDropBlueprint={(info == null ? "<no BuildingInfo>" : Str(() => info.CanDropBlueprint().ToString()))}" +
            $"|housingChar={Str(() => bt.GetHousingChar().ToString())}" +
            $"|btHasHousingUpgrade={Str(() => bt.HasHousingUpgrade().ToString())}" +
            $"|suppressedByMod={Str(() => SuppressibleBuildingTypes.Contains(bt).ToString())}");
    }

    private static BuildingInfo FindBuildingInfo(BuildingType bt)
    {
        if (InfoDB.I == null || InfoDB.I.Buildings == null)
            return null;

        var all = InfoDB.I.Buildings;
        for (var i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].Type == bt)
                return all[i];
        }

        return null;
    }

    /// <summary>
    /// Characters, their permanent level, and the housing-upgrade state - plus vanilla's own
    /// CharUnlockOrder, which is the one piece of per-character sequencing the mod does not own and
    /// therefore a plausible home for a "next character's housing" rule.
    /// </summary>
    private static void DumpCharacters()
    {
        var log = LocationHooks.Log;
        var meta = MetaSaveData.I;

        if (meta == null)
        {
            log?.Msg($"{Tag} CHARS|<no MetaSaveData>");
            return;
        }

        log?.Msg($"{Tag} CHARUNLOCKORDER|{Str(() => DescribeUnlockOrder(meta))}");

        var chars = meta.Chars;
        if (chars == null)
        {
            log?.Msg($"{Tag} CHARS|<null>");
            return;
        }

        log?.Msg($"{Tag} CHARS|count={chars.Length}");

        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c == null)
            {
                log?.Msg($"{Tag}   CHAR|idx={i}|<null>");
                continue;
            }

            var idx = i;
            var housing = Str(() =>
            {
                var h = InfoDB.I?.CharHousing;
                if (h == null || idx >= h.Length || h[idx] == null)
                    return "<none>";
                return h[idx].Type.ToString();
            });

            log?.Msg(
                $"{Tag}   CHAR|idx={i}|type={Str(() => c.Type.ToString())}" +
                $"|lvl={Str(() => c.Lvl.ToString())}" +
                $"|isUnlocked={Str(() => c.IsUnlocked.ToString())}" +
                $"|hasHousingUpgrade={Str(() => c.HasHousingUpgrade().ToString())}" +
                $"|shouldGainHousingUpgrade={Str(() => c.ShouldGainHousingUpgrade(c.Lvl).ToString())}" +
                $"|housing={housing}");
        }
    }

    private static string DescribeUnlockOrder(MetaSaveData meta)
    {
        var order = meta.CharUnlockOrder;
        if (order == null)
            return "<null>";

        var parts = new System.Collections.Generic.List<string>();
        for (var i = 0; i < order.Count; i++)
            parts.Add(order[i].ToString());

        return $"count={order.Count}|{string.Join(",", parts)}";
    }

    private static string Str(System.Func<string> f)
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

    /// <summary>
    /// Same guard as Str, for the vanilla queries that hand back an IL2CPP list. A throw here is a
    /// finding in itself (the query is not callable in this context), so it gets logged rather than
    /// swallowed silently.
    /// </summary>
    private static T Obj<T>(System.Func<T> f) where T : class
    {
        try
        {
            return f();
        }
        catch (System.Exception e)
        {
            LocationHooks.Log?.Msg($"{Tag} query threw <{e.GetType().Name}: {e.Message}>");
            return null;
        }
    }
}
#endif
