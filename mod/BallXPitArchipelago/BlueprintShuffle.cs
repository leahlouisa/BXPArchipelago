using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Il2Cpp;
using MelonLoader;
using Newtonsoft.Json.Linq;

namespace BallXPitArchipelago;

/// <summary>
/// Applies the per-level blueprint discovery order the apworld computed during generation
/// (see Rules.py/BlueprintPools.py), read from slot data rather than recomputed here -
/// generation-time access rules and mod-side runtime behavior must use the *exact* same
/// order, or the generator's completability guarantees don't hold. Confirmed live via
/// temporary debug logging (see project memory) that list *position* in
/// InfoDB.I.BlueprintsByLevel[lt] - not the BuildingInfo.UnlockOrder field, which turned out
/// to be unrelated - is what vanilla's reveal logic actually walks in sequence, skipping
/// already-owned entries. GainBlueprintLocationPatch now suppresses the real grant for
/// every building in PoolEligibleBuildings, relying on Rules.py's progressive dependency
/// chain (position N needs position N-1's item) to keep the seed completable.
///
/// Also shuffles InfoDB.I.CharHousing, the separate mapping from character to the housing
/// building that unlocks them (e.g. vanilla always ties Sheriff's Office -> Itchy Finger) -
/// confirmed live that completing a housing building's character-unlock reads this table
/// directly, independent of the BlueprintsByLevel reveal-cursor split entirely. This one
/// stays mod-computed (not generation-time) since it has no bearing on completability -
/// Character locations/items are unaffected either way (UnlockCharLocationPatch already
/// suppresses unconditionally, regardless of which building CharHousing ties to a
/// character), so there's no dependency chain to encode.
/// </summary>
internal static class BlueprintShuffle
{
    private static bool _applied;
    private static bool _charHousingApplied;

    /// <summary>
    /// Buildings GainBlueprintLocationPatch is safe to suppress because Rules.py encodes
    /// their real dependency chain. Empty until ApplyFromSlotData succeeds.
    /// </summary>
    internal static readonly HashSet<BuildingType> PoolEligibleBuildings = new();

    internal static void ApplyFromSlotData(Dictionary<string, object> slotData)
    {
        if (_applied || InfoDB.I == null || slotData == null)
            return;

        if (!slotData.TryGetValue("blueprint_order", out var raw) || raw is not JObject blueprintOrder)
            return;

        var byLevel = InfoDB.I.BlueprintsByLevel;
        if (byLevel == null)
            return;

        // Building references are shared across lists (the same BuildingInfo object a level
        // holds is the same object other levels would reference it by, if it appeared in
        // theirs) - scanning every current list once gives a reliable BuildingType ->
        // BuildingInfo lookup without needing to trust InfoDB.I.Buildings' own indexing
        // scheme, which was never empirically confirmed.
        var lookup = new Dictionary<BuildingType, BuildingInfo>();
        for (var i = 0; i < byLevel.Length; i++)
        {
            var list = byLevel[i];
            if (list == null)
                continue;
            for (var j = 0; j < list.Count; j++)
                if (list[j] != null)
                    lookup[list[j].Type] = list[j];
        }

        if (lookup.Count == 0)
        {
            LocationHooks.Log?.Warning("[BlueprintShuffle] found no populated per-level lists yet - will retry.");
            return;
        }

        var appliedCount = 0;
        var newPoolEligible = new HashSet<BuildingType>();

        foreach (var prop in blueprintOrder.Properties())
        {
            if (!Enum.TryParse<LevelType>(prop.Name, out var levelType))
                continue;

            var idx = (int)levelType;
            if (idx < 0 || idx >= byLevel.Length)
                continue;

            var list = byLevel[idx];
            if (list == null)
                continue;

            var newEntries = new List<BuildingInfo>();
            foreach (var token in (JArray)prop.Value)
            {
                var name = token.ToString();
                if (!Enum.TryParse<BuildingType>(name, out var bt) || !lookup.TryGetValue(bt, out var info))
                {
                    LocationHooks.Log?.Warning($"[BlueprintShuffle] level {levelType}: '{name}' not found among this session's building data - skipping it.");
                    continue;
                }

                newEntries.Add(info);
                newPoolEligible.Add(bt);
            }

            // Anything in this level's original list that isn't part of the apworld's pool
            // (buildings not yet tracked - see BlueprintPools.py) stays untouched, trailing
            // after the new order, in its original relative position.
            var trailing = new List<BuildingInfo>();
            for (var j = 0; j < list.Count; j++)
                if (list[j] != null && !newPoolEligible.Contains(list[j].Type))
                    trailing.Add(list[j]);

            var combined = newEntries.Concat(trailing).ToList();
            if (combined.Count != list.Count)
            {
                LocationHooks.Log?.Warning($"[BlueprintShuffle] level {levelType}: slot data count ({combined.Count}) doesn't match this level's list ({list.Count}) - skipping this level.");
                continue;
            }

            for (var j = 0; j < combined.Count; j++)
                list[j] = combined[j];

            appliedCount += newEntries.Count;
            LocationHooks.Log?.Msg($"[BlueprintShuffle] level {levelType}: applied order = {string.Join(", ", newEntries.Select(e => e.Type.ToString()))}");
        }

        if (appliedCount == 0)
        {
            LocationHooks.Log?.Warning("[BlueprintShuffle] slot data present but nothing applied yet - will retry.");
            return;
        }

        foreach (var bt in newPoolEligible)
            PoolEligibleBuildings.Add(bt);

        _applied = true;
        LocationHooks.Log?.Msg($"Applied generation-time blueprint order from slot data ({appliedCount} entries).");
    }

    internal static void ApplyCharHousingOnce(string apSeed)
    {
        if (_charHousingApplied || InfoDB.I == null || string.IsNullOrEmpty(apSeed))
            return;

        var charHousing = InfoDB.I.CharHousing;
        if (charHousing == null)
            return;

        var indices = new List<int>();
        var entries = new List<BuildingInfo>();
        for (var i = 0; i < charHousing.Length; i++)
        {
            if (charHousing[i] == null)
                continue;
            indices.Add(i);
            entries.Add(charHousing[i]);
        }

        if (indices.Count == 0)
        {
            LocationHooks.Log?.Warning("[BlueprintShuffle] CharHousing not populated yet - will retry.");
            return;
        }

        var sorted = entries.ToArray();
        Array.Sort(sorted, (a, b) => string.CompareOrdinal(SortKey(apSeed, "charhousing", a.Type), SortKey(apSeed, "charhousing", b.Type)));

        for (var i = 0; i < indices.Count; i++)
            charHousing[indices[i]] = sorted[i];

        _charHousingApplied = true;
        LocationHooks.Log?.Msg("[BlueprintShuffle] Shuffled CharHousing mapping (" + indices.Count + " entries): " +
            string.Join(", ", indices.Select(idx => $"{(CharType)idx}->{charHousing[idx].Type}")));
    }

    private static string SortKey(string apSeed, string context, BuildingType bt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{apSeed}:{context}:{bt}"));
        return Convert.ToHexString(bytes);
    }
}
