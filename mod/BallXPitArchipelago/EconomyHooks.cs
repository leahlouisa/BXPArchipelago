using HarmonyLib;
using Il2Cpp;

namespace BallXPitArchipelago;

/// <summary>
/// Applies the yaml-configurable "building_cost_percent" / "land_expansion_cost_percent"
/// options (see EconomyOptions.cs) to the two cost types that are real, computed methods
/// returning a fresh, throwaway Cost/int each call: BuildingInst.GetUpgradeCost() and
/// BaseGridMgr.GetExpansionCost(). A normal Postfix mutating that throwaway result in place
/// (BuildingUpgradeCostScalePatch) or the returned int (LandExpansionCostScalePatch) works
/// cleanly and is confirmed live, many times over, to never crash.
///
/// Building PLACEMENT cost is DELIBERATELY NOT scaled here (as of the emergency hotfix that
/// removed BuildingPlacementCostRefundPatch - see git history). Two independent approaches were
/// tried and both caused real, confirmed-live freezes:
///   1. Rewriting BuildingInfo.BuildCost's data directly (replacing or mutating-in-place the
///      shared Cost instance every building of that type points at) - froze on the very next
///      real purchase, unconditionally.
///   2. A pay-full-then-refund-the-difference design that never touched BuildingInfo.BuildCost
///      at all - ALSO froze on a real placement (a "boulder"), for reasons not yet root-caused.
///      Notably, a related affordability-gate patch in this same effort (since removed) turned
///      out to freeze for a clearly understood reason: it called Cost.CanAfford() from inside
///      its own Harmony Prefix patching that exact method - a method calling back into itself
///      through Harmony while its own wrapper is still on the stack. That specific bug is fixed,
///      but the refund patch itself still froze even after removing it, so something else about
///      patching BaseGridMgr.BuildBuilding (or reading the placement preview from inside it) is
///      also unsafe in a way not yet understood. Until that's properly diagnosed, placement cost
///      stays 100% vanilla regardless of building_cost_percent - only upgrade and land expansion
///      cost scale.
///
/// Deliberately does NOT touch elevator upgrade gear costs - those are a different currency the
/// Rules.py access rules assume are the real vanilla amounts (see Items.py's
/// _ELEVATOR_GATING_CHARACTER_ENUMS).
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


// REMOVED (2026-09-08, confirmed live): this used to hold BuildItemCostDisplayPatch (Postfix
// on BuildItem.InitInternal) and BuildingPlacementAffordabilityPatch (Prefix on Cost.CanAfford),
// meant to show/gate on the discounted price before purchase. BuildingPlacementAffordabilityPatch
// caused a real crash: its own Prefix body called `discounted.CanAfford()`, which re-enters the
// very method Harmony is patching WHILE the outer call's Prefix is still on the stack - the same
// reentrancy hazard that made the SaveMgr.SpendResources guards freeze the game (see the removed-
// code note below), just self-inflicted this time by calling into our own patched method instead
// of a leaf SaveMgr one. Confirmed live: removing the four SpendResources/etc guards alone fixed
// wheat/wood/Sheriff's Office placement, but a discounted "boulder" purchase still froze with
// these two patches present; BuildItemCostDisplayPatch was pulled alongside it out of caution
// (same "calls Cost.CanAfford() from inside a build-menu hook" shape, untested in isolation, and
// wasn't visibly working anyway - the tutorial's build menu never showed a discounted number).
//
// Net effect until these are redesigned: BuildingPlacementCostRefundPatch below still discounts
// building placement for real (pay vanilla, refund the difference), but the build menu displays
// and gates on the full vanilla cost - a player needs the full vanilla amount in hand to select
// and place a discounted building, even though they only end up paying the discounted amount.
// A real fix needs an affordability check that never calls Cost.CanAfford() reentrantly - e.g.
// comparing SaveMgr.I.GetNumResources(rt) against the scaled amount directly in C#, since
// GetNumResources is an unpatched leaf call and calling it from inside another patched method's
// Prefix has never caused a problem anywhere else in this codebase.

// REMOVED (2026-09-08, confirmed live): this used to hold four Prefix guard patches on
// SaveMgr.SpendResources/AddResources/SpendGold/AddMetaGold (`Prefix(int amt) => amt != 0`),
// added mid-session as the fix for a genuine zero-gold Watch Tower freeze. They were the actual
// cause of every placement crash investigated all session, including ones long blamed on
// building-cost scaling: merely having ANY Harmony patch on SaveMgr.SpendResources - regardless
// of what it does - breaks it when it's invoked reentrant from inside Cost.Spend()'s own native
// call, causing an unconditional silent freeze on every real purchase. Confirmed by isolating
// variables one at a time on a real save: pure vanilla (no mod) placed buildings fine; v0.2.2
// with these guards (Debug AND Release, i.e. with and without unrelated debug logging) froze on
// the very first placement; removing just these four patches (nothing else changed) fixed it
// completely. The original zero-gold edge case they were meant to fix (SpendResources(rt, 0)
// never returning, hit via the Watch Tower's genuine 0 Gold vanilla cost) is DELIBERATELY LEFT
// UNFIXED for now rather than re-attempting a patch on these same methods - a real fix needs a
// different shape entirely (e.g. a Cost.Spend() Prefix that calls SaveMgr.I.SpendResources
// itself for only the nonzero resources, bypassing the real Spend() rather than patching its
// leaf calls), and that hasn't been built or tested yet.
