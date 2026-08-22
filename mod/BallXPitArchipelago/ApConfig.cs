using System.Reflection;
using MelonLoader;
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
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            return Path.Combine(dir, "BallXPitArchipelago.json");
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
