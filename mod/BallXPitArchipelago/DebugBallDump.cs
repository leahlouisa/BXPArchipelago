#if DEBUG
using Il2Cpp;
using Il2CppI2.Loc;
using Il2CppInterop.Runtime;

namespace BallXPitArchipelago;

/// <summary>
/// The ground-truth dump behind evosanity's ball data. Not part of the shipped mod - guarded
/// behind #if DEBUG so it can never compile into a Release build regardless of whether anyone
/// remembers to strip the file before packaging.
///
/// KEEP THIS. It was originally written as one-shot investigation tooling, but it's now the only
/// way to regenerate game_data.json's "balls" section, and the only way to notice the roster
/// changing under us - MinVersion shows 30 of the 90 balls were added after launch, so another
/// update adding more is likely. To refresh: build Debug, run the game once, then
/// `py -3.12 shared/gen_balls_from_dump.py <MelonLoader log>` followed by
/// `py -3.12 shared/gen_ball_names.py`.
///
/// Two jobs, both pure reads - no Harmony patches at all, on anything. That's deliberate:
/// everything here is reachable by walking InfoDB/MetaSaveData singletons directly, so there's
/// no reason to go anywhere near the merge path (UpgradeMgr.CombineHeroes / HeroInst.CombineWith)
/// and repeat the SaveMgr.SpendResources reentrancy disaster (see DebugHooks.cs's removal note).
///
///   1. DumpOnce() - a one-shot walk of InfoDB.I.Heroes, emitting per ball: its HeroType, real
///      display name (and localization slug + resolved translation, since enum tokens demonstrably
///      don't reverse-parse into real names - the wiki's "Iron" is kHeavy), IsInGame/IncludeInGame,
///      IsMerged (base vs evolved), MinVersion (free-DLC gating), GetRequiredLevel (which biome
///      gates it), and the full MergeComponents recipe. MergeComponents is UpgradeInfo[][] - an
///      array of SLOTS, each slot holding the acceptable ALTERNATIVES for that slot (which is how
///      the wiki gets recipes like "Assassin = Iron + (Ghost or Dark)"), so each slot is logged
///      as its own line rather than flattened.
///
///   2. PollStatChanges() - watches MetaSaveData.I.HeroStats for NumObtained/NumCombos/NumUpgraded
///      transitions and logs each one along with GameMgr.I.CurState. This is the experiment for
///      "does HeroStats update the moment you evolve something, or in a batch at run end?" - a
///      change logged at CurState=kLevelUp means immediate; changes that only appear at
///      kGameOver/kEndingGame (or with no GameMgr at all, i.e. back at the base) means batched.
///      Also confirms the array is length HeroType.kNum and indexed by HeroType ordinal, which
///      the whole polling design for evosanity checks depends on.
///
/// Every log line is prefixed [BALLDUMP] and pipe-delimited so the output can be lifted straight
/// out of MelonLoader's log and parsed into game_data.json without hand-transcription.
/// </summary>
internal static class DebugBallDump
{
    private static bool _dumped;
    private static int[] _lastObtained;
    private static int[] _lastUpgraded;

    /// <summary>
    /// HeroMetaStats.NumCombos is NOT a scalar - it's an Il2CppStructArray&lt;int&gt; indexed by
    /// the OTHER ball's HeroType, i.e. HeroStats[a].NumCombos[b] counts how many times ball a was
    /// combined with ball b. 89 x 89 = 7,921, exactly the wiki's "potential Fused Balls" figure,
    /// so this is vanilla's fusion-pair ledger (relevant if a "fusionsanity" ever comes up; not
    /// used by evosanity). Watching a 90x90 matrix per tick isn't worth it, so the watcher tracks
    /// each ball's row SUM and dumps that row's nonzero entries when the sum moves.
    /// </summary>
    private static int[] _lastCombosSum;

    private const string Tag = "[BALLDUMP]";

    /// <summary>
    /// Called unconditionally from Mod.OnUpdate() - deliberately NOT behind the
    /// ApConnection.Session gate, since none of this needs (or wants) a live AP session: the
    /// point is to read vanilla's own data, and requiring a connected session would mean
    /// needing a generated seed just to dump static game data.
    /// </summary>
    internal static void Tick()
    {
        DumpOnce();
        PollStatChanges();
    }

