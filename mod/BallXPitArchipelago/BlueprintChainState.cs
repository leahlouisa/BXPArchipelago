using System.Collections.Generic;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace BallXPitArchipelago;

/// <summary>
/// Small local sidecar file tracking which chain positions (by BuildingType token) have
/// been consumed per level. BlueprintShuffle's per-level queues only live in memory
/// otherwise - without this, every process restart or reconnect rebuilds each level's queue
/// from scratch, silently re-walking through already-completed positions before reaching
/// new ground. That's harmless in isolation (LocationHooks.SendCheck no-ops on an
/// already-checked location, so nothing gets double-granted) but confusing to play through,
/// since already-found buildings' canonical names resurface as if they were new (reported
/// live).
///
/// A set per level, not a count: confirmed live that vanilla can grant a pool-eligible
/// building through side channels we don't control (e.g. a first-time level-complete bonus
/// - see BlueprintShuffle.HandleSideChannelGrant), which can consume any position in the
/// queue, not just the front one a plain "how many from the start" count could represent.
/// </summary>
public class BlueprintChainState
{
    public string Slot { get; set; } = "";
    public string SeedName { get; set; } = "";
    public Dictionary<string, HashSet<string>> ConsumedByLevel { get; set; } = new();

    private static string PathFor(string slot)
    {
        // MelonLoader's UserData folder, not next to the DLL in Mods\ - see ApConfig.cs's
        // ConfigPath for why (a mod update commonly wipes the Mods folder, which would
        // otherwise destroy this progress-tracking file - confirmed live: exactly this
        // deleted a player's blueprint-chain progress on every DLL update, making every
        // relaunch look like a brand-new seed with nothing ever consumed).
        var safeSlot = string.Concat(slot.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(MelonEnvironment.UserDataDirectory, $"BallXPitArchipelago.{safeSlot}.blueprintchain.json");
    }

    public static BlueprintChainState Load(string slot)
    {
        var path = PathFor(slot);
        if (!File.Exists(path))
            return new BlueprintChainState { Slot = slot };

        try
        {
            return JsonConvert.DeserializeObject<BlueprintChainState>(File.ReadAllText(path)) ?? new BlueprintChainState { Slot = slot };
        }
        catch
        {
            return new BlueprintChainState { Slot = slot };
        }
    }

    public void Save()
    {
        File.WriteAllText(PathFor(Slot), JsonConvert.SerializeObject(this, Formatting.Indented));
    }
}
