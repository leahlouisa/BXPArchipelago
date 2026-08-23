using System;
using System.Security.Cryptography;
using System.Text;
using Il2Cpp;
using MelonLoader;

namespace BallXPitArchipelago;

/// <summary>
/// Reorders InfoDB.I.BlueprintsByLevel[lt] per level, seeded by the AP room seed, so which
/// building each level offers next varies per multiworld instead of always following
/// vanilla's fixed authored order (e.g. Graveyard always offering Sheriff's Office first,
/// then Gunsmith, then Haunted House...). Confirmed live via temporary debug logging (see
/// project memory) that list *position* - not the BuildingInfo.UnlockOrder field, which
/// turned out to be unrelated (its values don't correlate with reveal order at all) - is
/// what vanilla's reveal logic actually walks in sequence, skipping already-owned entries.
///
/// Deliberately per-level, not one shuffle across every building: this only reorders
/// entries that already belong to a given level's list, so it can't hand a level a building
/// vanilla never intended for it. Trophy (kXIdol) buildings and the CharHousing pool aren't
/// part of BlueprintsByLevel at all, so they're untouched by this - GainBlueprintLocationPatch
/// still sends a location check either way, this only changes which specific building comes
/// out of the ~62 buildings that live in a per-level list.
///
/// Deterministic and idempotent: the sort key for each building is recomputed fresh from
/// the AP seed every time, not derived from the list's current order, so re-running this
/// (e.g. on every reconnect to the same seed) always converges to the same target
/// arrangement rather than drifting further with each call.
/// </summary>
internal static class BlueprintShuffle
{
    private static bool _applied;

    internal static void ApplyOnce(string apSeed)
    {
        if (_applied || InfoDB.I == null || string.IsNullOrEmpty(apSeed))
            return;

        var byLevel = InfoDB.I.BlueprintsByLevel;
        if (byLevel == null)
            return;

        // Don't latch _applied until we've actually found real content to shuffle - if
        // InfoDB.I exists but its lists aren't populated yet (e.g. this ran before a save
        // was loaded), a permanent skip here would mean it never gets a real chance to run
        // once they are populated, since Mod.OnUpdate() only calls this until it succeeds.
        var totalShuffled = 0;

        for (var i = 0; i < byLevel.Length; i++)
        {
            var list = byLevel[i];
            if (list == null || list.Count < 2)
                continue;

            var entries = new BuildingInfo[list.Count];
            for (var j = 0; j < list.Count; j++)
                entries[j] = list[j];

            Array.Sort(entries, (a, b) => string.CompareOrdinal(SortKey(apSeed, i, a.Type), SortKey(apSeed, i, b.Type)));

            for (var j = 0; j < entries.Length; j++)
                list[j] = entries[j];

            totalShuffled += entries.Length;
            LocationHooks.Log?.Msg($"[BlueprintShuffle] level index {i}: new order = {string.Join(", ", Array.ConvertAll(entries, e => e.Type.ToString()))}");
        }

        if (totalShuffled == 0)
        {
            LocationHooks.Log?.Warning("[BlueprintShuffle] found no populated per-level lists yet - will retry.");
            return;
        }

        _applied = true;
        LocationHooks.Log?.Msg($"Shuffled per-level blueprint discovery order for this seed ({totalShuffled} entries across {byLevel.Length} levels).");
    }

    private static string SortKey(string apSeed, int levelIndex, BuildingType bt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{apSeed}:{levelIndex}:{bt}"));
        return Convert.ToHexString(bytes);
    }
}