    private static void DumpOnce()
    {
        if (_dumped || InfoDB.I == null)
            return;

        var heroes = InfoDB.I.Heroes;
        if (heroes == null)
            return;

        _dumped = true;
        var log = LocationHooks.Log;

        log?.Msg($"{Tag} === begin === HeroType.kNum={(int)HeroType.kNum} Heroes={heroes.Length} " +
                 $"BaseHeroes={InfoDB.I.BaseHeroes?.Length ?? -1} HeroOrder={InfoDB.I.HeroOrder?.Count ?? -1}");

        for (var i = 0; i < heroes.Length; i++)
        {
            var h = heroes[i];
            if (h == null)
            {
                log?.Msg($"{Tag} HERO|idx={i}|<null>");
                continue;
            }

            log?.Msg(
                $"{Tag} HERO|idx={i}" +
                $"|type={Safe(() => h.Type.ToString())}" +
                $"|name={Safe(() => h.Name)}" +
                $"|slug={Safe(() => h.GetNameSlug())}" +
                $"|loc={Safe(() => Translate(h.GetNameSlug()))}" +
                $"|isInGame={Safe(() => h.IsInGame.ToString())}" +
                $"|includeInGame={Safe(() => h.IncludeInGame().ToString())}" +
                $"|isMerged={Safe(() => h.IsMerged().ToString())}" +
                $"|minVersion={Safe(() => h.MinVersion.ToString())}" +
                $"|reqLevel={Safe(() => h.GetRequiredLevel().ToString())}" +
                $"|numMergeComponents={Safe(() => h.GetNumMergeComponents().ToString())}");

            DumpRecipe(h);
            DumpEvolutions(h);
        }

        DumpBaseHeroes();
        DumpUnlocksByLevel();
        DumpStatsBaseline();

        log?.Msg($"{Tag} === end ===");
    }

    /// <summary>
    /// CONFIRMED BY THE FIRST REAL DUMP - MergeComponents is an array of COMPLETE ALTERNATIVE
    /// RECIPES, not "slots of interchangeable alternatives" (the initial guess, now disproven).
    /// Each entry lists every ingredient that one recipe needs; multiple entries mean the ball
    /// has multiple ways to be made. kAssassin has [kHeavy,kGhost] and [kHeavy,kDark], i.e.
    /// "Iron+Ghost OR Iron+Dark". GetNumMergeComponents() is the ingredient count per recipe
    /// (2 for 67 of the 69 evolved balls, 3 for kNosferatu, 4 for kElemental), NOT the recipe
    /// count. So an access rule is: cheapest recipe wins (min across entries), and within the
    /// chosen recipe every ingredient is required (AND across its contents).
    ///
    /// The log field is still named "slot" for continuity with dumps already captured; read it
    /// as "recipe variant index".
    /// </summary>
    private static void DumpRecipe(HeroInfo h)
    {
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UpgradeInfo>> slots;
        try
        {
            slots = h.MergeComponents;
        }
        catch (Exception e)
        {
            LocationHooks.Log?.Msg($"{Tag}   SLOT|type={h.Type}|<ERR reading MergeComponents: {e.Message}>");
            return;
        }

        if (slots == null || slots.Length == 0)
            return;

        for (var s = 0; s < slots.Length; s++)
        {
            var alts = slots[s];
            var names = alts == null ? "<null>" : DescribeUpgrades(alts);
            LocationHooks.Log?.Msg($"{Tag}   SLOT|type={h.Type}|slot={s}|alts={names}");
        }
    }

    private static void DumpEvolutions(HeroInfo h)
    {
        try
        {
            var evos = h.Evolutions;
            if (evos == null || evos.Count == 0)
                return;

            var parts = new List<string>();
            for (var i = 0; i < evos.Count; i++)
                parts.Add(Describe(evos[i]));

            LocationHooks.Log?.Msg($"{Tag}   EVOLVESINTO|type={h.Type}|into={string.Join(",", parts)}");
        }
        catch (Exception e)
        {
            LocationHooks.Log?.Msg($"{Tag}   EVOLVESINTO|type={h.Type}|<ERR: {e.Message}>");
        }
    }

