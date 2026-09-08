#if DEBUG
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace BallXPitArchipelago;

/// <summary>
/// TEMPORARY investigation tooling, not part of the shipped mod - guarded behind #if DEBUG so
/// it never compiles into a Release build (and therefore never ships to players), regardless
/// of whether anyone remembers to strip the file before packaging a release. Remove once the
/// already-leveled-CharHousing freeze is root-caused. Entry/exit logging (Prefix+Postfix,
/// never suppresses) on the state-transition/level-up methods most likely to be where a
/// character's existing meta level gets retroactively applied to freshly-built housing. In
/// vanilla this catch-up can never involve more than one level (housing always exists before
/// a character is playable at all), but this mod lets a character reach a high level entirely
/// before their housing blueprint is ever placed (see LocationHooks.cs -
/// UnlockCharLocationPatch), so construction-complete may need to apply several levels' worth
/// of housing upgrade at once - a path vanilla itself never exercises. Since IL2CPP compiles
/// method bodies to native code, ilspycmd can only show signatures, not what's inside - the
/// last "entering" line logged with no matching return is the freeze point. Trimmed down from
/// a wider first pass (dropped GetSpeedImprovementAmtInRange/IsUpgradedHousingInRange/
/// HasHousingUpgrade/BuildingMgr.CalculateBonuses) since those are plausible per-tick/per-
/// harvest hot paths that would bloat the log over a long play session without adding much
/// signal - the remaining set is construction/level-up-triggered only.
/// </summary>
internal static class DebugHooks
{
    private static MelonLogger.Instance Log => LocationHooks.Log;
}

[HarmonyPatch(typeof(BuildingInst), nameof(BuildingInst.SetState))]
internal static class Debug_BuildingInst_SetState
{
    private static void Prefix(BuildingInst __instance, BuildingState bs, bool force)
    {
        LocationHooks.Log?.Msg($"[DBG] entering BuildingInst.SetState(type={__instance?.Type}, state={bs}, force={force})");
    }

    private static void Postfix(BuildingInst __instance, BuildingState bs, bool force)
    {
        LocationHooks.Log?.Msg($"[DBG] returned BuildingInst.SetState(type={__instance?.Type}, state={bs}, force={force})");
    }
}

[HarmonyPatch(typeof(BuildingObj), nameof(BuildingObj.DisableScaffold))]
internal static class Debug_BuildingObj_DisableScaffold
{
    private static void Prefix(bool animateExit)
    {
        LocationHooks.Log?.Msg($"[DBG] entering BuildingObj.DisableScaffold(animateExit={animateExit})");
    }

    private static void Postfix(bool animateExit)
    {
        LocationHooks.Log?.Msg($"[DBG] returned BuildingObj.DisableScaffold(animateExit={animateExit})");
    }
}

[HarmonyPatch(typeof(BuildingObj), nameof(BuildingObj.InitScaffold))]
internal static class Debug_BuildingObj_InitScaffold
{
    private static void Prefix(bool animateEntry)
    {
        LocationHooks.Log?.Msg($"[DBG] entering BuildingObj.InitScaffold(animateEntry={animateEntry})");
    }

    private static void Postfix(bool animateEntry)
    {
        LocationHooks.Log?.Msg($"[DBG] returned BuildingObj.InitScaffold(animateEntry={animateEntry})");
    }
}

[HarmonyPatch(typeof(BuildingInst), nameof(BuildingInst.AddUpgradePts))]
internal static class Debug_BuildingInst_AddUpgradePts
{
    private static void Prefix(BuildingInst __instance, int pts)
    {
        LocationHooks.Log?.Msg($"[DBG] entering BuildingInst.AddUpgradePts(type={__instance?.Type}, pts={pts}, curUpgradeLvl={__instance?.UpgradeLvl})");
    }

    private static void Postfix(BuildingInst __instance, int pts)
    {
        LocationHooks.Log?.Msg($"[DBG] returned BuildingInst.AddUpgradePts(type={__instance?.Type}, pts={pts}, newUpgradeLvl={__instance?.UpgradeLvl})");
    }
}

[HarmonyPatch(typeof(CharMetaInst), nameof(CharMetaInst.ShouldGainHousingUpgrade))]
internal static class Debug_CharMetaInst_ShouldGainHousingUpgrade
{
    private static void Prefix(int lvl)
    {
        LocationHooks.Log?.Msg($"[DBG] entering CharMetaInst.ShouldGainHousingUpgrade(lvl={lvl})");
    }

    private static void Postfix(int lvl, bool __result)
    {
        LocationHooks.Log?.Msg($"[DBG] returned CharMetaInst.ShouldGainHousingUpgrade(lvl={lvl}) = {__result}");
    }
}

[HarmonyPatch(typeof(CharMetaInst), nameof(CharMetaInst.AddLvl))]
internal static class Debug_CharMetaInst_AddLvl
{
    private static void Prefix(CharMetaInst __instance)
    {
        LocationHooks.Log?.Msg($"[DBG] entering CharMetaInst.AddLvl(type={__instance?.Type}, curLvl={__instance?.Lvl})");
    }

    private static void Postfix(CharMetaInst __instance)
    {
        LocationHooks.Log?.Msg($"[DBG] returned CharMetaInst.AddLvl(type={__instance?.Type}, newLvl={__instance?.Lvl})");
    }
}
#endif
