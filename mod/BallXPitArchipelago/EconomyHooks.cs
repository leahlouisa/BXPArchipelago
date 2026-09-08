using HarmonyLib;
using Il2Cpp;

namespace BallXPitArchipelago;

/// <summary>
/// Applies the yaml-configurable "building_cost_percent" / "land_expansion_cost_percent"
/// options (see EconomyOptions.cs) for the two costs that ARE real, patchable methods, and
/// both are confirmed working correctly. Building PLACEMENT cost (BuildingInfo.BuildCost) is
/// deliberately NOT patched here - it's a raw IL2CPP field accessor, confirmed live to be
/// un-patchable, and as of the v0.2.2 hotfix its data-rewrite workaround
/// (EconomyOptions.ApplyBuildingCostScaling) is disabled entirely pending a redesign - see that
/// method's doc comment for why. BuildingInst.GetUpgradeCost() and BaseGridMgr.GetExpansionCost()
/// ARE real computed methods (confirmed live: both fire and scale correctly, never crashed), so
/// a normal Postfix works for them. Postfix only, never suppresses - a percent of 100 (the
/// default, matching vanilla) is a no-op. Deliberately does NOT touch elevator upgrade gear
/// costs - those are a different currency the Rules.py access rules assume are the real
/// vanilla amounts (see Items.py's _ELEVATOR_GATING_CHARACTER_ENUMS).
///
/// GetUpgradeCost's Postfix uses EconomyOptions.ScaleCostInPlace, mutating the Cost object
/// GetUpgradeCost already handed back rather than replacing it with a different one - see that
/// method's doc comment for why identity matters here: an earlier version of
/// ApplyBuildingCostScaling replaced BuildingInfo.BuildCost with a newly-constructed Cost
/// instance, which froze the game 100% of the time on a completely unrelated-looking call
/// (SaveMgr.SpendResources with a normal nonzero amount) for any building whose cost had been
/// replaced that way - confirmed live only after the user pointed out these exact buildings
/// had been purchased successfully many times before v0.2.1, which is what correctly redirected
/// the investigation away from a wrong "zero amount"/"pre-existing vanilla bug" theory this
/// class's patches were originally (and still are, for a real but separate issue - see
/// ZeroAmountResourceGuardPatch below) built around.
/// </summary>
[HarmonyPatch(typeof(BuildingInst), nameof(BuildingInst.GetUpgradeCost))]
internal static class BuildingUpgradeCostScalePatch
{
    private static void Postfix(Cost __result)
    {
        if (EconomyOptions.BuildingCostPercent == 100 || __result == null)
            return;

        EconomyOptions.ScaleCostInPlace(__result, EconomyOptions.BuildingCostPercent / 100f);
    }
}

[HarmonyPatch(typeof(BaseGridMgr), nameof(BaseGridMgr.GetExpansionCost))]
internal static class LandExpansionCostScalePatch
{
    private static void Postfix(ref int __result)
    {
        if (EconomyOptions.LandExpansionCostPercent == 100 || __result <= 0)
            return;

        var scaled = (int)System.Math.Round(__result * (EconomyOptions.LandExpansionCostPercent / 100f));
        __result = System.Math.Max(1, scaled);
    }
}

/// <summary>
/// Real fix for the confirmed-live freeze: SaveMgr.SpendResources(rt, 0) never returns
/// (Prefix logs "entering", no matching Postfix ever fires, no exception, nothing - the same
/// silent-freeze signature as the earlier network-blocking-call bug, just a different native
/// call this time). Confirmed this is unconditional, not tied to negative gold or to our own
/// scaling: it reproduced identically buying the Watch Tower (genuinely 0 Gold cost in
/// UNMODIFIED vanilla data) at both -178 and +89 real gold. Spending/adding exactly 0 of a
/// resource is always semantically a no-op anyway - nothing observable should happen, no UI
/// should animate - so unconditionally skipping the real call whenever the amount is exactly 0
/// changes nothing a player could ever notice while removing the freeze entirely. Applied to
/// all four SaveMgr resource-mutation methods (not just the one confirmed to freeze) since
/// they're structurally identical thin wrappers and most likely share whatever native code
/// path is actually broken for a zero delta - cheap, safe insurance either way. Deliberately
/// NOT gated behind building_cost_percent - this can happen on a 100%/vanilla-cost seed too,
/// for any building that happens to cost 0 of some resource in real unmodified game data.
/// </summary>
[HarmonyPatch(typeof(SaveMgr), nameof(SaveMgr.SpendResources))]
internal static class ZeroAmountSpendResourcesGuardPatch
{
    private static bool Prefix(int amt) => amt != 0;
}

[HarmonyPatch(typeof(SaveMgr), nameof(SaveMgr.AddResources))]
internal static class ZeroAmountAddResourcesGuardPatch
{
    private static bool Prefix(int amt) => amt != 0;
}

[HarmonyPatch(typeof(SaveMgr), nameof(SaveMgr.SpendGold))]
internal static class ZeroAmountSpendGoldGuardPatch
{
    private static bool Prefix(int amt) => amt != 0;
}

[HarmonyPatch(typeof(SaveMgr), nameof(SaveMgr.AddMetaGold))]
internal static class ZeroAmountAddMetaGoldGuardPatch
{
    private static bool Prefix(int amt) => amt != 0;
}
