using System;
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
/// Nothing here is a Harmony patch. Same reasoning as BallHooks.cs: these are reachable directly
/// from singletons, so there's no reason to go near vanilla's own call paths.
/// </summary>
internal static class RunScopedRewards
{
    /// <summary>
    /// Delivers at most ONE reward per call, and only while a run is actually in progress.
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

        if (ItemReceiver.PendingLevelUps <= 0 && ItemReceiver.PendingFusers <= 0)
            return;

        // GameMgr only exists inside a battle, so this doubles as the "are we in a run?" test -
        // out at the base it's simply null and everything stays queued. kPlaying specifically
        // (rather than any in-battle state) keeps us out of kLevelUp, kPaused, kGameOver,
        // kPickTreasure and the rest, where a reward would either be lost or stack on an open UI.
        var gameMgr = GameMgr.I;
        if (gameMgr == null || gameMgr.CurState != GameState.kPlaying)
            return;

        if (PickupMgr.I == null)
            return;

        // Fusers first: a fuser is a pickup the player still has to collect, so getting it into
        // the world early gives them the most time to reach it, whereas a level-up resolves
        // instantly and can wait a tick.
        if (ItemReceiver.PendingFusers > 0 && TryGrantFuser())
            return;

        if (ItemReceiver.PendingLevelUps > 0)
            TryGrantLevelUp();
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
    /// Grants exactly enough XP to cross the current level threshold - not an estimate: TgtXP is
    /// vanilla's own target for the next level and CurXP the run's current total, so the
    /// difference is precisely one level and no banked progress toward the one after.
    ///
    /// CurXP is a float and TgtXP an int, hence the ceiling. The floor of 1 covers the case where
    /// the player is already at or past the threshold on this tick (vanilla is about to level them
    /// up anyway) - passing 0 or a negative would either do nothing or, worse, be interpreted as a
    /// deduction.
    /// </summary>
    private static bool TryGrantLevelUp()
    {
        var upgradeMgr = UpgradeMgr.I;
        var battle = BattleSaveData.I;
        if (upgradeMgr == null || battle == null)
            return false;

        var needed = (int)Math.Ceiling(upgradeMgr.TgtXP - battle.CurXP);
        if (needed < 1)
            needed = 1;

        if (!ItemReceiver.TryConsumePending(levelUp: true))
            return false;

        try
        {
            PickupMgr.I.AddXP(needed);
        }
        catch (Exception e)
        {
            LocationHooks.Log?.Error($"[RunScopedRewards] Failed to grant a free level up: {e}");
            return true;
        }

        LocationHooks.Log?.Msg(
            $"[RunScopedRewards] Granted a free level up (+{needed} XP to reach {upgradeMgr.TgtXP}; " +
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
