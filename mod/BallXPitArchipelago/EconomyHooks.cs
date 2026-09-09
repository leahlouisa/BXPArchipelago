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
/// Building PLACEMENT cost is handled separately, in EconomyOptions.ApplyBuildingCostScaling -
/// direct mutation of BuildingInfo.BuildCost's data, not a Harmony patch at all (it's a raw
/// IL2CPP field accessor with nothing to patch). An entire session's investigation wrongly
/// blamed this mechanism for a building-placement freeze; the real cause turned out to be
/// unrelated Harmony patches on SaveMgr.SpendResources/SpendGold/AddResources/AddMetaGold
/// (added around the same time, for an unrelated zero-gold edge case) - ANY patch on those four
/// methods breaks them when called reentrant from inside Cost.Spend()'s own native execution,
/// regardless of what the patch does. Once those were found and permanently removed (see git
/// history), direct BuildCost mutation turned out to be fine after all. Hard rule going forward:
/// never Harmony-patch SaveMgr.SpendResources/SpendGold/AddResources/AddMetaGold, for any
/// reason, ever - not even for logging.
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

/// <summary>
/// Scales GetExpansionCost()'s displayed/queried value, and separately stashes the real
/// vanilla amount for LandExpansionCostRefundPatch below to use. Both patches exist because of
/// a real bug found live: BaseGridMgr.ConfirmExpansion() (the actual purchase) does NOT charge
/// whatever GetExpansionCost() last returned - it independently computes/charges the real
/// vanilla amount regardless of this Postfix, so scaling GetExpansionCost() alone only affects
/// what's DISPLAYED before the purchase, not what's actually charged. Confirmed live: with
/// land_expansion_cost_percent=25, expansions displayed as 50/75 Gold but the player was
/// actually charged the real 200/300 Gold - going 75 Gold negative on a purchase they believed
/// (correctly, per the UI) they could afford. LastRealVanillaCost records the pre-scaling value
/// so the refund patch can recover the true vanilla amount despite this Postfix having already
/// overwritten it for every other caller.
/// </summary>
[HarmonyPatch(typeof(BaseGridMgr), nameof(BaseGridMgr.GetExpansionCost))]
internal static class LandExpansionCostScalePatch
{
    internal static int LastRealVanillaCost { get; private set; }

    private static void Postfix(ref int __result)
    {
        if (__result > 0)
            LastRealVanillaCost = __result;

        if (EconomyOptions.LandExpansionCostPercent == 100 || __result <= 0)
            return;

        __result = EconomyOptions.ScaleAmount(__result, EconomyOptions.LandExpansionCostPercent / 100f);
    }
}

/// <summary>
/// Real fix for the display/charge mismatch described above: refunds the discounted difference
/// after ConfirmExpansion() charges the player the full real vanilla amount (confirmed live -
/// see LandExpansionCostScalePatch's doc comment). Relies on GetExpansionCost() having been
/// queried at least once (to display the price) before the player could click confirm, which
/// normal UI flow guarantees - if that assumption ever fails, LastRealVanillaCost stays 0 or
/// stale and this simply refunds nothing rather than refunding the wrong amount. Uses
/// SaveMgr.AddMetaGold, the same call already proven safe for every other gold grant in this
/// mod - land expansion only ever costs Gold, never the other three resources.
/// </summary>
[HarmonyPatch(typeof(BaseGridMgr), nameof(BaseGridMgr.ConfirmExpansion))]
internal static class LandExpansionCostRefundPatch
{
    private static void Postfix()
    {
        if (EconomyOptions.LandExpansionCostPercent == 100)
            return;

        var vanillaCost = LandExpansionCostScalePatch.LastRealVanillaCost;
        if (vanillaCost <= 0)
            return;

        var scaledCost = EconomyOptions.ScaleAmount(vanillaCost, EconomyOptions.LandExpansionCostPercent / 100f);
        var refund = vanillaCost - scaledCost;
        if (refund <= 0)
            return;

        SaveMgr.I?.AddMetaGold(refund);
        LocationHooks.Log?.Msg(
            $"[EconomyOptions] land_expansion_cost_percent={EconomyOptions.LandExpansionCostPercent}: " +
            $"charged real vanilla cost {vanillaCost} Gold, refunded {refund} Gold.");
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
