using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Newtonsoft.Json.Linq;

namespace BallXPitArchipelago;

/// <summary>
/// Enforces the real intended difficulty progression (confirmed live with the user, not the
/// LevelType enum declaration order - the two differ substantially) at the level-select
/// screen: a level is only treated as unlocked once every earlier level's "Level Access"
/// item has also been received, not just its own. Without this, per-item delivery order in
/// a multiworld is unrelated to difficulty - a player could receive a late-game level's
/// access item before any earlier one and get dropped into content far above where they're
/// actually equipped to survive (reported live).
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
    /// True once every level up to and including this one (in the real difficulty order)
    /// has had its "Level Access" item received - not just this level's own item, so bad
    /// luck on delivery order can't hand the player a high-tier level before they're able
    /// to realistically survive it.
    /// </summary>
    internal static bool IsReachable(LevelType type)
    {
        // Slot data not loaded yet, or this level isn't in the parsed order (shouldn't
        // happen for any of the 8 real levels) - fall back to the old single-item check
        // rather than lock everything out while we're still catching up.
        if (_order == null || _order.Count == 0)
            return ItemReceiver.UnlockedLevels.Contains(type);

        // The first level in the order is the vanilla starting level - already unlocked the
        // moment a save is created, no item needed (confirmed live), so it's excluded from
        // the chain entirely: always reachable itself, and never required as a prerequisite
        // for anything later (see Rules.py's _set_level_order_rules for why).
        if (type == _order[0])
            return true;

        if (!_order.Contains(type))
            return ItemReceiver.UnlockedLevels.Contains(type);

        return _order.Skip(1)
            .TakeWhile(lt => lt != type)
            .Append(type)
            .All(ItemReceiver.UnlockedLevels.Contains);
    }
}
