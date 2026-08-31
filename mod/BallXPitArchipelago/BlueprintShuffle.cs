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
/// Applies the per-level blueprint discovery chain the apworld computed during generation
/// (see Rules.py/BlueprintPools.py). Every position in every level's chain is represented
/// in InfoDB.I.BlueprintsByLevel by a single shared placeholder building - Void Trophy
/// (BuildingType.kMoonIdol), permanently and unconditionally suppressed in
/// LocationHooks.cs - rather than by the real building assigned to that position.
///
/// Why: vanilla's "next undiscovered blueprint for this level" logic walks each level's
/// list sequentially, skipping any entry whose HasBlueprint flag is already true. That flag
/// is *global* per building, not per list-position - if the list held real building
/// identities, a player receiving "Blueprint: Sheriff's Office" from anywhere in the
/// multiworld (another location, another player's gift) BEFORE ever actually killing
/// Boneyard's boss would silently and permanently flip that flag, causing vanilla to skip
/// straight past that position on the next visit. The corresponding check - and whatever
/// another player's critical item happened to be placed there - would never fire, ever
/// again (confirmed live: this is a real, not theoretical, risk). Void Trophy's ownership
/// is never touched by anything except HandleVoidTrophyGrant below, so it can never desync:
/// the mod swaps a fresh placeholder into a level's list the instant the previous one gets
/// consumed, so "does this level still have something to offer" is entirely our own state,
/// decoupled from what the player has actually received and when.
///
/// LevelMgr.I.CurLevel supplies the level context SaveMgr.GainBlueprint's own bt parameter
/// can't provide any more: since every level shares the same placeholder identity, bt alone
/// can no longer tell us which level's queue a given grant belongs to.
///
/// Vanilla also has at least one side channel we never found the exact source of: something
/// grants a level's own original building directly (confirmed live: Boneyard's Sheriff's
/// Office, at first-time level completion, with BlueprintsByLevel/BossDropBlueprints/
/// FuserDropBlueprints/LevelData all ruled out as the source) - GainBlueprintLocationPatch's
/// general suppression branch still catches and blocks the real grant correctly regardless
/// of where it comes from, but HandleSideChannelGrant is what keeps _pending in sync with
/// it: rather than chase down every possible source, it reacts to any pool-eligible grant
/// that didn't come through the Void Trophy chain and removes that building from wherever
/// it's queued, generically covering side channels we haven't identified too.
///
/// List mutation (RemoveAt/Insert) is deferred to Mod.OnUpdate rather than done inside
/// HandleVoidTrophyGrant directly - that runs from inside GainBlueprintLocationPatch's
/// Harmony prefix, i.e. still inside vanilla's own call stack, and mutating the list vanilla
/// is mid-call on risks a "collection modified" exception.
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
    private static BuildingInfo _voidTrophyInfo;
    private static BlueprintChainState _chainState;

    /// <summary>
    /// Every building InfoDB.I.CharHousing ties to a character (e.g. vanilla's Sheriff's
    /// Office -> Itchy Finger), populated once CharHousing is available - independent of
    /// whether ApplyCharHousingOnce has actually shuffled the mapping yet, since membership
    /// in this set doesn't change across the shuffle, only which character each one maps to.
    /// GainBlueprintLocationPatch uses this to suppress these grants: confirmed live (a
    /// player got a real, un-suppressed Cozy Home from beating a level with a new character)
    /// that these are safe to suppress the same way Trophies are - each is a one-shot
    /// per-character event, not a sequentially-walked pool a suppressed grant could get
    /// permanently stuck in (that risk is specific to BlueprintsByLevel's "skip
    /// already-owned" walk, which CharHousing has no equivalent of).
    /// </summary>
    internal static readonly HashSet<BuildingType> CharHousingBuildings = new();

    /// <summary>Per-level queue of buildings still to be offered (canonical location name is derived at send time).</summary>
    private static readonly Dictionary<LevelType, Queue<BuildingType>> _pending = new();

    /// <summary>
    /// True remaining chain length for a level, or null if we don't have one yet (before
    /// ApplyFromSlotData runs). Used to override vanilla's own "Undiscovered Blueprints"
    /// display - see LevelSelectItemInitBlueprintCountPatch. Vanilla's own counter isn't
    /// reliable any more: it decrements whenever ANY GainBlueprint call succeeds while that
    /// level happens to be LevelMgr.I.CurLevel, regardless of which level the granted
    /// building actually belongs to - so a background-applied item for a building assigned
    /// to a different level can decrement the wrong level's display (confirmed live).
    /// </summary>
    internal static int? GetRemainingCount(LevelType level) =>
        _pending.TryGetValue(level, out var queue) ? queue.Count : null;

    /// <summary>Levels whose placeholder was just consumed - list mutation deferred to the next OnUpdate tick.</summary>
    private static readonly HashSet<LevelType> _needsRefresh = new();

    /// <summary>
    /// Every building assigned to some level's chain this seed. GainBlueprintLocationPatch
    /// suppresses these defensively if vanilla ever somehow offers one directly - under
    /// normal play it shouldn't, since none of them are ever written into
    /// InfoDB.I.BlueprintsByLevel any more (Void Trophy stands in for all of them there),
    /// but this keeps "one trigger, one outcome" true even if that assumption is ever wrong.
    /// </summary>
    internal static readonly HashSet<BuildingType> PoolEligibleBuildings = new();

    internal static void ApplyFromSlotData(Dictionary<string, object> slotData, string slot, string seedName)
    {
        if (_applied || InfoDB.I == null || slotData == null)
            return;

        if (!slotData.TryGetValue("blueprint_order", out var raw) || raw is not JObject blueprintOrder)
            return;

        var byLevel = InfoDB.I.BlueprintsByLevel;
        if (byLevel == null)
            return;

        if (!TryFindVoidTrophy())
        {
            LocationHooks.Log?.Warning("[BlueprintShuffle] Void Trophy (kMoonIdol) not found in InfoDB.I.Buildings yet - will retry.");
            return;
        }

        // The per-level queues only live in memory (_pending) - without persisting which
        // positions have already been consumed, a restart/reconnect would rebuild every
        // queue from scratch and silently re-walk through already-completed positions
        // (harmless - SendCheck no-ops on an already-checked location - but confusing to
        // play through, since already-found buildings' names resurface as if new; reported
        // live). ConsumedByLevel[level] buildings are skipped when rebuilding each queue.
        _chainState ??= BlueprintChainState.Load(slot);
        if (_chainState.SeedName != seedName)
        {
            LocationHooks.Log?.Msg($"[BlueprintShuffle] New seed detected for blueprint chain progress (was '{_chainState.SeedName}', now '{seedName}') - resetting.");
            _chainState.SeedName = seedName;
            _chainState.ConsumedByLevel.Clear();

            // Flush immediately rather than waiting for the next MarkConsumed - otherwise,
            // if nothing gets consumed yet this session (confirmed live: a real bug once
            // silently prevented it for an entire session), the file never gets written at
            // all, and a relaunch has nothing to compare against - looking exactly like a
            // fresh seed again even though it's the same one, discarding no real progress
            // only because none had been recorded yet, but masking that anything is wrong.
            _chainState.Save();
        }

        // Two passes: first parse every level's queue and the full global set of buildings
        // this seed's chain claims. A building's ORIGINAL vanilla home level and the level
        // its chain position was shuffled into this seed can differ (the pool is pooled
        // across all 8 levels, not just reordered within one) - so a level's real list has
        // to be stripped of the *global* pool set, not just its own queue's buildings, or
        // a reassigned building would still sit reachable in its old home level too.
        var newPending = new Dictionary<LevelType, Queue<BuildingType>>();
        var newPoolEligible = new HashSet<BuildingType>();

        foreach (var prop in blueprintOrder.Properties())
        {
            if (!Enum.TryParse<LevelType>(prop.Name, out var levelType))
                continue;

            var tokens = (JArray)prop.Value;
            var consumedSet = _chainState.ConsumedByLevel.TryGetValue(prop.Name, out var s) ? s : new HashSet<string>();

            var queue = new Queue<BuildingType>();
            foreach (var token in tokens)
            {
                var name = token.ToString();
                if (!Enum.TryParse<BuildingType>(name, out var bt))
                {
                    LocationHooks.Log?.Warning($"[BlueprintShuffle] level {levelType}: '{name}' is not a known BuildingType - skipping it.");
                    continue;
                }

                // Still counts toward the global strip-set regardless of consumed status -
                // this building's real identity should never sit in any vanilla list for
                // the rest of the seed, whether its position was already found or not.
                newPoolEligible.Add(bt);
                if (!consumedSet.Contains(name))
                    queue.Enqueue(bt);
            }

            newPending[levelType] = queue;
        }

        if (newPending.Count == 0)
        {
            LocationHooks.Log?.Warning("[BlueprintShuffle] slot data present but nothing applied yet - will retry.");
            return;
        }

        // A level whose BlueprintsByLevel entry is still null (not yet lazily populated by
        // the game - confirmed live: happens for a level unlocked for the first time ever,
        // if this method's one-shot run happens to fire before that biome has ever been
        // loaded into) must retry later rather than silently giving up on just that level
        // forever. _applied only ever checks "did at least one level succeed", so without
        // this, a level whose list wasn't ready at this exact moment would never get a real
        // entry in _pending for the rest of the session - matching a real report live
        // (Heaven's "N blueprints remaining" never appeared, and playing it never offered
        // anything, despite the seed having real content queued for it).
        //
        // Critically, a level already present in _pending must be SKIPPED here, not
        // reprocessed - this method gets called again every Mod.OnUpdate() tick for as long
        // as ANY level (e.g. one you simply haven't visited yet, like a still-unvisited
        // final level) remains not-ready, and this loop runs the real InfoDB.I.BlueprintsByLevel
        // mutation (strip + insert a placeholder). Without this guard, an already-succeeded
        // level would get ANOTHER placeholder inserted on every single retry - confirmed
        // live as a real, serious bug (not theoretical): roughly a thousand duplicate
        // placeholders piled up in one level's list over a 9-minute session where a
        // different, unvisited level kept the retry loop alive, which corrupted that
        // level's real blueprint-reward flow badly enough that killing a boss there showed
        // the reward splash but never actually completed the underlying grant.
        var anyLevelNotReady = false;

        foreach (var pair in newPending)
        {
            var levelType = pair.Key;
            var queue = pair.Value;

            if (_pending.ContainsKey(levelType))
                continue; // already successfully applied in an earlier attempt.

            var idx = (int)levelType;
            if (idx < 0 || idx >= byLevel.Length)
                continue; // structural mismatch, not a timing issue - retrying won't help.

            var list = byLevel[idx];
            if (list == null)
            {
                anyLevelNotReady = true;
                continue;
            }

            for (var j = list.Count - 1; j >= 0; j--)
                if (list[j] != null && newPoolEligible.Contains(list[j].Type))
                    list.RemoveAt(j);

            _pending[levelType] = queue;
            if (queue.Count > 0)
                list.Insert(0, _voidTrophyInfo);
        }

        if (anyLevelNotReady)
        {
            LocationHooks.Log?.Warning("[BlueprintShuffle] At least one level's BlueprintsByLevel entry isn't populated yet - will retry.");
            return;
        }

        // InfoDB.I.BossDropBlueprints and .FuserDropBlueprints are separate, GLOBAL (not
        // per-level) lists we never touched - confirmed live that FuserDropBlueprints is a
        // real side channel: vanilla's first-time level-complete bonus grant reads from it
        // directly, completely bypassing BlueprintsByLevel, so a level's own original
        // vanilla building (e.g. Boneyard's Sheriff's Office) could get granted for real
        // outside our sequential chain entirely - suppressed correctly by the general
        // branch in GainBlueprintLocationPatch, but never dequeued from _pending, leaving
        // it permanently "already checked" once the chain eventually reaches that position
        // (reported live). No placeholder needed here, unlike BlueprintsByLevel - these
        // lists aren't part of our sequential chain at all, we just need pool-eligible
        // buildings unreachable through every path that isn't it.
        StripGlobalList(InfoDB.I.BossDropBlueprints, newPoolEligible);
        StripGlobalList(InfoDB.I.FuserDropBlueprints, newPoolEligible);

        foreach (var bt in newPoolEligible)
            PoolEligibleBuildings.Add(bt);

        _applied = true;
        LocationHooks.Log?.Msg($"Applied generation-time blueprint chain from slot data ({newPoolEligible.Count} buildings across {newPending.Count} levels, resuming from saved progress).");
    }

    /// <summary>
    /// Called from GainBlueprintLocationPatch when vanilla tries to grant Void Trophy - i.e.
    /// the front of some level's chain was just reached. Sends the check for whichever real
    /// position that represents and marks the level for a placeholder refresh on the next
    /// OnUpdate tick.
    /// </summary>
    internal static void HandleVoidTrophyGrant()
    {
        var level = LevelMgr.I?.CurLevel;
        if (level == null)
        {
            LocationHooks.Log?.Warning("[BlueprintShuffle] Void Trophy grant fired but LevelMgr.I.CurLevel is unavailable - ignoring.");
            return;
        }

        if (!_pending.TryGetValue(level.Value, out var queue) || queue.Count == 0)
        {
            LocationHooks.Log?.Warning($"[BlueprintShuffle] Void Trophy grant fired for {level.Value} but nothing is pending there - ignoring.");
            return;
        }

        var bt = queue.Dequeue();
        LocationHooks.SendCheck($"Blueprint: {GameNames.BuildingDisplay(bt)}");
        _needsRefresh.Add(level.Value);
        MarkConsumed(level.Value, bt);
    }

    /// <summary>
    /// Called from GainBlueprintLocationPatch's general suppression branch whenever vanilla
    /// tries to grant a pool-eligible building directly (i.e. bt != kMoonIdol - some other
    /// path than the Void Trophy chain). Confirmed live that at least one such side channel
    /// exists (vanilla's first-time level-complete bonus grants a level's original building
    /// - e.g. Boneyard's Sheriff's Office - through something that isn't BlueprintsByLevel,
    /// BossDropBlueprints, or FuserDropBlueprints; the actual source was never found despite
    /// checking all three plus LevelData's completion-tracking fields), and there may be
    /// others we don't know about. Rather than keep hunting individual sources, this reacts
    /// generically to any such grant: if the building is still sitting in some level's
    /// pending queue, remove it from wherever it is (not just the front) and mark it
    /// consumed, so the chain never wastes a future pickup re-discovering something vanilla
    /// already resolved through a side channel (confirmed live: it otherwise sits at the
    /// front of the queue until reached naturally, showing up as an "already checked"
    /// no-op that silently costs the player a pickup for nothing).
    /// </summary>
    internal static void HandleSideChannelGrant(BuildingType bt)
    {
        foreach (var pair in _pending)
        {
            if (!pair.Value.Contains(bt))
                continue;

            _pending[pair.Key] = new Queue<BuildingType>(pair.Value.Where(b => b != bt));
            MarkConsumed(pair.Key, bt);
            LocationHooks.Log?.Msg($"[BlueprintShuffle] {bt} was granted through a side channel (not the Void Trophy chain) - removed it from {pair.Key}'s pending queue.");
            return;
        }
    }

    private static void MarkConsumed(LevelType level, BuildingType bt)
    {
        if (_chainState == null)
            return;

        var key = level.ToString();
        if (!_chainState.ConsumedByLevel.TryGetValue(key, out var set))
        {
            set = new HashSet<string>();
            _chainState.ConsumedByLevel[key] = set;
        }

        set.Add(bt.ToString());
        _chainState.Save();
    }

    /// <summary>Call from Mod.OnUpdate() - performs the list mutation HandleVoidTrophyGrant deferred.</summary>
    internal static void ProcessPendingRefreshes()
    {
        if (_needsRefresh.Count == 0 || InfoDB.I == null)
            return;

        var byLevel = InfoDB.I.BlueprintsByLevel;
        if (byLevel == null)
            return;

        foreach (var level in _needsRefresh)
        {
            var idx = (int)level;
            if (idx < 0 || idx >= byLevel.Length)
                continue;

            var list = byLevel[idx];
            if (list == null)
                continue;

            if (list.Count > 0 && list[0] != null && list[0].Type == BuildingType.kMoonIdol)
                list.RemoveAt(0);

            if (_pending.TryGetValue(level, out var queue) && queue.Count > 0)
                list.Insert(0, _voidTrophyInfo);
        }

        _needsRefresh.Clear();
    }

    private static void StripGlobalList(Il2CppSystem.Collections.Generic.List<BuildingInfo> list, HashSet<BuildingType> poolEligible)
    {
        if (list == null)
            return;

        var removed = 0;
        for (var j = list.Count - 1; j >= 0; j--)
        {
            if (list[j] != null && poolEligible.Contains(list[j].Type))
            {
                list.RemoveAt(j);
                removed++;
            }
        }

        if (removed > 0)
            LocationHooks.Log?.Msg($"[BlueprintShuffle] Stripped {removed} pool-eligible entries from a global blueprint list ({list.Count} remaining).");
    }

    private static bool TryFindVoidTrophy()
    {
        if (_voidTrophyInfo != null)
            return true;

        var buildings = InfoDB.I.Buildings;
        if (buildings == null)
            return false;

        for (var i = 0; i < buildings.Length; i++)
        {
            if (buildings[i] != null && buildings[i].Type == BuildingType.kMoonIdol)
            {
                _voidTrophyInfo = buildings[i];
                return true;
            }
        }

        return false;
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
            CharHousingBuildings.Add(charHousing[i].Type);
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
