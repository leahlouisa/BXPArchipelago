#if DEBUG
using HarmonyLib;
using Il2Cpp;

namespace BallXPitArchipelago;

/// <summary>
/// Minimal probe for why the 11 non-pooled CharHousing blueprints stall a level's chain. Logs which
/// building vanilla chose for a blueprint reward, together with the state that tests the leading
/// hypothesis.
///
/// ESTABLISHED (real IL2CPP stack capture, not inference):
///
///   GRANT|type=kCampground|level=kClouds|gameState=kFoundBlueprint
///     at BlueprintFoundUI.Activate ()
///     at GameMgr.SetState (GameState st, Boolean force)
///     at BlueprintFoundUI.ActivateBuilding (BuildingType bt)
///     at PickupObj.CompletePickup ()
///     at PickupObj+&lt;_AnimatePlayerPickUp&gt;d__28.MoveNext ()
///     at MEC.Timing.Update ()
///
/// So an NPCH blueprint arrives as an ORDINARY IN-LEVEL PICKUP the player collected - the same path
/// as every pooled blueprint, not the separate hidden system three earlier investigations assumed.
///
/// HYPOTHESIS UNDER TEST: the bug is partly self-inflicted. BlueprintShuffle.StripGlobalList empties
/// BossDropBlueprints and FuserDropBlueprints completely ("Stripped 25 pool-eligible entries ...
/// (0 remaining)", twice, in every connected log). With nothing left to choose from, vanilla appears
/// to fall back to some "any building the player doesn't own" rule - and the only unowned buildings
/// we have NOT replaced with Void Trophy placeholders are exactly the 11 NPCH. If so, the fix is to
/// leave a placeholder in those lists instead of emptying them, which lands squarely on the
/// "everything is an AP grant" behaviour we want.
///
/// Hence every line logs bossList/fuserList/curLevelList alongside the chosen building. An NPCH
/// chosen while those sit at 0 confirms it.
///
/// WHY ONLY ONE PATCH - and it must stay that way:
///
/// Patching any of PickupMgr.DropBlueprint / DropBlueprintIfNecessary / TryDropBossBlueprint /
/// TryDropFuserBlueprint breaks mod startup outright. ApGui registers, then AddComponent&lt;ApGui&gt;()
/// throws NullReferenceException and the mod never initialises. All four return PickupObj, and
/// Harmony builds its wrapper from the original's FULL signature - return type included - so that
/// type gets resolved during the patch-application pass regardless of what the patch method itself
/// declares. Two dead theories on the way here, both disproven by experiment: first that naming
/// `PickupObj __result` in a parameter list was the trigger (removing it changed nothing), then that
/// it was Harmony patch COUNT exhausting Il2CppInterop's "Class::Init signatures" budget (cutting
/// 30 patches to 19 changed nothing either).
///
/// BlueprintFoundUI.ActivateBuilding(BuildingType) returns void and takes a plain enum, so nothing
/// in its signature needs a game type resolved. It is also directly on the confirmed delivery path,
/// so it sees every chosen building. What it cannot tell us is WHO chose - for that, the list-count
/// context is the substitute.
///
/// Observe-only void Prefix: cannot alter behaviour.
/// </summary>
internal static class DebugBlueprintDropProbe
{
    private const string Tag = "[BPDROP]";

    private static readonly System.Collections.Generic.HashSet<BuildingType> NonPooledCharHousing = new()
    {
        BuildingType.kVilla, BuildingType.kCaptainQuarters, BuildingType.kCampground,
        BuildingType.kSingleFamilyHome, BuildingType.kMausoleum, BuildingType.kCozyHome,
        BuildingType.kLab, BuildingType.kBrickHouse, BuildingType.kRockyHill,
        BuildingType.kMonastery, BuildingType.kHovel,
    };

