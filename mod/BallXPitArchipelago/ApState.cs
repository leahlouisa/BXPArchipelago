using MelonLoader.Utils;
using Newtonsoft.Json;

namespace BallXPitArchipelago;

/// <summary>
/// Small local sidecar file tracking how many items we've already applied for a given
/// slot. The Archipelago server replays a slot's entire item history on every connect,
/// so without this we'd re-grant every item (duplicating resources, etc.) on each launch.
/// </summary>
public class ApState
{
    public string Slot { get; set; } = "";
    public string SeedName { get; set; } = "";
    public int AppliedItemCount { get; set; }

    /// <summary>
    /// Whether evosanity_jumpstart's one-time resource grant has already been applied for this
    /// seed. The 12 jumpstart BUILDINGS need no tracking of their own - they're precollected AP
    /// items, so AppliedItemCount already covers them - but the resources aren't items at all
    /// (see the apworld's fill_slot_data), so without this they'd be re-granted on every launch.
    /// Reset alongside AppliedItemCount when a new seed is detected, since a new seed means a new
    /// save and a fresh entitlement to the grant.
    /// </summary>
    public bool JumpstartApplied { get; set; }

    /// <summary>
    /// Run-scoped rewards received but not yet delivered, because they only mean anything inside
    /// a run (see RunScopedRewards.cs). Persisted rather than held in memory so quitting between
    /// receiving one and starting a run doesn't silently eat it.
    ///
    /// Counters, not a queue: the two kinds are independent and order between them doesn't
    /// matter. Not reset on a new seed the way AppliedItemCount is - a pending reward is a debt
    /// already incurred, and the cursor reset would re-deliver the items that created it anyway,
    /// so zeroing here as well would double-count. ItemReceiver clears them explicitly instead.
    /// </summary>
    public int PendingLevelUps { get; set; }

    public int PendingFusers { get; set; }

    private static string PathFor(string slot)
    {
        // MelonLoader's UserData folder, not next to the DLL in Mods\ - see ApConfig.cs's
        // ConfigPath for why (a mod update commonly wipes the Mods folder, which would
        // otherwise destroy this progress-tracking file - confirmed live).
        var safeSlot = string.Concat(slot.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(MelonEnvironment.UserDataDirectory, $"BallXPitArchipelago.{safeSlot}.state.json");
    }

    public static ApState Load(string slot)
    {
        var path = PathFor(slot);
        if (!File.Exists(path))
            return new ApState { Slot = slot };

        try
        {
            return JsonConvert.DeserializeObject<ApState>(File.ReadAllText(path)) ?? new ApState { Slot = slot };
        }
        catch
        {
            return new ApState { Slot = slot };
        }
    }

    public void Save()
    {
        File.WriteAllText(PathFor(Slot), JsonConvert.SerializeObject(this, Formatting.Indented));
    }
}
