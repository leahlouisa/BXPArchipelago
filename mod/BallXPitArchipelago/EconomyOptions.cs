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
/// place) is handled by ApplyBuildingCostScaling below - direct data mutation of
/// BuildingInfo.BuildCost, not a Harmony patch on anything. See that method's doc comment for
/// why this turned out to be safe after all, despite a whole session's investigation initially
/// blaming it for a freeze that was actually caused by something else entirely (Harmony patches
/// on SaveMgr's resource-mutation methods - see EconomyHooks.cs's class doc comment and git
/// history for the full story). The one hard rule that investigation left behind: NEVER
/// Harmony-patch SaveMgr.SpendResources/SpendGold/AddResources/AddMetaGold, for any reason,
/// ever - direct field/property writes (like this class does) are fine.
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
    /// Scales an EXISTING Cost's Num array in place, never constructing a replacement Cost or
    /// reassigning the field/property that points at it - avoids any risk of some other part of
    /// the game holding a stale reference to a Cost object we've since abandoned. Used both for
    /// upgrade cost (EconomyHooks.cs's BuildingUpgradeCostScalePatch, on a fresh throwaway Cost
    /// each call) and placement cost (ApplyBuildingCostScaling below, on the real shared
    /// BuildingInfo.BuildCost instance).
    /// </summary>
    internal static void ScaleCostInPlace(Cost cost, float scale)
    {
        var num = cost.Num;
        for (var i = 0; i < num.Length; i++)
            num[i] = ScaleAmount(num[i], scale);

        cost.Num = num;
    }

    /// <summary>
    /// Rewrites every BuildingInfo.BuildCost in place (via ScaleCostInPlace, never replacing the
    /// object) to building_cost_percent% of its real vanilla amount. Call periodically from
    /// Mod.OnUpdate() once InfoDB is ready (same pattern as BlueprintShuffle's InfoDB-dependent
    /// applies) - guarded by _buildCostScalingApplied so a percent of 100 never touches InfoDB
    /// at all, and a non-default percent only ever gets applied once per process (re-applying
    /// would compound the discount, since this mutates the field's own data rather than
    /// replacing a return value).
    ///
    /// This is pure data mutation - BuildingInfo.BuildCost is a raw IL2CPP field accessor with
    /// no real backing method (confirmed live: MelonLoader itself logs "is a field accessor, it
    /// can't be patched" for it), so there's nothing to Harmony-patch here, and nothing about
    /// this touches SaveMgr.SpendResources/SpendGold/AddResources/AddMetaGold - vanilla's own
    /// purchase pipeline runs completely unmodified against this pre-scaled data, exactly as it
    /// always has for every building's cost, scaled or not. An entire session's investigation
    /// into a building-placement freeze wrongly blamed this exact mechanism (both this in-place
    /// version and an earlier replace-based one); the freeze was actually caused by unrelated
    /// Harmony patches on SaveMgr's resource-mutation methods (see EconomyHooks.cs and git
    /// history) that happened to be introduced around the same time - once those were found and
    /// removed, reviving this turned out to work fine.
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

            ScaleCostInPlace(info.BuildCost, scale);
            count++;
        }

        _buildCostScalingApplied = true;
        LocationHooks.Log?.Msg(
            $"[EconomyOptions] Rewrote BuildCost in place for {count} buildings to " +
            $"{BuildingCostPercent}% of vanilla.");
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
