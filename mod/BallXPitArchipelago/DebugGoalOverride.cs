#if DEBUG
using UnityEngine;

namespace BallXPitArchipelago;

/// <summary>
/// TEMPORARY test harness for the goal=evosanity win condition, not part of the shipped mod -
/// guarded behind #if DEBUG so it can never compile into a Release build. Delete once the
/// condition has been confirmed live.
///
/// Exists because the evosanity goal is the one piece of this feature that's impractical to test
/// by playing: it requires beating all 8 biomes AND discovering all 69 evolved balls, which is
/// many hours of real play before the new logic is exercised even once. These overrides make both
/// halves satisfiable on demand, so the actual decision - LocationHooks.IsGoalMet, the same method
/// the real path calls, not a copy - can be checked in both directions in under a minute.
///
/// HOTKEYS (handled in ApGui.OnGUI via IMGUI's Event.current, which fires reliably every frame -
/// Mod.OnUpdate is throttled to every 30th frame, where GetKeyDown's single-frame window would be
/// missed most of the time):
///
///   F9   dry-run: evaluate the real goal condition and log the verdict. Never reports anything
///        to Archipelago, so it's safe to press at any time, on any seed.
///   F10  toggle "pretend all 8 biomes are complete"
///   F11  toggle "pretend all 69 evolved balls are discovered"
///
/// The intended test is entirely dry-run:
///   1. F10 on, F11 off, F9  -> must report NOT met, naming how many balls are still missing.
///                              This is the case that matters - it's the regression the whole
///                              "goal is a superset" design exists to prevent.
///   2. F10 on, F11 on,  F9  -> must report met.
///   3. F10 off, F11 off, F9 -> must report NOT met, naming the biome count.
///
/// Deliberately NO hotkey to actually fire SetGoalAchieved. That call cannot be un-sent - it would
/// permanently mark the slot finished on the server - and the dry run already exercises the real
/// decision method, which is the part that could be wrong. The reporting itself (the Task.Run and
/// the guard flag) is unchanged code that has shipped since v0.1.
/// </summary>
internal static class DebugGoalOverride
{
    /// <summary>Makes LocationHooks.AllLevelsComplete() return true regardless of real progress.</summary>
    internal static bool ForceAllLevelsComplete { get; private set; }

    /// <summary>Makes BallDiscoveryTracker.AllEvolvedBallsDiscovered() return true regardless of real progress.</summary>
    internal static bool ForceAllBallsDiscovered { get; private set; }

    /// <summary>
    /// Called from ApGui.OnGUI. Only acts on KeyDown of the three hotkeys and never consumes the
    /// event otherwise, so it can't interfere with the connect box's own text-field handling.
    /// </summary>
    internal static void HandleHotkeys(Event e)
    {
        if (e == null || e.type != EventType.KeyDown)
            return;

        switch (e.keyCode)
        {
            case KeyCode.F9:
                LogDryRun();
                break;

            case KeyCode.F10:
                ForceAllLevelsComplete = !ForceAllLevelsComplete;
                LocationHooks.Log?.Msg($"[DebugGoalOverride] ForceAllLevelsComplete = {ForceAllLevelsComplete}");
                ApGui.ShowToast($"[DBG] Force all biomes complete: {ForceAllLevelsComplete}");
                break;

            case KeyCode.F11:
                ForceAllBallsDiscovered = !ForceAllBallsDiscovered;
                LocationHooks.Log?.Msg($"[DebugGoalOverride] ForceAllBallsDiscovered = {ForceAllBallsDiscovered}");
                ApGui.ShowToast($"[DBG] Force all balls discovered: {ForceAllBallsDiscovered}");
                break;

            default:
                return;
        }

        e.Use();
    }

    /// <summary>
    /// Evaluates the real condition and logs the verdict without reporting anything. Calls
    /// LocationHooks.IsGoalMet directly - the same method ReportGoalIfComplete uses - so a pass
    /// here really does mean the shipping logic agrees.
    /// </summary>
    private static void LogDryRun()
    {
        var log = LocationHooks.Log;
        var goal = EvosanityOptions.GoalIsEvosanity ? "evosanity" : "all_biomes";
        var met = LocationHooks.IsGoalMet(out var reason);

        log?.Msg(
            $"[DebugGoalOverride] DRY RUN (nothing reported to Archipelago): goal={goal}, " +
            $"forceLevels={ForceAllLevelsComplete}, forceBalls={ForceAllBallsDiscovered}");
        log?.Msg($"[DebugGoalOverride]   -> goal {(met ? "WOULD fire" : "would NOT fire")}: {reason}");

        ApGui.ShowToast(met ? "[DBG] Goal WOULD fire" : "[DBG] Goal would NOT fire");
    }
}
#endif
