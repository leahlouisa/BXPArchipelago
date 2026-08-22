using MelonLoader;

[assembly: MelonInfo(typeof(BallXPitArchipelago.Mod), "Ball X Pit Archipelago", "0.1.0", "leahlouisa")]
[assembly: MelonGame("Kenny Sun", "BALL x PIT")]

namespace BallXPitArchipelago;

public class Mod : MelonMod
{
    public override void OnInitializeMelon()
    {
        var config = ApConfig.Load(LoggerInstance);
        if (config == null)
        {
            LoggerInstance.Warning("No Archipelago config found; the mod will stay idle. " +
                                    "Fill in BallXPitArchipelago.json next to this DLL and restart the game.");
            return;
        }

        ApConnection.Connect(config, LoggerInstance);
    }

    public override void OnApplicationQuit()
    {
        ApConnection.Disconnect();
    }

    private int _frameCounter;

    public override void OnUpdate()
    {
        // Cheap throttle: only worth checking a few times a second.
        if (ApConnection.Session == null || ++_frameCounter % 30 != 0)
            return;

        ItemReceiver.RetryPending(ApConnection.Session.Items);
    }
}
