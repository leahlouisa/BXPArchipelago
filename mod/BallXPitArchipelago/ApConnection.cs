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

    public static void Connect(ApConfig config, MelonLogger.Instance log)
    {
        ArchipelagoSession session;
        try
        {
            session = ArchipelagoSessionFactory.CreateSession(config.Host, config.Port);
        }
        catch (Exception e)
        {
            log.Error($"Could not create a session for {config.Host}:{config.Port}: {e.Message}");
            return;
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
            log.Error($"Connection to {config.Host}:{config.Port} failed: {e.Message}");
            return;
        }

        if (!result.Successful)
        {
            var failure = (LoginFailure)result;
            log.Error($"Failed to connect to {config.Host}:{config.Port} as {config.Slot}:");
            foreach (var error in failure.Errors)
                log.Error($"    {error}");
            foreach (var errorCode in failure.ErrorCodes)
                log.Error($"    {errorCode}");
            return;
        }

        Session = session;
        var success = (LoginSuccessful)result;
        log.Msg($"Connected to {config.Host}:{config.Port} as {config.Slot} " +
                $"(team {success.Team}, slot {success.Slot}).");

        session.Items.ItemReceived += ItemReceiver.OnItemReceived;
        ItemReceiver.CatchUp(session.Items, config.Slot, log);
    }

    public static void Disconnect()
    {
        if (Session == null)
            return;

        Session.Items.ItemReceived -= ItemReceiver.OnItemReceived;
        Session.Socket.DisconnectAsync();
        Session = null;
    }
}
