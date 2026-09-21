using System;
using HarmonyLib;
using Il2Cpp;

namespace BallXPitArchipelago;

/// <summary>
/// Delivers the two run-scoped filler rewards - "Free Level Up" and "Fusion Reactor" - which help
/// only the run you're currently playing rather than adding permanently to the base economy.
///
/// Added after a real report from live play: an evosanity seed is mostly filler by construction
/// (up to 90 extra locations with no item category of their own), and a steady stream of resource
/// grants was both monotonous and genuinely inflationary - the player ended up rich enough that
/// building costs stopped mattering at all. Half the filler cycle is now these instead; see the
/// apworld's Items.py all_filler_item_names.
///
/// Both are ordinary vanilla operations, NOT GameCheatMgr.ApplyCheat. That class has
/// GameCheatType.kLevelUp and kGainFuser - exactly these two features, one call each, very
/// tempting - but BattleSaveData has a `Cheated` bool that AchMgr.ShouldCheckAch is the obvious
/// consumer of, so routing through the cheat menu would likely cost the player their Steam
/// achievements for that run. Granting XP and dropping a pickup are things vanilla does to itself
/// constantly, so the question never arises. (ApplyCheat remains the fallback if these ever stop
/// working; it's per-run, not permanent - MetaSaveData has no such flag - and `Cheated` is a plain
/// bool field we could write back.)
///
/// The delivery itself needs no Harmony patch - the managers involved are reachable directly from
/// their singletons. The two patches at the bottom of this file exist only to learn WHEN a run has
/// stopped being playable, which turned out not to be inferable from GameMgr.CurState alone (see
/// _runEnded). Both are Postfixes on ordinary GameMgr methods, nothing like the SaveMgr
/// resource-mutation methods that must never be patched under any circumstances - see
/// EconomyHooks.cs and DebugHooks.cs for that story.
/// </summary>
internal static class RunScopedRewards
{
    /// <summary>
    /// Whether a run is genuinely live right now. Turned ON only by an explicit
    /// SetState(kEnteringLvl), and OFF by MarkLevelComplete or kEndingGame/kGameOver.
    ///
    /// GameMgr.CurState CANNOT be used for this, in two separate ways, both confirmed live from
    /// real lost rewards:
    ///
    /// 1. kPlaying is GameState's FIRST enum value, so it is also default(GameState) == 0. A
    ///    freshly-constructed GameMgr therefore reads as "playing" before anything has called
    ///    SetState on it. That window is real and we landed in it:
    ///
    ///      21:02:03.075  SetState(kGameOver)        <- previous run ends
    ///      21:03:22.729  [Free Level Up queued at base]
    ///      21:06:51.903  [granted a free level up]  <- last real state was kGameOver!
    ///      21:06:51.971  SetState(kEnteringLvl)     <- the run actually begins, 68ms LATER
    ///
    ///    Worse than merely early: UpgradeMgr/BattleSaveData still held the PREVIOUS run's values
    ///    (TgtXP=1088, CurXP~1062 - the end state of the run that died at 21:02), so the XP went
    ///    into objects the new run then reinitialised to zero. Granted into the void.
    ///
    /// 2. After a level is beaten, the end-of-run reward sequence bounces back through kPlaying
    ///    between each popup:
    ///
    ///      21:20:42.766  MarkLevelComplete()        <- run is over here
    ///      21:20:44.731  SetState(kFoundBlueprint)
    ///      21:20:45.681  SetState(kPlaying)         <- and again at :51.915 and :55.732
    ///      21:20:55.983  [dropped a Fusion Reactor] <- poll landed in one of those windows
    ///      21:20:56.015  SetState(kEndingGame)
    ///
    /// A positive latch handles both: case 1 because the latch is still false until kEnteringLvl,
    /// case 2 because MarkLevelComplete clears it and the later kPlaying bounces don't set it
    /// (only kEnteringLvl does). Checking CurState == kPlaying is still needed ON TOP of this, to
    /// stay out of kLevelUp/kPaused/kPickTreasure mid-run.
    ///
    /// Note this means a reward can only be delivered in a run the mod watched START. That's fine:
    /// confirmed with the user that the game has no resume-mid-run - abandoning is the only way out
    /// of a run in progress - so there's no path where kEnteringLvl gets skipped.
    ///
    /// The pre-level bonus-choice screens are handled by the CurState == kPlaying check rather than
    /// by this latch. Some buildings (Gemsmith, Antique Shop - see BuildingMgr.BonusBallChoiceLvl /
    /// BonusPassiveChoiceLvl) open an extra ball/passive selection at the start of a run, which is
    /// GameState.kBonusBall / kBonusPassive. Either ordering is safe: if the screen comes before
    /// kEnteringLvl the latch is still false, and if it comes after, CurState isn't kPlaying yet -
    /// so a reward simply waits until the level proper. This is why the state check is kPlaying
    /// specifically and not "any in-battle state".
    /// </summary>
    private static bool _runLive;

