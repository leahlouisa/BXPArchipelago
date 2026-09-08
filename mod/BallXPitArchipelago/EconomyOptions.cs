using System;
using System.Collections.Generic;
using Il2Cpp;
using Newtonsoft.Json.Linq;

namespace BallXPitArchipelago;

/// <summary>
/// Reads the yaml-configurable economy options from slot data (see the ballxpit apworld's
/// Options.py/__init__.py's fill_slot_data): how much a single Wood/Stone/Wheat/Gold filler
/// item grants (used by ItemReceiver instead of hardcoding vanilla's 50/50/50/200), and what
/// percent of vanilla cost buildings/land-expansion should charge. Land expansion and
/// building upgrade costs are applied via ordinary Harmony patches (see EconomyHooks.cs -
/// BaseGridMgr.GetExpansionCost()/BuildingInst.GetUpgradeCost() are real computed methods).
/// Building PLACEMENT cost can't be done that way, though: confirmed live that
/// BuildingInfo.BuildCost is a raw IL2CPP field accessor, not a real method - MelonLoader
/// itself logs "is a field accessor, it can't be patched" for it at patch time, and a Harmony
/// patch on it silently never fires (a placed Schoolhouse still charged the full vanilla 5
/// Wheat/2 Stone with building_cost_percent=25 set). ApplyBuildingCostScaling works around
/// this by rewriting every BuildingInfo's BuildCost field once, directly, via its own public
/// setter (no patching needed for that - we're not intercepting a call, just overwriting the
/// data once before the player can interact with any build menu) - every subsequent read of
/// BuildCost, from whatever internal path actually consults it, then sees the already-scaled
/// value with nothing further to patch. Falls back to vanilla's real values if slot data is
/// missing, malformed, or not yet loaded, rather than granting 0 or throwing - same
/// defensive pattern as ApConnection.IsDeathLinkEnabled.
/// </summary>
internal static class EconomyOptions
{
    private const int DefaultWoodStoneWheatFillerAmount = 50;
    private const int DefaultGoldFillerAmount = 200;
    private const int DefaultCostPercent = 100;

    private static bool _buildCostScalingApplied;

    internal static int WoodFillerAmount { get; private set; } = DefaultWoodStoneWheatFillerAmount;
    internal static int StoneFillerAmount { get; private set; } = DefaultWoodStoneWheatFillerAmount;
    internal static int WheatFillerAmount { get; private set; } = DefaultWoodStoneWheatFillerAmount;
    internal static int GoldFillerAmount { get; private set; } = DefaultGoldFillerAmount;

    /// <summary>Applied once to every BuildingInfo.BuildCost - see ApplyBuildingCostScaling.</summary>
    internal static int BuildingCostPercent { get; private set; } = DefaultCostPercent;

    /// <summary>Applies to BaseGridMgr.GetExpansionCost() - see EconomyHooks.cs.</summary>
    internal static int LandExpansionCostPercent { get; private set; } = DefaultCostPercent;

    internal static void ApplyFromSlotData(Dictionary<string, object> slotData)
    {
        if (slotData == null)
            return;

        if (slotData.TryGetValue("filler_amounts", out var rawAmounts) && rawAmounts is JObject amounts)
        {
            WoodFillerAmount = ReadInt(amounts, "Wood", DefaultWoodStoneWheatFillerAmount);
            StoneFillerAmount = ReadInt(amounts, "Stone", DefaultWoodStoneWheatFillerAmount);
            WheatFillerAmount = ReadInt(amounts, "Wheat", DefaultWoodStoneWheatFillerAmount);
            GoldFillerAmount = ReadInt(amounts, "Gold", DefaultGoldFillerAmount);
        }

        BuildingCostPercent = ReadInt(slotData, "building_cost_percent", DefaultCostPercent);
        LandExpansionCostPercent = ReadInt(slotData, "land_expansion_cost_percent", DefaultCostPercent);

        LocationHooks.Log?.Msg(
            $"[EconomyOptions] filler amounts: Wood={WoodFillerAmount}, Stone={StoneFillerAmount}, " +
            $"Wheat={WheatFillerAmount}, Gold={GoldFillerAmount}; building_cost_percent={BuildingCostPercent}, " +
            $"land_expansion_cost_percent={LandExpansionCostPercent}");
    }

    /// <summary>
    /// Call periodically from Mod.OnUpdate() once InfoDB is ready (same pattern as
    /// BlueprintShuffle's InfoDB-dependent applies) - rewrites every BuildingInfo.BuildCost
    /// once via its normal setter. Guarded by _buildCostScalingApplied so a percent of 100
    /// (the default) never touches InfoDB at all, and a non-default percent only ever gets
    /// applied once per process (re-applying on a later call would compound the discount,
    /// since we're overwriting the field itself rather than replacing a return value).
    /// </summary>
    internal static void ApplyBuildingCostScaling()
    {
        if (_buildCostScalingApplied || BuildingCostPercent == 100 || InfoDB.I == null)
            return;

        var buildings = InfoDB.I.Buildings;
        if (buildings == null)
            return;

        var scale = BuildingCostPercent / 100f;
        var count = 0;
        foreach (var info in buildings)
        {
            if (info == null || info.BuildCost == null)
                continue;

            info.BuildCost = info.BuildCost * scale;
            count++;
        }

        _buildCostScalingApplied = true;
        LocationHooks.Log?.Msg(
            $"[EconomyOptions] Rewrote BuildCost directly for {count} buildings to {BuildingCostPercent}% " +
            "(BuildingInfo.BuildCost is a raw field accessor, not Harmony-patchable - see class doc).");
    }

    private static int ReadInt(JObject obj, string key, int fallback)
    {
        if (!obj.TryGetValue(key, out var token))
            return fallback;

        try { return token.ToObject<int>(); }
        catch { return fallback; }
    }

    private static int ReadInt(Dictionary<string, object> dict, string key, int fallback)
    {
        if (!dict.TryGetValue(key, out var value))
            return fallback;

        try { return Convert.ToInt32(value); }
        catch { return fallback; }
    }
}
