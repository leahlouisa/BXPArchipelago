using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(BallXPitArchipelago.Mod), "Ball X Pit Archipelago", "0.1.2", "leahlouisa")]
[assembly: MelonGame("Kenny Sun", "BALL x PIT")]

namespace BallXPitArchipelago;

public class Mod : MelonMod
{
    public override void OnInitializeMelon()
    {
        LocationHooks.Log = LoggerInstance;

        ClassInjector.RegisterTypeInIl2Cpp<ApGui>();
        ApGui.Init(ApConfig.Load(LoggerInstance));

        var guiObject = new GameObject("BallXPitArchipelago GUI");
        guiObject.AddComponent<ApGui>();
        UnityEngine.Object.DontDestroyOnLoad(guiObject);
    }

    public override void OnApplicationQuit()
    {
        ApConnection.Disconnect();
    }

    private int _frameCounter;

    public override void OnUpdate()
    {
        // Cheap throttle: only worth checking a few times a second.
        if (++_frameCounter % 30 != 0)
            return;

        LocationHooks.PollForChanges();

        if (ApConnection.Session != null)
        {
            ItemReceiver.RetryPending(ApConnection.Session.Items);
            DeathLinkHandler.ProcessPending();
            ItemSendNotifier.ProcessPending();
            BlueprintShuffle.ApplyFromSlotData(ApConnection.SlotData, ApConnection.SlotName, ApConnection.Session.RoomState.Seed);
            BlueprintShuffle.ApplyCharHousingOnce(ApConnection.Session.RoomState.Seed);
            LevelUnlockOrder.ApplyFromSlotData(ApConnection.SlotData);
            BlueprintShuffle.ProcessPendingRefreshes();
        }
    }
}
