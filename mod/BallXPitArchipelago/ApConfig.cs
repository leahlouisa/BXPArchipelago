using System.Reflection;
using MelonLoader;
using Newtonsoft.Json;

namespace BallXPitArchipelago;

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

    /// <summary>
    /// Loads BallXPitArchipelago.json from next to this DLL. If it doesn't exist yet,
    /// writes a template with an empty Slot (which is treated as "not configured") and
    /// returns null so the caller can skip connecting.
    /// </summary>
    public static ApConfig Load(MelonLogger.Instance log)
    {
        var path = ConfigPath;

        if (!File.Exists(path))
        {
            var template = new ApConfig();
            File.WriteAllText(path, JsonConvert.SerializeObject(template, Formatting.Indented));
            log.Msg($"Wrote a template config to {path}");
            return null;
        }

        ApConfig config;
        try
        {
            config = JsonConvert.DeserializeObject<ApConfig>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            log.Error($"Failed to parse {path}: {e.Message}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(config?.Slot))
        {
            log.Warning($"{path} has no Slot set; not connecting.");
            return null;
        }

        return config;
    }
}
