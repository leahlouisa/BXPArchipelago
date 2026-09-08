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

/// <summary>
/// Investigating whether a guaranteed-blueprint-per-boss option is feasible. No dedicated
/// "boss defeated" method/state was found via decompiling (Assembly-CSharp has no
/// OnBossKilled, no boss-tracking field on LevelData beyond DidCompleteWithChar's opaque
/// tgtDifficulty getter) - bosses turn out to be grid-piece-based (LevelInfo.BossInfo/
/// BossTurns), not a separate combat subsystem with its own hookable event. GameState DOES
/// have a dedicated kFoundBlueprint entry though, and GameMgr.SetState is the single dispatch
/// point for every transition - logging every transition plus the level-complete/endless-entry
/// anchors should let us correlate against the user's own boss-kill timing to find the real
/// trigger (or confirm there isn't a clean one and a different approach is needed).
/// </summary>
[HarmonyPatch(typeof(GameMgr), nameof(GameMgr.SetState))]
internal static class Debug_GameMgr_SetState
{
    private static void Prefix(GameState st, bool force)
    {
        LocationHooks.Log?.Msg($"[DBG] entering GameMgr.SetState(state={st}, force={force})");
    }

    private static void Postfix(GameState st, bool force)
    {
        LocationHooks.Log?.Msg($"[DBG] returned GameMgr.SetState(state={st}, force={force})");
    }
}

[HarmonyPatch(typeof(GameMgr), nameof(GameMgr.MarkLevelComplete))]
internal static class Debug_GameMgr_MarkLevelComplete
{
    private static void Prefix()
    {
        LocationHooks.Log?.Msg("[DBG] entering GameMgr.MarkLevelComplete()");
    }

    private static void Postfix()
    {
        LocationHooks.Log?.Msg("[DBG] returned GameMgr.MarkLevelComplete()");
    }
}

[HarmonyPatch(typeof(GameMgr), nameof(GameMgr.EnterEndless))]
internal static class Debug_GameMgr_EnterEndless
{
    private static void Prefix()
    {
        LocationHooks.Log?.Msg("[DBG] entering GameMgr.EnterEndless()");
    }

    private static void Postfix()
    {
        LocationHooks.Log?.Msg("[DBG] returned GameMgr.EnterEndless()");
    }
}

/// <summary>
/// Restored after confirming removing every patch on this chain does NOT fix the crash
/// (ruling out "too many stacked patches" as the cause) - full entry/exit visibility on the
/// whole Cost.CanAfford/Cost.Spend/SaveMgr.Spend*/Add* chain is needed again to see exactly
/// where the current build (in-place BuildCost mutation + zero-amount guards, no other
/// changes) actually fails.
/// </summary>
[HarmonyPatch(typeof(SaveMgr), nameof(SaveMgr.SpendResources))]
internal static class Debug_SaveMgr_SpendResources
{
    private static void Prefix(ResourceType rt, int amt)
    {
        LocationHooks.Log?.Msg($"[DBG] entering SaveMgr.SpendResources(rt={rt}, amt={amt})");
    }

    private static void Postfix(ResourceType rt, int amt)
    {
        LocationHooks.Log?.Msg($"[DBG] returned SaveMgr.SpendResources(rt={rt}, amt={amt})");
    }
}

[HarmonyPatch(typeof(SaveMgr), nameof(SaveMgr.SpendGold))]
internal static class Debug_SaveMgr_SpendGold
{
    private static void Prefix(int amt)
    {
        LocationHooks.Log?.Msg($"[DBG] entering SaveMgr.SpendGold(amt={amt})");
    }

    private static void Postfix(int amt)
    {
        LocationHooks.Log?.Msg($"[DBG] returned SaveMgr.SpendGold(amt={amt})");
    }
}

[HarmonyPatch(typeof(Cost), nameof(Cost.CanAfford))]
internal static class Debug_Cost_CanAfford
{
    private static void Postfix(Cost __instance, bool __result)
    {
        LocationHooks.Log?.Msg($"[DBG] Cost.CanAfford() cost={__instance?.ToString("/")}, result={__result}");
    }
}

[HarmonyPatch(typeof(Cost), nameof(Cost.Spend))]
internal static class Debug_Cost_Spend
{
    private static void Prefix(Cost __instance)
    {
        LocationHooks.Log?.Msg($"[DBG] entering Cost.Spend() cost={__instance?.ToString("/")}");
    }

    private static void Postfix(Cost __instance)
    {
        LocationHooks.Log?.Msg($"[DBG] returned Cost.Spend() cost={__instance?.ToString("/")}");
    }
}
#endif