    private static void DumpBaseHeroes()
    {
        try
        {
            var baseHeroes = InfoDB.I.BaseHeroes;
            if (baseHeroes == null)
                return;

            var parts = new List<string>();
            for (var i = 0; i < baseHeroes.Length; i++)
                parts.Add(baseHeroes[i] == null ? "<null>" : Safe(() => baseHeroes[i].Type.ToString()));

            LocationHooks.Log?.Msg($"{Tag} BASEHEROES|count={baseHeroes.Length}|types={string.Join(",", parts)}");
        }
        catch (Exception e)
        {
            LocationHooks.Log?.Msg($"{Tag} BASEHEROES|<ERR: {e.Message}>");
        }
    }

    /// <summary>
    /// InfoDB.UnlocksByLevel is List&lt;UpgradeInfo&gt;[] - assumed indexed by LevelType ordinal,
    /// which this dump is partly here to confirm. Contains passives as well as balls (it's the
    /// whole per-level unlock set), so each entry is labelled with its real UpgradeType rather
    /// than assumed to be a ball.
    /// </summary>
    private static void DumpUnlocksByLevel()
    {
        try
        {
            var byLevel = InfoDB.I.UnlocksByLevel;
            if (byLevel == null)
            {
                LocationHooks.Log?.Msg($"{Tag} UNLOCKSBYLEVEL|<null>");
                return;
            }

            for (var i = 0; i < byLevel.Length; i++)
            {
                var list = byLevel[i];
                var levelName = i < (int)LevelType.kNum ? ((LevelType)i).ToString() : $"<out of range {i}>";
                if (list == null)
                {
                    LocationHooks.Log?.Msg($"{Tag} UNLOCKSBYLEVEL|idx={i}|level={levelName}|<null>");
                    continue;
                }

                var parts = new List<string>();
                for (var j = 0; j < list.Count; j++)
                    parts.Add(Describe(list[j]));

                LocationHooks.Log?.Msg(
                    $"{Tag} UNLOCKSBYLEVEL|idx={i}|level={levelName}|count={list.Count}|entries={string.Join(",", parts)}");
            }
        }
        catch (Exception e)
        {
            LocationHooks.Log?.Msg($"{Tag} UNLOCKSBYLEVEL|<ERR: {e.Message}>");
        }
    }

    /// <summary>
    /// Baseline snapshot of MetaSaveData.I.HeroStats. Only nonzero entries are listed (a fresh
    /// save would print none) - the array length is the important part, since evosanity's whole
    /// detection design assumes HeroStats[(int)someHeroType] is that ball's record.
    /// </summary>
    private static void DumpStatsBaseline()
    {
        var stats = MetaSaveData.I?.HeroStats;
        if (stats == null)
        {
            LocationHooks.Log?.Msg($"{Tag} STATS|<no MetaSaveData/HeroStats yet - will report on first change>");
            return;
        }

        LocationHooks.Log?.Msg($"{Tag} STATS|len={stats.Length}|expected={(int)HeroType.kNum}");

        for (var i = 0; i < stats.Length; i++)
        {
            var s = stats[i];
            if (s == null)
                continue;

            var comboSum = CombosSum(s);
            if (s.NumObtained == 0 && s.NumUpgraded == 0 && comboSum == 0)
                continue;

            LocationHooks.Log?.Msg(
                $"{Tag} STATS|idx={i}|type={TypeNameFor(i)}|numObtained={s.NumObtained}" +
                $"|numUpgraded={s.NumUpgraded}|numCompletedRuns={s.NumCompletedRuns}" +
                $"|totalLaunches={s.TotalLaunches}|totalDamage={s.TotalDamage}" +
                $"|comboRowLen={s.NumCombos?.Length ?? -1}|comboSum={comboSum}|combos={DescribeCombos(s)}");
        }
    }

