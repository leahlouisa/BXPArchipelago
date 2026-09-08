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
/// BaseGridMgr.GetExpansionCost()/BuildingInst.GetUpgradeCost() are real computed methods) and
/// both are confirmed working correctly.
///
/// Building PLACEMENT cost (building_cost_percent's effect on BuildingInfo.BuildCost) is
/// CURRENTLY DISABLED as of the v0.2.2 hotfix - see ApplyBuildingCostScaling's doc comment.
/// BuildingInfo.BuildCost is a raw IL2CPP field accessor, not a real method (MelonLoader itself
/// logs "is a field accessor, it can't be patched" at patch time), so it can't be intercepted
/// via Harmony - the only way to scale it is to rewrite the underlying data directly. Both ways
/// of doing that were tried (replacing BuildingInfo.BuildCost with a newly-scaled Cost object,
/// and mutating the existing Cost's Num array in place instead) and BOTH reliably froze the
/// game the first time a real purchase tried to spend the scaled cost - confirmed live down to
/// a single-resource 8 Gold purchase on a brand new save during the tutorial's forced first
/// building placement, so this isn't a rare edge case, it's unconditional. A proper fix needs
/// to bypass vanilla's Cost.Spend()/SaveMgr.SpendResources pipeline for purchases entirely
/// rather than trying to make it tolerate scaled BuildCost data - not yet implemented.
///
/// Falls back to vanilla's real values if slot data is missing, malformed, or not yet loaded,
/// rather than granting 0 or throwing - same defensive pattern as ApConnection.IsDeathLinkEnabled.
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
    /// HOTFIX (v0.2.2): temporarily disabled, unconditionally. Confirmed live that scaling
    /// BuildingInfo.BuildCost - in EVERY variant tried (replacing the Cost object, mutating it
    /// in place, with or without the zero-amount guards, with or without diagnostic patches on
    /// the call chain) - reliably freezes the game the first time a REAL (nonzero) resource
    /// spend actually happens, even for a single-resource 8 Gold purchase on a completely
    /// fresh save during the tutorial's forced first building placement. That's not a rare edge
    /// case, it's every purchase, unconditionally, once BuildingInfo.BuildCost has been
    /// touched at all - so shipping this half-working was worse than shipping it off. Until a
    /// proper redesign lands (bypassing Cost.Spend()/SaveMgr.SpendResources entirely for
    /// purchases rather than trying to make vanilla's own pipeline tolerate scaled BuildCost
    /// data), building_cost_percent has no effect on PLACEMENT cost - buildings cost their real
    /// vanilla amount to place regardless of what this option is set to. BuildingInst.
    /// GetUpgradeCost() scaling (EconomyHooks.cs) is UNAFFECTED and still works - that's a
    /// completely different code path (a real method returning a fresh, throwaway Cost each
    /// call, confirmed live many times over never to have crashed) - so upgrade costs still
    /// scale normally. Land expansion (BaseGridMgr.GetExpansionCost, a real method returning a
    /// plain int, also never crashed) is unaffected too.
    /// </summary>
    internal static void ApplyBuildingCostScaling()
    {
        if (_buildCostScalingApplied || BuildingCostPercent == 100)
            return;

        _buildCostScalingApplied = true;
        LocationHooks.Log?.Msg(
            $"[EconomyOptions] building_cost_percent={BuildingCostPercent} but BuildCost scaling is " +
            "TEMPORARILY DISABLED (v0.2.2 hotfix) - confirmed live it freezes the game on any real " +
            "purchase, regardless of implementation. Buildings will cost their real vanilla amount to " +
            "place. Upgrade cost and land expansion scaling are unaffected and still apply normally.");
    }

    /// <summary>
    /// Scales an EXISTING Cost's Num array in place - never constructs a replacement Cost and
    /// never reassigns BuildingInfo.BuildCost to a different object. See this class's doc
    /// comment for why that distinction turned out to matter: replacing BuildCost with a
    /// different Cost instance froze the game on a completely unrelated-looking call
    /// (SaveMgr.SpendResources with a normal nonzero amount) for any building whose cost had
    /// been replaced this way, 100% of the time, confirmed live. Also floors any resource that
    /// was originally nonzero back up to at least 1 if scaling rounds it down to 0 - separately
    /// confirmed live that an explicit 0 for a resource a purchase actually costs freezes
    /// SaveMgr.SpendResources too (vanilla's own data never produces that combination, so nothing
    /// in its native code was ever exercised against it before this option existed).
    /// </summary>
    internal static void ScaleCostInPlace(Cost cost, float scale)
    {
        var num = cost.Num;
        for (var i = 0; i < num.Length; i++)
        {
            if (num[i] <= 0)
                continue;

            var scaled = (int)System.Math.Round(num[i] * scale);
            num[i] = scaled > 0 ? scaled : 1;
        }

        cost.Num = num;
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
