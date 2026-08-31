using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;

namespace BallXPitArchipelago;

/// <summary>
/// Last-used connection details, saved after a successful connect so the in-game GUI
/// (ApGui) can prefill its fields on the next launch. Not required to exist - the GUI is
/// the primary way to connect, this is just a convenience so returning players don't have
/// to retype everything.
/// </summary>
public class ApConfig
{
    public string Host { get; set; } = "archipelago.gg";
    public int Port { get; set; } = 38281;
    public string Slot { get; set; } = "";
    public string Password { get; set; }

    private static string ConfigPath
    {
        get
        {
            // MelonLoader's UserData folder, not next to the DLL in Mods\ - a mod update is
            // commonly installed by wiping the Mods folder and extracting a fresh zip into
            // it, which would silently destroy anything stored alongside the DLL there
            // (confirmed live: exactly this happened to BlueprintChainState/ApState's
            // progress-tracking files, forcing this move).
            return Path.Combine(MelonEnvironment.UserDataDirectory, "BallXPitArchipelago.json");
        }
    }

    public static ApConfig Load(MelonLogger.Instance log)
    {
        var path = ConfigPath;

        if (!File.Exists(path))
            return new ApConfig();

        try
        {
            return JsonConvert.DeserializeObject<ApConfig>(File.ReadAllText(path)) ?? new ApConfig();
        }
        catch (Exception e)
        {
            log.Warning($"Failed to parse {path}, ignoring it: {e.Message}");
            return new ApConfig();
        }
    }

    public void Save()
    {
        File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(this, Formatting.Indented));
    }
}
