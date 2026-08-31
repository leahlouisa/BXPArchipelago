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
