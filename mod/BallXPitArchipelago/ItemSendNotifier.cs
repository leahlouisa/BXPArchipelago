using System.Collections.Concurrent;
using Archipelago.MultiClient.Net.MessageLog.Messages;

namespace BallXPitArchipelago;

/// <summary>
/// Notifies the player via toast when one of their own location checks delivers an item to
/// a DIFFERENT player in the multiworld - self-sends are already covered by ItemReceiver's
/// "Received: X" toast, so this only covers the outbound direction.
///
/// IMessageLogHelper.OnMessageReceived fires on Archipelago's network thread (same
/// socket.PacketReceived plumbing as session.Items.ItemReceived / DeathLinkService's
/// OnDeathLinkReceived - see ItemReceiver's header comment for why that's unsafe for
/// IL2CPP/Unity calls), so OnMessageReceived here only enqueues; Mod.OnUpdate() drains the
/// queue on the main thread via ProcessPending(), which is the only place it's safe to call
/// ApGui.ShowToast (a plain, non-thread-safe List) from.
/// </summary>
internal static class ItemSendNotifier
{
    private static readonly ConcurrentQueue<string> Pending = new();

    internal static void OnMessageReceived(LogMessage message)
    {
        if (message is ItemSendLogMessage itemSend && itemSend.IsSenderTheActivePlayer && !itemSend.IsReceiverTheActivePlayer)
            Pending.Enqueue($"Sent: {itemSend.Item.ItemDisplayName} to {itemSend.Receiver.Alias}");
    }

    /// <summary>Call periodically from Mod.OnUpdate().</summary>
    internal static void ProcessPending()
    {
        while (Pending.TryDequeue(out var text))
            ApGui.ShowToast(text);
    }
}
