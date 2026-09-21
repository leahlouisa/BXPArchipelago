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
///   F9   dry-run: evaluate the real goal condition and log the verdict. F9 ITSELF reports nothing.
///   F10  toggle "pretend all 8 biomes are complete"
///   F11  toggle "pretend all 69 evolved balls are discovered"
///
/// WARNING, learned the hard way: F10 and F11 are NOT side-effect free, and an earlier version of
/// this comment wrongly claimed the whole harness was safe on any seed. The overrides feed
/// LocationHooks.AllLevelsComplete and BallDiscoveryTracker.AllEvolvedBallsDiscovered, which the
/// REAL PollForChanges path also consults - so turning both on genuinely satisfies the goal and
/// ReportGoalIfComplete fires within one poll tick (~0.5s), reporting to Archipelago for real.
/// Confirmed live: F11 pressed at 21:24:45.243, goal reported at 21:24:45.644. SetGoalAchieved
/// cannot be un-sent. USE A DISPOSABLE SEED.
///
/// That's a deliberate trade rather than something to fix: overrides that the real path ignored
/// would be testing a parallel universe, which is exactly the divergence IsGoalMet exists to
/// prevent. F9 alone is genuinely read-only; the toggles are the dangerous part.
///
/// The intended test is entirely dry-run:
///   1. F10 on, F11 off, F9  -> must report NOT met, naming how many balls are still missing.
///                              This is the case that matters - it's the regression the whole
///                              "goal is a superset" design exists to prevent.
///   2. F10 on, F11 on,  F9  -> must report met.
///   3. F10 off, F11 off, F9 -> must report NOT met, naming the biome count.
///
/// There's no dedicated "fire the goal" hotkey because none is needed - step 2 above already does
/// it as a side effect (see the warning). Step 1 is the one that actually matters and is safe in
/// isolation: it's the regression the "goal is a superset" design exists to prevent.
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