    /// <summary>Sum of a ball's fusion-pair row, or 0 if the row is missing.</summary>
    private static int CombosSum(HeroMetaStats s)
    {
        try
        {
            var row = s.NumCombos;
            if (row == null)
                return 0;

            var total = 0;
            for (var i = 0; i < row.Length; i++)
                total += row[i];
            return total;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Only the nonzero entries of a ball's fusion-pair row, as "kOther=count" pairs.</summary>
    private static string DescribeCombos(HeroMetaStats s)
    {
        try
        {
            var row = s.NumCombos;
            if (row == null)
                return "<null>";

            var parts = new List<string>();
            for (var i = 0; i < row.Length; i++)
            {
                if (row[i] != 0)
                    parts.Add($"{TypeNameFor(i)}={row[i]}");
            }

            return parts.Count == 0 ? "<none>" : string.Join(",", parts);
        }
        catch (Exception e)
        {
            return $"<ERR: {e.Message}>";
        }
    }

    /// <summary>
    /// The "when does HeroStats actually update?" experiment. Runs every Mod.OnUpdate tick (which
    /// is already throttled to a few times a second) and logs any NumObtained/NumCombos/NumUpgraded
    /// transition together with GameMgr.I.CurState, so the timing is unambiguous from the log alone.
    /// </summary>
    private static void PollStatChanges()
    {
        var stats = MetaSaveData.I?.HeroStats;
        if (stats == null)
            return;

        if (_lastObtained == null || _lastObtained.Length != stats.Length)
        {
            _lastObtained = new int[stats.Length];
            _lastUpgraded = new int[stats.Length];
            _lastCombosSum = new int[stats.Length];

            for (var i = 0; i < stats.Length; i++)
            {
                var s = stats[i];
                if (s == null)
                    continue;
                _lastObtained[i] = s.NumObtained;
                _lastUpgraded[i] = s.NumUpgraded;
                _lastCombosSum[i] = CombosSum(s);
            }

            LocationHooks.Log?.Msg($"{Tag} WATCH|baseline established for {stats.Length} entries");
            return;
        }

        for (var i = 0; i < stats.Length; i++)
        {
            var s = stats[i];
            if (s == null)
                continue;

            var comboSum = CombosSum(s);
            if (s.NumObtained == _lastObtained[i] && s.NumUpgraded == _lastUpgraded[i] && comboSum == _lastCombosSum[i])
                continue;

            var state = GameMgr.I == null ? "<no GameMgr (out of battle)>" : Safe(() => GameMgr.I.CurState.ToString());

            LocationHooks.Log?.Msg(
                $"{Tag} CHANGE|idx={i}|type={TypeNameFor(i)}|gameState={state}" +
                $"|numObtained={_lastObtained[i]}->{s.NumObtained}" +
                $"|numUpgraded={_lastUpgraded[i]}->{s.NumUpgraded}" +
                $"|comboSum={_lastCombosSum[i]}->{comboSum}|combos={DescribeCombos(s)}");

            _lastObtained[i] = s.NumObtained;
            _lastUpgraded[i] = s.NumUpgraded;
            _lastCombosSum[i] = comboSum;
        }
    }

    private static string DescribeUpgrades(
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UpgradeInfo> arr)
    {
        var parts = new List<string>();
        for (var i = 0; i < arr.Length; i++)
            parts.Add(Describe(arr[i]));
        return string.Join(",", parts);
    }

    /// <summary>
    /// An UpgradeInfo in a recipe slot / unlock list may be a ball, a passive, or a pet upgrade.
    /// TryCast is a managed-side type check (no IL2CPP call that could throw), so a ball gets
    /// named by its HeroType - the stable identifier we'd key game_data.json on - and anything
    /// else falls back to its real name plus its UpgradeType, so passive entries are obvious
    /// rather than silently mixed in with balls.
    /// </summary>
    private static string Describe(UpgradeInfo u)
    {
        if (u == null)
            return "<null>";

        var hero = u.TryCast<HeroInfo>();
        if (hero != null)
            return Safe(() => hero.Type.ToString());

        return Safe(() => $"{u.Name}({u.GetUpgradeType()})");
    }

    private static string TypeNameFor(int idx) =>
        idx >= 0 && idx < (int)HeroType.kNum ? ((HeroType)idx).ToString() : $"<out of range {idx}>";

    private static string Translate(string slug)
    {
        if (string.IsNullOrEmpty(slug))
            return "<no slug>";
        var t = LocalizationManager.GetTranslation(slug);
        return string.IsNullOrEmpty(t) ? "<untranslated>" : t;
    }

    /// <summary>
    /// Every field/method access here crosses into IL2CPP, where a null internal reference or an
    /// unexpected type surfaces as a managed exception. One bad ball shouldn't abort the dump for
    /// the other 89, so each value is fetched independently and failures are recorded inline.
    /// </summary>
    private static string Safe(Func<string> f)
    {
        try
        {
            return f() ?? "<null>";
        }
        catch (Exception e)
        {
            return $"<ERR {e.GetType().Name}: {e.Message}>";
        }
    }
}
#endif
