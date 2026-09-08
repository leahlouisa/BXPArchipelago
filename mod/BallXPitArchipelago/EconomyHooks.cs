using HarmonyLib;
using Il2Cpp;

namespace BallXPitArchipelago;

/// <summary>
/// Applies the yaml-configurable "building_cost_percent" / "land_expansion_cost_percent"
/// options (see EconomyOptions.cs) for the two costs that ARE real, patchable methods.
/// Building PLACEMENT cost (BuildingInfo.BuildCost) is deliberately NOT patched here - it's a
/// raw IL2CPP field accessor, confirmed live to be un-patchable (MelonLoader itself logs "is a
/// field accessor, it can't be patched", and a Harmony patch on it silently never fires - see
/// EconomyOptions.ApplyBuildingCostScaling for how that one's actually handled, by rewriting
/// the field directly instead). BuildingInst.GetUpgradeCost() and BaseGridMgr.GetExpansionCost()
/// ARE real computed methods (confirmed live: both fire and scale correctly), so a normal
/// Postfix works for them. Cost's own `operator *(Cost, float)` is used for the Cost-returning
/// one rather than hand-rolling per-resource multiplication, so whatever rounding vanilla's
/// own operator does is inherited rather than reimplemented. Postfix only, never suppresses -
/// a percent of 100 (the default, matching vanilla) is a no-op multiply. Deliberately does NOT
/// touch elevator upgrade gear costs - those are a different currency the Rules.py access
/// rules assume are the real vanilla amounts (see Items.py's _ELEVATOR_GATING_CHARACTER_ENUMS).
/// </summary>
[HarmonyPatch(typeof(BuildingInst), nameof(BuildingInst.GetUpgradeCost))]
internal static class BuildingUpgradeCostScalePatch
{
    private static void Postfix(ref Cost __result)
    {
        if (EconomyOptions.BuildingCostPercent == 100 || __result == null)
            return;

        __result = __result * (EconomyOptions.BuildingCostPercent / 100f);
    }
}

[HarmonyPatch(typeof(BaseGridMgr), nameof(BaseGridMgr.GetExpansionCost))]
internal static class LandExpansionCostScalePatch
{
    private static void Postfix(ref int __result)
    {
        if (EconomyOptions.LandExpansionCostPercent == 100)
            return;

        __result = (int)System.Math.Round(__result * (EconomyOptions.LandExpansionCostPercent / 100f));
    }
}