    /// <summary>
    /// Called from the SetState patch below on kEnteringLvl - the one unambiguous "a new run is
    /// beginning" signal in the state machine.
    /// </summary>
    internal static void NotifyRunStarted()
    {
        if (_runLive)
            return;

        _runLive = true;
        LocationHooks.Log?.Msg("[RunScopedRewards] Run is live - queued rewards can now be delivered.");
    }

    /// <summary>
    /// Called from the patches below. Idempotent - the terminal states can fire more than once per
    /// run, and MarkLevelComplete plus kEndingGame will both land on a completed level.
    /// </summary>
    internal static void NotifyRunEnded(string why)
    {
        if (!_runLive)
            return;

        _runLive = false;
        LocationHooks.Log?.Msg($"[RunScopedRewards] Run no longer live ({why}) - holding queued rewards for the next one.");
    }

    /// <summary>
    /// Delivers at most ONE reward per call, and only while a run is genuinely in progress.
    ///
    /// One at a time because a level-up opens a modal choice screen: granting three at once would
    /// either stack three UIs or have two silently swallowed while the first is open. Waiting for
    /// CurState to be back to kPlaying means the next one lands after the player has finished
    /// choosing, which is also how vanilla paces its own level-ups.
    /// </summary>
    internal static void Drain()
    {
        if (ApConnection.Session == null)
            return;

        // The positive latch, not CurState, is what decides whether a run is live - see _runLive
        // for the two distinct ways CurState lies about it.
        if (!_runLive)
            return;

        if (ItemReceiver.PendingLevelUps <= 0 && ItemReceiver.PendingFusers <= 0)
            return;

        // GameMgr going away mid-run shouldn't happen, but if it does, treat the run as over rather
        // than dereferencing null.
        var gameMgr = GameMgr.I;
        if (gameMgr == null)
        {
            NotifyRunEnded("GameMgr disappeared");
            return;
        }

        // Needed IN ADDITION to _runLive: keeps us out of kLevelUp, kPaused, kPickTreasure and the
        // rest, where a reward would either be lost or stack on an already-open UI.
        if (gameMgr.CurState != GameState.kPlaying)
            return;

        if (PickupMgr.I == null)
            return;

        // Level-ups before fusers (user preference, from real play): a level-up resolves instantly
        // and can improve the balls you'd then take into a fuser's evolution option, so getting it
        // first is strictly more useful than the reverse. The original order put fusers first on
        // the theory that a pickup needs collecting time, but a fuser waits on the floor anyway.
        if (ItemReceiver.PendingLevelUps > 0 && TryGrantLevelUp())
            return;

        if (ItemReceiver.PendingFusers > 0)
            TryGrantFuser();
    }

    /// <summary>
    /// Drops a real Fusion Reactor pickup at the player's feet - PickupType.kFuser is one of
    /// vanilla's own pickup types, dropped through the same PickupMgr.DropPickup the game uses for
    /// every other drop, so it behaves exactly like a naturally-spawned fuser (including having to
    /// be collected).
    ///
    /// Especially apt for evosanity: a fuser's three choices are FuserOptionType
    /// {kCombo, kEvo, kFreeUpgrades}, so one of them is literally the evolution option the whole
    /// mode is built around.
    /// </summary>
    private static bool TryGrantFuser()
    {
        var player = Player.I;
        if (player == null)
            return false;

        if (!ItemReceiver.TryConsumePending(levelUp: false))
            return false;

        try
        {
            PickupMgr.I.DropPickup(player.transform.position, PickupType.kFuser);
        }
        catch (Exception e)
        {
            // The reward is already consumed at this point. Re-queueing on failure would risk an
            // infinite retry loop against a genuinely broken call, so it's logged and dropped
            // instead - one lost filler item is a far better outcome than a spin.
            LocationHooks.Log?.Error($"[RunScopedRewards] Failed to drop a Fusion Reactor pickup: {e}");
            return true;
        }

        LocationHooks.Log?.Msg($"[RunScopedRewards] Dropped a Fusion Reactor ({ItemReceiver.PendingFusers} still pending).");
        ApGui.ShowToast("Fusion Reactor incoming!");
        return true;
    }

