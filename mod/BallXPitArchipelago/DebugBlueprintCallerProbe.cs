#if DEBUG
using HarmonyLib;
using Il2Cpp;

namespace BallXPitArchipelago;

/// <summary>
/// Answers "what actually offers the 11 non-pooled CharHousing blueprints?" by asking the runtime
/// for its call stack at the moment of the grant, instead of hunting for a data structure that
/// contains them.
///
/// Two previous attempts failed, both by inspecting data: BlueprintsByLevel, BossDropBlueprints,
/// FuserDropBlueprints and LevelData were all ruled out years apart, and DebugBlueprintListDump
/// confirmed afresh that all 11 are absent from every level list and that both global lists are
/// emptied by our own strip. So the offer comes from somewhere none of those cover.
///
/// The new idea comes from a real log: when vanilla's LevelSelectItem.InitLocked threw an NRE,
/// MelonLoader printed a full managed-style IL2CPP call stack (LevelSelectItem.InitLocked <-
/// LevelSelectUI.SetSelectedNGPlus <- LevelSelectUI.Activate <- BaseMgr.SetState <- ...). IL2CPP
/// stack walking therefore works in this build. Il2CppSystem.Environment.StackTrace exposes it as
/// a plain read-only string, so a Harmony prefix - which runs with vanilla's own frames still
/// below it on the stack - can capture the caller chain directly. No exception thrown, nothing
/// mutated, no guessing.
///
/// Logs the stack for EVERY SaveMgr.GainBlueprint call, not just the 11, specifically so the
/// working case can be diffed against the broken one: a pooled grant arrives via the Void Trophy
/// chain and works, Campground arrives via something else and deadlocks. The difference between
/// those two stacks is the thing we need to strip (or otherwise neutralise) to make these 11
/// behave like every other blueprint.
///
/// If Environment.StackTrace comes back empty or useless, that's still a result - it means stack
/// walking is only available through IL2CPP's exception path, and the next step would be a
/// deliberate throw. Try the safe thing first.
/// </summary>
internal static class DebugBlueprintCallerProbe
{
    private const string Tag = "[BPCALLER]";

    /// <summary>
    /// The 11 buildings CharHousing.py classifies as non-pooled - flagged in the log so the
    /// interesting stacks are greppable.
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<BuildingType> NonPooledCharHousing = new()
    {
        BuildingType.kVilla, BuildingType.kCaptainQuarters, BuildingType.kCampground,
        BuildingType.kSingleFamilyHome, BuildingType.kMausoleum, BuildingType.kCozyHome,
        BuildingType.kLab, BuildingType.kBrickHouse, BuildingType.kRockyHill,
        BuildingType.kMonastery, BuildingType.kHovel,
    };

    /// <summary>
    /// Keeps the log survivable. Vanilla can call GainBlueprint a lot (every AP item application
    /// goes through it too), and each stack is many lines - so log the full stack only the first
    /// few times per building, and only once per building for the uninteresting ones.
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<BuildingType, int> _logged = new();

    private const int MaxPerNonPooled = 3;
    private const int MaxPerOther = 1;

    internal static void Report(BuildingType bt)
    {
        var interesting = NonPooledCharHousing.Contains(bt);
        var cap = interesting ? MaxPerNonPooled : MaxPerOther;

        _logged.TryGetValue(bt, out var seen);
        if (seen >= cap)
            return;

        _logged[bt] = seen + 1;

        var log = LocationHooks.Log;
        var flag = interesting ? "|NONPOOLED_CHARHOUSING=YES" : "";
        var applying = ItemReceiver.IsApplyingItem ? "AP-item" : "vanilla";
        var level = LevelMgr.I == null ? "<no LevelMgr>" : Safe(() => LevelMgr.I.CurLevel.ToString());
        var state = GameMgr.I == null ? "<no GameMgr>" : Safe(() => GameMgr.I.CurState.ToString());

        log?.Msg($"{Tag} GRANT|type={bt}|source={applying}|level={level}|gameState={state}|occurrence={seen + 1}{flag}");

        var stack = Safe(() => Il2CppSystem.Environment.StackTrace);
        if (string.IsNullOrWhiteSpace(stack))
        {
            log?.Msg($"{Tag}   <stack unavailable - Environment.StackTrace returned nothing; IL2CPP stack walking may be exception-only in this build>");
            return;
        }

        // One log line per frame: MelonLoader's log is line-oriented and a single embedded-newline
        // blob is painful to read back.
        foreach (var line in stack.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(trimmed))
                log?.Msg($"{Tag}   FRAME|{trimmed.Trim()}");
        }
    }

    private static string Safe(System.Func<string> f)
    {
        try
        {
            return f() ?? "";
        }
        catch (System.Exception e)
        {
            return $"<ERR {e.GetType().Name}: {e.Message}>";
        }
    }
}

/// <summary>
/// Separate from GainBlueprintLocationPatch so the shipped suppression logic isn't touched at all -
/// this only observes. Harmony runs every prefix registered for a method, and a prefix that returns
/// void can't affect whether the original executes, so this cannot change behaviour.
///
/// SaveMgr.GainBlueprint is already patched by the shipped mod, so it's a proven-safe target - and
/// it is NOT one of the four SaveMgr resource-mutation methods that must never be patched (see
/// EconomyHooks.cs).
/// </summary>
[HarmonyPatch(typeof(SaveMgr), nameof(SaveMgr.GainBlueprint))]
internal static class Debug_SaveMgr_GainBlueprint_Caller
{
    private static void Prefix(BuildingType bt)
    {
        DebugBlueprintCallerProbe.Report(bt);
    }
}
#endif
