using System;
using System.Collections.Generic;
using Il2Cpp;
using Newtonsoft.Json.Linq;

namespace BallXPitArchipelago;

/// <summary>
/// Enforces the real intended difficulty progression (confirmed live with the user, not the
/// LevelType enum declaration order - the two differ substantially) at the level-select
/// screen: a level is only treated as unlocked once the player holds enough copies of the
/// "Progressive Level Access" item to cover every earlier level too, not just its own.
/// Without this, per-item delivery order in a multiworld is unrelated to difficulty - a
/// player could receive access far above where they're actually equipped to survive
/// (reported live). A single progressive item (rather than one distinct item per level, the
/// original design) means every copy received always does something - it unlocks whichever
/// level is next in the player's own order, never a level further out that would just sit
/// doing nothing until earlier ones caught up (also reported live: confusing, since an AP
/// grant with no visible effect looks like a bug even when it isn't one).
///
/// The order itself is computed once at generation time (Rules.py's LEVEL_UNLOCK_ORDER) and
/// read from slot data here rather than hand-duplicated, so the generator's access rules
/// and this runtime check can never disagree about it.
/// </summary>
internal static class LevelUnlockOrder
{
    private static List<LevelType> _order;

    internal static void ApplyFromSlotData(Dictionary<string, object> slotData)
    {
        if (_order != null || slotData == null)
            return;

        if (!slotData.TryGetValue("level_unlock_order", out var raw) || raw is not JArray arr)
            return;

        var order = new List<LevelType>();
        foreach (var token in arr)
        {
            if (Enum.TryParse<LevelType>(token.ToString(), out var lt))
                order.Add(lt);
            else
                LocationHooks.Log?.Warning($"[LevelUnlockOrder] '{token}' is not a known LevelType - skipping it.");
        }

        if (order.Count == 0)
        {
            LocationHooks.Log?.Warning("[LevelUnlockOrder] slot data present but nothing parsed - will retry.");
            return;
        }

        _order = order;
        LocationHooks.Log?.Msg($"[LevelUnlockOrder] Applied level unlock order from slot data: {string.Join(" -> ", order)}");
    }

    /// <summary>
    /// True once the player holds at least as many "Progressive Level Access" copies as
    /// this level's position in the real difficulty order requires (0 for the starting
    /// level, which needs none - see Rules.py's _level_access_positions). Every copy
    /// received always unlocks whichever level is next in this order, regardless of when
    /// or from where it arrived in the multiworld - so unlike the old per-level-item
    /// design, there's no way for a level to be "skipped over": position N can only ever
    /// become reachable once positions 1..N-1 already are.
    /// </summary>
    internal static bool IsReachable(LevelType type)
    {
        // Slot data not loaded yet - err locked rather than guessing (this window is brief,
        // right after connecting, and the alternative of guessing wrong risks the exact bug
        // this whole system exists to prevent).
        if (_order == null || _order.Count == 0)
            return false;

        var position = _order.IndexOf(type);
        return position >= 0 && ItemReceiver.ProgressiveLevelAccessCount >= position;
    }

    /// <summary>
    /// Which level becomes reachable once the player holds exactly `count` copies of the
    /// progressive item (0 = the starting level, already reachable with none) - used only
    /// to report which level a specific received copy unlocked, e.g. for the receipt toast.
    /// Null if slot data isn't loaded yet or count is out of range.
    /// </summary>
    internal static LevelType? LevelForCount(int count)
    {
        if (_order == null || count < 0 || count >= _order.Count)
            return null;

        return _order[count];
    }
}
