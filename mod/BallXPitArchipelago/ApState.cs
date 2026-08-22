using System.Reflection;
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
    public int AppliedItemCount { get; set; }

    private static string PathFor(string slot)
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        var safeSlot = string.Concat(slot.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(dir, $"BallXPitArchipelago.{safeSlot}.state.json");
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
