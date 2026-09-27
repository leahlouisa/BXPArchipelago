using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(BallXPitArchipelago.Mod), "Ball X Pit Archipelago", "0.3.1", "leahlouisa")]
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

#if DEBUG
        // Deliberately outside the session gate - the evosanity data dump reads vanilla's own
        // InfoDB/MetaSaveData and shouldn't need a generated seed just to run. Debug-only.
        DebugBallDump.Tick();
        DebugBlueprintListDump.Tick();
#endif

        if (ApConnection.Session != null)
        {
            ItemReceiver.RetryPending(ApConnection.Session.Items);
            DeathLinkHandler.ProcessPending();
            ItemSendNotifier.ProcessPending();
            // Before BlueprintShuffle - it needs the difficulty order to pick the right
            // BlueprintsByLevel slot (see BlueprintShuffle.SlotIndexFor).
            LevelUnlockOrder.ApplyFromSlotData(ApConnection.SlotData);
            BlueprintShuffle.ApplyLocationNameOverrides(ApConnection.SlotData);
            BlueprintShuffle.ApplyFromSlotData(ApConnection.SlotData, ApConnection.SlotName, ApConnection.Session.RoomState.Seed);
            BlueprintShuffle.PopulateCharHousingBuildings();
            BlueprintShuffle.ProcessPendingRefreshes();
            EconomyOptions.ApplyBuildingCostScaling();
            // Delivers at most one queued run-scoped reward per tick, and only mid-run - see
            // RunScopedRewards.cs for why they're queued rather than applied on receipt.
            RunScopedRewards.Drain();
        }
    }
}
