using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace BallXPitArchipelago;

/// <summary>
/// Applies the yaml-configurable "building_cost_percent" / "land_expansion_cost_percent"
/// options (see EconomyOptions.cs). Three different mechanisms, one per cost type, because each
/// turned out to need a different approach after live testing:
///
/// - BuildingInst.GetUpgradeCost() and BaseGridMgr.GetExpansionCost() are real computed methods
///   that hand back a fresh, throwaway Cost/int each call - a normal Postfix mutating that
///   result in place (BuildingUpgradeCostScalePatch) or the returned int (
///   LandExpansionCostScalePatch) works cleanly and is confirmed live, many times over, to
///   never crash.
/// - Building PLACEMENT cost (BuildingInfo.BuildCost) is a raw IL2CPP field accessor (confirmed
///   live, including by MelonLoader's own "is a field accessor, it can't be patched" log line)
///   pointing at the SAME shared Cost instance for every building of that type - rewriting that
///   shared instance's data (tried both by replacing it and by mutating its Num array in place)
///   reliably froze the game the first time a real purchase tried to spend it, unconditionally,
///   down to a single-resource 8 Gold purchase on a brand new save (the v0.2.2 hotfix disabled
///   this entirely - see git history). BuildingPlacementCostRefundPatch below is the redesign:
///   BuildingInfo.BuildCost is never touched at all anymore, only read (for its real vanilla
///   amount), and vanilla's own BuildBuilding/Cost.Spend()/SaveMgr.SpendResources pipeline runs
///   completely untouched against that unmodified data - exactly the configuration proven safe
///   throughout the whole investigation. The discount is applied afterward as a refund via
///   SaveMgr.AddResources instead.
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

/// <summary>
/// Building PLACEMENT cost discount - see this file's class-level doc comment for why this
/// pays-full-then-refunds rather than discounting the charge upfront. BaseGridMgr.BuildBuilding
/// (float x, float y, int rot) is the real method fired once per successful placement (a plain
/// public method, not a field accessor - confirmed patchable).
///
/// Prefix reads the building-about-to-be-placed's real vanilla BuildCost via the placement
/// preview (BaseGridMgr.GetPlacePreview().Inst.GetInfo().BuildCost - the preview BuildingObj
/// already has a real BuildingInst attached, just flagged isPreview, per BuildingObj.Init(inst,
/// isPreview)) and snapshots its Num array into a plain C# int[] __state - deliberately a copy,
/// not a reference to the live Cost object, in case Spend() (which runs for real, untouched,
/// between the Prefix and Postfix) mutates the shared BuildCost instance itself as a side effect
/// of spending it; a snapshot can't be affected by that either way. Postfix then refunds the
/// vanilla-minus-scaled difference per resource via SaveMgr.AddResources, skipping any resource
/// that isn't actually priced (amount 0) or that rounds to no discount. If BuildBuilding throws
/// or freezes, Harmony never runs the Postfix (same as vanilla failing on its own), so nothing
/// gets refunded for a purchase that didn't actually happen.
///
/// Num's index order is assumed to match ResourceType's declared enum order (kGold=0, kWheat=1,
/// kWood=2, kStone=3) - inferred from Cost's own `Cost(int gold, int wheat, int wood, int
/// stone)` constructor using that exact parameter order, not yet confirmed live. The Postfix's
/// log line prints resource name alongside amount specifically so the first real test confirms
/// or refutes this immediately.
/// </summary>
[HarmonyPatch(typeof(BaseGridMgr), nameof(BaseGridMgr.BuildBuilding))]
internal static class BuildingPlacementCostRefundPatch
{
    private static void Prefix(BaseGridMgr __instance, out int[] __state)
    {
        __state = null;
        if (EconomyOptions.BuildingCostPercent == 100)
            return;

        var num = __instance.GetPlacePreview()?.Inst?.GetInfo()?.BuildCost?.Num;
        if (num == null)
        {
            LocationHooks.Log?.Warning(
                "[EconomyOptions] BuildBuilding fired but the placement preview's BuildCost " +
                "couldn't be read (preview/Inst/Info/BuildCost was null) - no placement " +
                "discount refund this time.");
            return;
        }

        var snapshot = new int[num.Length];
        for (var i = 0; i < num.Length; i++)
            snapshot[i] = num[i];

        __state = snapshot;
    }

    private static void Postfix(int[] __state)
    {
        if (__state == null)
            return;

        var scale = EconomyOptions.BuildingCostPercent / 100f;
        var refundedParts = new List<string>();

        for (var i = 0; i < __state.Length; i++)
        {
            var vanillaAmt = __state[i];
            if (vanillaAmt <= 0)
                continue;

            var refund = vanillaAmt - EconomyOptions.ScaleAmount(vanillaAmt, scale);
            if (refund <= 0)
                continue;

            var rt = (ResourceType)i;
            SaveMgr.I?.AddResources(rt, refund, false, false);
            refundedParts.Add($"{refund} {rt}");
        }

        if (refundedParts.Count > 0)
        {
            LocationHooks.Log?.Msg(
                $"[EconomyOptions] building_cost_percent={EconomyOptions.BuildingCostPercent}: " +
                $"placed for real vanilla cost, refunded {string.Join(", ", refundedParts)}.");
        }
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
