using System;
using System.Collections.Generic;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Models;
using MelonLoader;

namespace BallXPitArchipelago;

public static class ApConnection
{
    /// <summary>
    /// Must match the `game` string the ballxpit apworld registers under.
    /// </summary>
    public const string GameName = "Ball x Pit";

    public static ArchipelagoSession Session { get; private set; }
    public static string SlotName { get; private set; }
    public static Dictionary<string, object> SlotData { get; private set; }
    public static DeathLinkService DeathLinkService { get; private set; }
    public static bool DeathLinkEnabled { get; private set; }

    /// <summary>Attempts to connect. Returns null on success, or an error message to show the player.</summary>
    public static string Connect(ApConfig config, MelonLogger.Instance log)
    {
        ArchipelagoSession session;
        try
        {
            session = ArchipelagoSessionFactory.CreateSession(config.Host, config.Port);
        }
        catch (Exception e)
        {
            var msg = $"Could not create a session for {config.Host}:{config.Port}: {e.Message}";
            log.Error(msg);
            return msg;
        }

        LoginResult result;
        try
        {
            result = session.TryConnectAndLogin(
                GameName,
                config.Slot,
                ItemsHandlingFlags.AllItems,
                password: config.Password);
        }
        catch (Exception e)
        {
            var msg = $"Connection to {config.Host}:{config.Port} failed: {e.Message}";
            log.Error(msg);
            return msg;
        }

        if (!result.Successful)
        {
            var failure = (LoginFailure)result;
            log.Error($"Failed to connect to {config.Host}:{config.Port} as {config.Slot}:");
            foreach (var error in failure.Errors)
                log.Error($"    {error}");
            foreach (var errorCode in failure.ErrorCodes)
                log.Error($"    {errorCode}");
            return string.Join("\n", failure.Errors);
        }

        Session = session;
        SlotName = config.Slot;
        var success = (LoginSuccessful)result;
        SlotData = success.SlotData;
        log.Msg($"Connected to {config.Host}:{config.Port} as {config.Slot} " +
                $"(team {success.Team}, slot {success.Slot}).");

        // InfoDB may not be ready this early (it wasn't for the equivalent debug dump - see
        // project memory) - Mod.OnUpdate() retries via BlueprintShuffle.ApplyFromSlotData()
        // the same way ItemReceiver retries pending items, so a no-op here just means it
        // applies a little later instead of failing.
        BlueprintShuffle.ApplyFromSlotData(success.SlotData);
        BlueprintShuffle.ApplyCharHousingOnce(session.RoomState.Seed);

        // No session.Items.ItemReceived subscription: that event fires on the network
        // thread, and item application (SaveMgr/IL2CPP calls) isn't safe off the main
        // thread. Mod.OnUpdate() polls ItemReceiver.RetryPending() on the main thread
        // instead, so CatchUp here just establishes the state - it'll pick up from here.
        ItemReceiver.CatchUp(session.Items, config.Slot, session.RoomState.Seed, log);

        // DeathLinkService.OnDeathLinkReceived has the exact same network-thread hazard as
        // session.Items.ItemReceived (both are driven by the same socket.PacketReceived
        // event under the hood) - DeathLinkHandler.OnReceived only queues, it doesn't touch
        // GameMgr directly. See DeathLinkHandler.cs for the main-thread drain.
        DeathLinkService = session.CreateDeathLinkService();
        DeathLinkEnabled = IsDeathLinkEnabled(success.SlotData);
        if (DeathLinkEnabled)
        {
            DeathLinkService.EnableDeathLink();
            DeathLinkService.OnDeathLinkReceived += DeathLinkHandler.OnReceived;
            log.Msg("DeathLink enabled for this slot.");
        }

        return null;
    }

    private static bool IsDeathLinkEnabled(Dictionary<string, object> slotData)
    {
        if (slotData == null || !slotData.TryGetValue("death_link", out var value))
            return false;

        try
        {
            return Convert.ToBoolean(value);
        }
        catch
        {
            return false;
        }
    }

    public static void Disconnect()
    {
        if (Session == null)
            return;

        if (DeathLinkEnabled)
            DeathLinkService.OnDeathLinkReceived -= DeathLinkHandler.OnReceived;

        DeathLinkService = null;
        DeathLinkEnabled = false;
        SlotName = null;
        SlotData = null;

        Session.Socket.DisconnectAsync();
        Session = null;
    }
}