    /// <summary>
    /// Grants a full level's worth of XP - TgtXP, vanilla's own target for the next level.
    ///
    /// This used to grant only the REMAINDER (TgtXP - CurXP), on the reasoning that it was "exactly
    /// one level and no banked progress toward the next". That was a real bug, reported live as
    /// "earned a level up but never received it": if the player happens to be nearly at the
    /// threshold, the remainder is nearly nothing. The log caught it exactly - "+26 XP to reach
    /// 1088", i.e. a whole item spent to buy 26 XP the player would have earned seconds later
    /// through normal play. It technically levelled them up, and was technically worthless.
    ///
    /// A flat TgtXP always yields exactly one level regardless of where in the bar the player
    /// happens to be, which is what the item's name promises. It also sidesteps a boundary question
    /// the old version had - whether vanilla levels up on >= or > the target - since overshooting
    /// is now guaranteed rather than landing exactly on it.
    /// </summary>
    private static bool TryGrantLevelUp()
    {
        var upgradeMgr = UpgradeMgr.I;
        if (upgradeMgr == null)
            return false;

        var amount = upgradeMgr.TgtXP;
        if (amount < 1)
            return false; // TgtXP not calculated yet this run - try again next tick.

        if (!ItemReceiver.TryConsumePending(levelUp: true))
            return false;

        try
        {
            PickupMgr.I.AddXP(amount);
        }
        catch (Exception e)
        {
            LocationHooks.Log?.Error($"[RunScopedRewards] Failed to grant a free level up: {e}");
            return true;
        }

        LocationHooks.Log?.Msg(
            $"[RunScopedRewards] Granted a free level up (+{amount} XP, a full level; " +
            $"{ItemReceiver.PendingLevelUps} still pending).");
        ApGui.ShowToast("Free Level Up!");
        return true;
    }

    /// <summary>
    /// Receipt toast text. Says "when you're next in a level" rather than "received" when the
    /// player is out of a run, so a reward that visibly does nothing right now doesn't read as a
    /// bug - the same confusion the Progressive Level Access redesign was made to avoid.
    /// </summary>
    internal static string ToastFor(string itemName)
    {
        var inRun = GameMgr.I != null;
        return inRun
            ? $"Received: {itemName}"
            : $"Received: {itemName} (applies when you next enter a level)";
    }
}

/// <summary>
/// Tells RunScopedRewards the current run has been beaten. This is the signal that fixes the
/// lost-reward bug: it fires well before the end-of-run reward screens, which bounce the game
/// state back through kPlaying and would otherwise look like a live run to the delivery poll.
///
/// A plain Postfix on an ordinary manager method - nothing like the SaveMgr resource-mutation
/// methods that must never be patched (see EconomyHooks.cs). This exact method has been
/// Prefix+Postfix patched by DebugHooks.cs across many sessions with no trouble.
/// </summary>
[HarmonyPatch(typeof(GameMgr), nameof(GameMgr.MarkLevelComplete))]
internal static class RunEndedOnLevelCompletePatch
{
    private static void Postfix()
    {
        if (ApConnection.Session != null)
            RunScopedRewards.NotifyRunEnded("level complete");
    }
}

/// <summary>
/// Maintains RunScopedRewards' run-live latch from the two state transitions that matter:
/// kEnteringLvl (a new run is beginning - the ONLY thing that turns delivery on) and
/// kGameOver/kEndingGame (dying, or the level-complete teardown).
///
/// kEnteringLvl is what makes the latch trustworthy. GameState.kPlaying is enum value 0, so a
/// newly-constructed GameMgr reads as "playing" before anything sets its state - requiring an
/// explicit kEnteringLvl is what keeps a reward from being delivered into a run that hasn't
/// started yet, against manager objects still holding the previous run's data.
///
/// kGameOver here is not redundant with the MarkLevelComplete patch above: that one doesn't fire
/// on a death, and this one doesn't fire early enough for a completed level (see _runLive).
/// </summary>
[HarmonyPatch(typeof(GameMgr), nameof(GameMgr.SetState))]
internal static class RunLiveStateTrackingPatch
{
    private static void Postfix(GameState st)
    {
        if (ApConnection.Session == null)
            return;

        if (st == GameState.kEnteringLvl)
            RunScopedRewards.NotifyRunStarted();
        else if (st is GameState.kGameOver or GameState.kEndingGame)
            RunScopedRewards.NotifyRunEnded($"state {st}");
    }
}