    internal static void Report(BuildingType bt)
    {
        var flag = NonPooledCharHousing.Contains(bt) ? "|NONPOOLED_CHARHOUSING=YES" : "";
        LocationHooks.Log?.Msg($"{Tag} CHOSEN|type={bt}|{ListState()}{flag}");
        DumpStack();

        // Only for the buildings actually under investigation, and only in-level where the choice
        // was just made: dump vanilla's own availability queries right at the scene of the crime.
        // DebugBlueprintSourceProbe is the same dump F7 produces from the base - having it fire
        // automatically here means an in-run capture never depends on remembering a keypress.
        if (NonPooledCharHousing.Contains(bt))
            DebugBlueprintSourceProbe.Dump($"non-pooled CharHousing chosen: {bt}");
    }

    /// <summary>
    /// The state that decides the hypothesis: are the lists vanilla picks from actually empty, and
    /// what is sitting at the head of the current level's list (should be the Void Trophy
    /// placeholder while the chain still has positions left)?
    /// </summary>
    private static string ListState()
    {
        if (InfoDB.I == null)
            return "<no InfoDB>";

        var boss = Safe(() => (InfoDB.I.BossDropBlueprints?.Count ?? -1).ToString());
        var fuser = Safe(() => (InfoDB.I.FuserDropBlueprints?.Count ?? -1).ToString());
        var level = LevelMgr.I == null ? "<none>" : Safe(() => LevelMgr.I.CurLevel.ToString());
        var curLevelList = "<n/a>";

        if (LevelMgr.I != null && InfoDB.I.BlueprintsByLevel != null)
        {
            curLevelList = Safe(() =>
            {
                // Difficulty position, NOT (int)CurLevel - BlueprintsByLevel is ordered by
                // difficulty, and reading it by enum ordinal is the bug this probe helped find. An
                // ordinal read here reported "curLevelList=0 (head=<empty>)" for a level that had a
                // placeholder waiting, which is precisely the misreading to avoid reproducing.
                var position = LevelUnlockOrder.PositionOf(LevelMgr.I.CurLevel);
                if (position == null)
                    return "<difficulty order not loaded>";

                var idx = position.Value;
                var byLevel = InfoDB.I.BlueprintsByLevel;
                if (idx < 0 || idx >= byLevel.Length || byLevel[idx] == null)
                    return "<out of range>";

                var list = byLevel[idx];
                var head = list.Count > 0 && list[0] != null ? list[0].Type.ToString() : "<empty>";
                return $"{list.Count} (head={head})";
            });
        }

        return $"level={level}|bossList={boss}|fuserList={fuser}|curLevelList={curLevelList}";
    }

    /// <summary>
    /// Best-effort: the IL2CPP stack walk only yields frames when managed ones sit below us. It
    /// worked from inside MEC's coroutine driver and returned only its own frame from native call
    /// sites, so this stays quiet rather than logging a useless single line.
    /// </summary>
    private static void DumpStack()
    {
        var stack = Safe(() => Il2CppSystem.Environment.StackTrace);
        if (string.IsNullOrWhiteSpace(stack))
            return;

        var frames = 0;
        foreach (var line in stack.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r').Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Contains("get_StackTrace"))
                continue;

            LocationHooks.Log?.Msg($"{Tag}   FRAME|{trimmed}");
            if (++frames >= 10)
                break;
        }
    }

    private static string Safe(System.Func<string> f)
    {
        try
        {
            return f() ?? "<null>";
        }
        catch (System.Exception e)
        {
            return $"<ERR {e.GetType().Name}>";
        }
    }
}

/// <summary>
/// The only patch here, deliberately. See the class comment for why patching anything on PickupMgr
/// kills mod startup. Returns void, takes a plain enum, sits on the confirmed delivery path.
/// </summary>
[HarmonyPatch(typeof(BlueprintFoundUI), nameof(BlueprintFoundUI.ActivateBuilding))]
internal static class Debug_BlueprintFoundUI_ActivateBuilding
{
    private static void Prefix(BuildingType bt) => DebugBlueprintDropProbe.Report(bt);
}
#endif
