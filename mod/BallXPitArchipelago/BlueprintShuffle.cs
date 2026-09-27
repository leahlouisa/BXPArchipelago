using System;
using System.Collections.Generic;
using System.Linq;
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
/// Also reads (but as of the "major design reconsideration" redesign - see project memory -
/// no longer shuffles) InfoDB.I.CharHousing, the separate mapping from character to the
/// housing building that unlocks them (e.g. vanilla always ties Sheriff's Office -> Itchy
/// Finger) - confirmed live that completing a housing building's character-unlock reads this
/// table directly, independent of the BlueprintsByLevel reveal-cursor split entirely.
/// PopulateCharHousingBuildings just records which buildings are CharHousing-tied (needed by
/// LocationHooks.cs's SuppressibleBuildingTypes) without touching the real vanilla mapping -
/// randomizing which building unlocks which character was a genuinely separate,
/// uncoordinated layer of randomization on top of the generator's own item/location fill,
/// and produced the same hint-confusion problem this whole redesign exists to fix (a hint
/// naming a real building no longer told a player anything true about which character it
/// would unlock). Rules.py's _set_char_housing_rules now gates each "Character: X" location
/// on the matching real "Blueprint: <building>" item instead, resolving the access-rule gap
/// that used to exist here without needing any mod-side computation at all.
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

    private static bool _locationNameOverridesApplied;

    /// <summary>
    /// building -> real location name, for the buildings whose location isn't simply
    /// "Blueprint: {display}" - see LocationNameFor. Populated once from slot data's
    /// "blueprint_location_names" (Rules.py/Locations.py's positional-naming redesign -
    /// items always keep their real name, but pooled and CharHousing-only blueprint
    /// LOCATIONS are positionally named, e.g. "Boneyard pooled blueprint #3").
    /// </summary>
    private static readonly Dictionary<BuildingType, string> LocationNameOverrides = new();

    /// <summary>Call from Mod.OnUpdate() alongside the other slot-data-driven applies.</summary>
    internal static void ApplyLocationNameOverrides(Dictionary<string, object> slotData)
    {
        if (_locationNameOverridesApplied || slotData == null)
            return;

        if (!slotData.TryGetValue("blueprint_location_names", out var raw) || raw is not JObject overrides)
            return;

        foreach (var prop in overrides.Properties())
        {
            if (Enum.TryParse<BuildingType>(prop.Name, out var bt))
                LocationNameOverrides[bt] = prop.Value.ToString();
        }

        _locationNameOverridesApplied = true;
        LocationHooks.Log?.Msg($"[BlueprintShuffle] Loaded {LocationNameOverrides.Count} positional blueprint location name overrides from slot data.");
    }

    /// <summary>The real location name to send a check for when vanilla grants this building.</summary>
    internal static string LocationNameFor(BuildingType bt) =>
        LocationNameOverrides.TryGetValue(bt, out var name) ? name : $"Blueprint: {GameNames.BuildingDisplay(bt)}";

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

        // Strip every slot, not just the one belonging to the level being processed. Two reasons:
        // a building's chain position can be reassigned away from its original home level, so its
        // real identity has to be unreachable everywhere; and since the slot a level's placeholder
        // goes into is no longer that level's own ordinal slot (see SlotIndexFor), "the level's
        // list" is not a meaningful unit to strip any more. Idempotent across retries - kMoonIdol is
        // never pool-eligible, so an already-inserted placeholder always survives this.
        for (var i = 0; i < byLevel.Length; i++)
        {
            var levelList = byLevel[i];
            if (levelList == null)
            {
                anyLevelNotReady = true;
                continue;
            }

            for (var j = levelList.Count - 1; j >= 0; j--)
                if (levelList[j] != null && newPoolEligible.Contains(levelList[j].Type))
                    levelList.RemoveAt(j);
        }

        foreach (var pair in newPending)
        {
            var levelType = pair.Key;
            var queue = pair.Value;

            if (_pending.ContainsKey(levelType))
                continue; // already successfully applied in an earlier attempt.

            var idx = SlotIndexFor(levelType);
            if (idx == null)
            {
                // Difficulty order not loaded yet - a timing issue, so retry rather than fall back
                // to the enum cast, which is exactly the bug this replaced.
                anyLevelNotReady = true;
                continue;
            }

            if (idx < 0 || idx >= byLevel.Length)
                continue; // structural mismatch, not a timing issue - retrying won't help.

            var list = byLevel[idx.Value];
            if (list == null)
            {
                anyLevelNotReady = true;
                continue;
            }

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
        // (reported live).
        //
        // A placeholder is left in each of these two lists as well. Be careful about what that does
        // and does not accomplish, because the obvious reading of it has been tested and is wrong:
        //
        // It does NOT fix the non-pooled CharHousing stall. That was the hypothesis it was written
        // for - a player stuck on Clouds at 6 blueprints remaining with vanilla re-offering
        // Campground every visit - on the reasoning that stripping had taken both lists to literally
        // 0 entries and vanilla was falling through to "any unowned building", of which the 11
        // non-pooled CharHousing buildings are the only ones we never replace. Disproven live: with
        // the placeholder in place and both lists reporting 1 entry, vanilla picked Campground
        // anyway. The earlier per-level theory died the same way - at the moment of a bad pick,
        // Clouds' own list held exactly one entry and it WAS an unowned Void Trophy placeholder at
        // position 0, available and passed over.
        //
        // So vanilla's blueprint selection reads none of the three lists this class rewrites, and
        // the real source is still open (see DebugBlueprintSourceProbe.cs, which calls vanilla's own
        // BuildingMgr.GetAvail* queries instead of guessing).
        //
        // What it DOES do, and why it stays: leaving these lists at 0 entries is a state vanilla is
        // never otherwise in, and any path that does read them now gets a candidate that routes
        // through GainBlueprintLocationPatch into HandleVoidTrophyGrant and advances the level's real
        // chain, rather than escaping the chain or finding nothing. Idempotent by the same argument
        // as the per-level placeholder: HandleVoidTrophyGrant dequeues exactly one pending position
        // per grant regardless of which list the placeholder came from.
        StripGlobalList(InfoDB.I.BossDropBlueprints, newPoolEligible);
        StripGlobalList(InfoDB.I.FuserDropBlueprints, newPoolEligible);
        EnsureVoidTrophyPlaceholder(InfoDB.I.BossDropBlueprints, "BossDropBlueprints");
        EnsureVoidTrophyPlaceholder(InfoDB.I.FuserDropBlueprints, "FuserDropBlueprints");

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
        LocationHooks.SendCheck(LocationNameFor(bt));
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

    /// <summary>
    /// Which InfoDB.I.BlueprintsByLevel slot vanilla reads when the player is in this level.
    ///
    /// That array is ordered by DIFFICULTY, not by LevelType declaration order, and the two differ
    /// for six of the eight levels - only kGraveyard and kSnowy happen to coincide. Using `(int)level`
    /// here (the original code) therefore wrote every other level's placeholder into some unrelated
    /// level's slot, and cost a player a hard, permanent progression stall: kClouds reads kShroom's
    /// slot, kShroom's chain was fully drained, nothing ever refills a drained level's slot, so
    /// completing Clouds could never be offered anything and vanilla fell through to an unowned
    /// non-pooled CharHousing building (Campground) on every single visit.
    ///
    /// Established live via BuildingMgr.GetAvailBlueprintsForLevel, which answers from
    /// BlueprintsByLevel[PositionOf(lt)] for all 8 levels. Two of those answers rule out the enum
    /// cast on their own: kHell and kDesert reported a blueprint available while their own ordinal
    /// slots were EMPTY, and kClouds and kMoon reported nothing available while an unowned Void
    /// Trophy placeholder sat in theirs.
    ///
    /// This is safe to change mid-seed and needs no regeneration. The placeholder is only a sentinel
    /// - every bit of chain meaning lives in _pending (keyed by LevelType) and LevelMgr.I.CurLevel,
    /// so which array slot carries it affects only whether vanilla offers anything at all, never
    /// which check fires or which item is behind it.
    ///
    /// Do NOT conclude from this that the array's CONTENTS are mislabelled - that inference was made
    /// here once and was wrong. BlueprintsByLevel is laid out by LevelType ordinal (proven 2026-09-27
    /// two ways that need no array index: InfoDB.Levels[i].Type == (LevelType)i for all 8, and the
    /// per-ball HeroInfo.reqLevel agrees with UnlocksByLevel read by ordinal for all 7 gated balls).
    /// So BlueprintPools.py's per-level keys are correct and need no relabelling.
    ///
    /// Both facts hold at once: buildings LIVE at their ordinal index, and vanilla LOOKS at the
    /// difficulty-position index. Why vanilla reads a different slot than it fills is still
    /// unexplained, and is a vanilla quirk rather than anything the mod introduced - but the mod only
    /// needs to put its sentinel where vanilla looks, which is what this does, and that is verified
    /// live end to end.
    /// </summary>
    private static int? SlotIndexFor(LevelType level) => LevelUnlockOrder.PositionOf(level);

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
            var idx = SlotIndexFor(level);
            if (idx == null || idx < 0 || idx >= byLevel.Length)
                continue;

            var list = byLevel[idx.Value];
            if (list == null)
                continue;

            if (list.Count > 0 && list[0] != null && list[0].Type == BuildingType.kMoonIdol)
                list.RemoveAt(0);

            if (_pending.TryGetValue(level, out var queue) && queue.Count > 0)
                list.Insert(0, _voidTrophyInfo);
        }

        _needsRefresh.Clear();
    }

    /// <summary>
    /// Makes sure a stripped global list still offers the Void Trophy placeholder, so any path that
    /// does read these lists gets a candidate that routes into our chain instead of finding nothing.
    /// See the call site for why this is a safety net rather than the fix for the non-pooled
    /// CharHousing stall it was originally written for - that hypothesis was tested live and failed.
    ///
    /// Adds at most one: these lists are scanned, not walked positionally like BlueprintsByLevel, so
    /// a single entry is enough and duplicates would just be noise.
    /// </summary>
    private static void EnsureVoidTrophyPlaceholder(
        Il2CppSystem.Collections.Generic.List<BuildingInfo> list, string listName)
    {
        if (list == null || _voidTrophyInfo == null)
            return;

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] != null && list[i].Type == BuildingType.kMoonIdol)
                return;
        }

        list.Add(_voidTrophyInfo);
        LocationHooks.Log?.Msg(
            $"[BlueprintShuffle] Added the Void Trophy placeholder to {listName} ({list.Count} entries) - " +
            "so any path that reads this list feeds the level's chain instead of finding it empty.");
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

    /// <summary>
    /// Records which buildings are CharHousing-tied (LocationHooks.cs's
    /// SuppressibleBuildingTypes needs this to know it's safe to suppress them - one-shot
    /// per-character events, no sequential pool to get stuck in). Deliberately does NOT
    /// touch the real vanilla character->building mapping any more - see this class's doc
    /// comment for why the old runtime shuffle was reverted.
    /// </summary>
    internal static void PopulateCharHousingBuildings()
    {
        if (_charHousingApplied || InfoDB.I == null)
            return;

        var charHousing = InfoDB.I.CharHousing;
        if (charHousing == null)
            return;

        var found = 0;
        for (var i = 0; i < charHousing.Length; i++)
        {
            if (charHousing[i] == null)
                continue;
            CharHousingBuildings.Add(charHousing[i].Type);
            found++;
        }

        if (found == 0)
        {
            LocationHooks.Log?.Warning("[BlueprintShuffle] CharHousing not populated yet - will retry.");
            return;
        }

        _charHousingApplied = true;
        LocationHooks.Log?.Msg($"[BlueprintShuffle] Recorded {found} CharHousing-tied buildings (real vanilla mapping, unmodified).");
    }
}
