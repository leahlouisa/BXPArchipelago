using Archipelago.MultiClient.Net;
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
        var success = (LoginSuccessful)result;
        log.Msg($"Connected to {config.Host}:{config.Port} as {config.Slot} " +
                $"(team {success.Team}, slot {success.Slot}).");

        // No session.Items.ItemReceived subscription: that event fires on the network
        // thread, and item application (SaveMgr/IL2CPP calls) isn't safe off the main
        // thread. Mod.OnUpdate() polls ItemReceiver.RetryPending() on the main thread
        // instead, so CatchUp here just establishes the state - it'll pick up from here.
        ItemReceiver.CatchUp(session.Items, config.Slot, log);

        return null;
    }

    public static void Disconnect()
    {
        if (Session == null)
            return;

        Session.Socket.DisconnectAsync();
        Session = null;
    }
}
