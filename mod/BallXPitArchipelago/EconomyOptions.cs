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
/// Building PLACEMENT cost (building_cost_percent's effect on what a new building costs to
/// place, as opposed to upgrade) is handled entirely in EconomyHooks.cs's
/// BuildingPlacementCostRefundPatch - see that class's doc comment. This class deliberately
/// never touches BuildingInfo.BuildCost (a raw IL2CPP field accessor - confirmed live,
/// including by MelonLoader's own "is a field accessor, it can't be patched" log line at patch
/// time, that rewriting its data reliably freezes the game the first time a real purchase tries
/// to spend the rewritten cost, unconditionally, down to a single-resource 8 Gold purchase on a
/// brand new save - see git history around the v0.2.2 hotfix for the full investigation).
///
/// Falls back to vanilla's real values if slot data is missing, malformed, or not yet loaded,
/// rather than granting 0 or throwing - same defensive pattern as ApConnection.IsDeathLinkEnabled.
/// </summary>
internal static class EconomyOptions
{
    private const int DefaultWoodStoneWheatFillerAmount = 50;
    private const int DefaultGoldFillerAmount = 200;
    private const int DefaultCostPercent = 100;

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
    /// Scales a single resource amount to `scale` percent of its vanilla value, flooring any
    /// originally-positive amount back up to at least 1 rather than letting it round down to 0 -
    /// confirmed live that an explicit 0 for a resource a purchase actually costs freezes
    /// SaveMgr.SpendResources (vanilla's own data never produces that combination, so nothing in
    /// its native code was ever exercised against it before this option existed). Shared by
    /// ScaleCostInPlace (upgrade cost) and BuildingPlacementCostRefundPatch (placement cost
    /// refund, EconomyHooks.cs) so both use identical rounding/flooring behavior.
    /// </summary>
    internal static int ScaleAmount(int vanillaAmt, float scale)
    {
        if (vanillaAmt <= 0)
            return vanillaAmt;

        var scaled = (int)System.Math.Round(vanillaAmt * scale);
        return scaled > 0 ? scaled : 1;
    }

    /// <summary>
    /// Scales an EXISTING Cost's Num array in place - never constructs a replacement Cost and
    /// never reassigns BuildingInfo.BuildCost to a different object. See this class's doc
    /// comment for why that distinction turned out to matter: replacing BuildCost with a
    /// different Cost instance froze the game on a completely unrelated-looking call
    /// (SaveMgr.SpendResources with a normal nonzero amount) for any building whose cost had
    /// been replaced this way, 100% of the time, confirmed live.
    /// </summary>
    internal static void ScaleCostInPlace(Cost cost, float scale)
    {
        var num = cost.Num;
        for (var i = 0; i < num.Length; i++)
            num[i] = ScaleAmount(num[i], scale);

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
