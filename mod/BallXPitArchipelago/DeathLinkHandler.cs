using System.Collections.Concurrent;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using HarmonyLib;
using Il2Cpp;

namespace BallXPitArchipelago;

/// <summary>
/// DeathLink: dying for real sends one out (LoseGameDeathLinkPatch below), receiving one
/// ends the player's current run the same way a real death would.
///
/// GameMgr.LoseGame() is confirmed (via temporary debug logging, see project memory) to
/// only fire on a genuinely final, unrevivable loss - the Necromancer building's mid-run
/// revive offer resolves (accept or decline) *before* LoseGame() is ever called, so there's
/// no risk of sending a DeathLink for a death the player was about to undo.
///
/// DeathLinkService.OnDeathLinkReceived fires on Archipelago's network thread (same
/// socket.PacketReceived plumbing as session.Items.ItemReceived - see ItemReceiver's header
/// comment for why that's unsafe for IL2CPP/Unity calls), so OnReceived only enqueues here;
/// Mod.OnUpdate() drains the queue on the main thread via ProcessPending(). A DeathLink that
/// arrives while not actively in a run (GameMgr.CurState != kPlaying) stays queued rather
/// than being dropped - it takes effect at the start of the next run instead of being lost.
/// IsApplyingDeathLink guards LoseGameDeathLinkPatch from re-sending a DeathLink for a death
/// that was itself caused by receiving one, which would otherwise bounce forever.
/// </summary>
internal static class DeathLinkHandler
{
    private static readonly ConcurrentQueue<DeathLink> Pending = new();

    internal static bool IsApplyingDeathLink { get; private set; }

    internal static void OnReceived(DeathLink deathLink)
    {
        Pending.Enqueue(deathLink);
    }

    /// <summary>Call periodically from Mod.OnUpdate().</summary>
    internal static void ProcessPending()
    {
        if (Pending.IsEmpty || GameMgr.I == null || GameMgr.I.CurState != GameState.kPlaying)
            return;

        // Draining fully rather than one-at-a-time: dying once already covers any other
        // DeathLinks that piled up in the queue alongside it.
        DeathLink applied = null;
        while (Pending.TryDequeue(out var deathLink))
            applied = deathLink;

        if (applied == null)
            return;

        LocationHooks.Log?.Msg(
            $"DeathLink received from {applied.Source}" +
            (string.IsNullOrEmpty(applied.Cause) ? "" : $": {applied.Cause}"));

        IsApplyingDeathLink = true;
        try
        {
            GameMgr.I.LoseGame();
        }
        finally
        {
            IsApplyingDeathLink = false;
        }
    }
}

[HarmonyPatch(typeof(GameMgr), nameof(GameMgr.LoseGame))]
internal static class LoseGameDeathLinkPatch
{
    private static void Postfix()
    {
        if (ApConnection.Session == null || !ApConnection.DeathLinkEnabled || DeathLinkHandler.IsApplyingDeathLink)
            return;

        ApConnection.DeathLinkService.SendDeathLink(new DeathLink(ApConnection.SlotName, $"{ApConnection.SlotName} died in Ball x Pit"));
        LocationHooks.Log?.Msg("Sent DeathLink.");
    }
}
