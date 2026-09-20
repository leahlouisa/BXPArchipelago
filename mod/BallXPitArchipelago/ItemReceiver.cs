using System.Collections.ObjectModel;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using Il2Cpp;
using MelonLoader;

namespace BallXPitArchipelago;

/// <summary>
/// Applies items received from the Archipelago server to the current save.
///
/// Item name convention (must match the ballxpit apworld's item table):
///   "Character: {display}"        -> SaveMgr.I.UnlockChar(CharType.k{Token})
///   "Blueprint: {display}"        -> SaveMgr.I.GainBlueprint(BuildingType.k{Token})
///   "Progressive Level Access"    -> counted (not tracked per-level - see
///                                     ProgressiveLevelAccessCount), read by LocationHooks'
///                                     LevelSelectItem gates via LevelUnlockOrder.IsReachable
///   "Wood" / "Stone" / "Wheat" / "Gold" -> SaveMgr.I.AddResources(...) filler grant - also
///                                     what "Land Expansion #n" locations grant (land
///                                     expansion purchases are unrestricted vanilla - see
///                                     ConfirmExpansionLocationPatch in LocationHooks.cs -
///                                     so there's nothing real left to gate; a genuine
///                                     resource grant reads better than a dedicated "no
///                                     effect" item, reported live)
///
/// Characters and Blueprints are also grantable by vanilla game logic, through the same
/// SaveMgr methods this applies AP items with - IsApplyingItem tells LocationHooks "this
/// SaveMgr call came from an AP item being applied, don't treat it as a fresh vanilla
/// trigger". For Character this also suppresses the vanilla grant entirely (real unlock only
/// happens via a received AP item); Blueprint no longer suppresses (see
/// GainBlueprintLocationPatch), but still needs the guard so applying a received "Blueprint:
/// X" item doesn't loop back around into sending a check for itself.
///
/// The Archipelago server replays a slot's *entire* item history every time we connect
/// (IReceivedItemsHelper.AllItemsReceived grows to hold the full history, not just new
/// items). Character/Blueprint/resource grants write into the game's own save file, so
/// it's safe (and necessary, to avoid double-granting) to apply each one only once, tracked
/// via ApState's cursor. Progressive Level Access has no representation anywhere in the
/// game's save file though - ProgressiveLevelAccessCount lives only in this mod's memory -
/// so applying it once via the same cursor would silently lose it on every reconnect or
/// process restart (the cursor says "already applied", so it'd never be re-added to the
/// in-memory state). Recomputed from the full history every time instead.
///
/// Deliberately not wired to session.Items.ItemReceived: that event fires on Archipelago's
/// network thread, and SaveMgr/IL2CPP calls aren't safe off Unity's main thread. Instead
/// Mod.OnUpdate() polls RetryPending() a couple times a second, so every actual grant
/// happens on the main thread. The cursor only advances past an item once it's confirmed
/// applied (see Drain) so a transient failure gets retried instead of silently dropped.
/// </summary>
public static class ItemReceiver
{
    private const string ProgressiveLevelAccessItemName = "Progressive Level Access";

    private static MelonLogger.Instance _log;
    private static ApState _state;

    internal static bool IsApplyingItem { get; private set; }

    /// <summary>How many "Progressive Level Access" copies have been received so far - see LevelUnlockOrder.IsReachable.</summary>
    internal static int ProgressiveLevelAccessCount { get; private set; }

    public static void CatchUp(IReceivedItemsHelper items, string slot, string seedName, MelonLogger.Instance log)
    {
        _log = log;
        _state = ApState.Load(slot);

        // AppliedItemCount is an index into *this seed's* item history - a local sidecar
        // file keyed only by slot name has no way to know a new seed was generated (e.g.
        // after an apworld data change), so without this check it would keep treating a
        // stale index as "already applied", silently skipping every item in the new seed's
        // history from 0 up to that index. RoomState.Seed uniquely IDs each generation.
        if (_state.SeedName != seedName)
        {
            _log.Msg($"New seed detected (was '{_state.SeedName}', now '{seedName}') - resetting applied-item progress.");
            _state.SeedName = seedName;
            _state.AppliedItemCount = 0;
            // A new seed means a new save, and therefore a fresh entitlement to the jumpstart
            // grant - see ApState.JumpstartApplied.
            _state.JumpstartApplied = false;
            // Undelivered run-scoped rewards belong to the old seed. The cursor reset above will
            // replay the new seed's item history from 0 and re-queue whatever it contains, so
            // carrying these over would hand out the old seed's debt on top of the new seed's.
            _state.PendingLevelUps = 0;
            _state.PendingFusers = 0;
        }

        Drain(items);
    }

    private static void Drain(IReceivedItemsHelper items)
    {
        if (_log == null || _state == null)
            return;

        // SaveMgr is a scene singleton - it doesn't exist yet at the point we connect
        // (OnInitializeMelon runs before the game's own scenes load). Leave our cursor
        // where it is and let Mod.OnUpdate() retry once SaveMgr is available.
        if (SaveMgr.I == null)
            return;

        var all = items.AllItemsReceived;

        RecomputeInMemoryState(all);

        // Before any items: the jumpstart grant is what makes the 12 precollected blueprints
        // actually placeable, so applying it first means a player never sees the buildings arrive
        // with nothing to build them with.
        ApplyJumpstartIfNeeded();

        // Running count of "Progressive Level Access" copies seen so far, as of (and
        // including) the item about to be applied - lets ApplyPersistent report which
        // level a given copy unlocked, without re-scanning the whole history per item.
        // Recomputed from 0 every Drain() call rather than persisted, so it stays correct
        // even after a failed-and-retried item (which doesn't advance the cursor).
        var progressiveSeen = 0;
        for (var i = 0; i < _state.AppliedItemCount; i++)
            if (all[i].ItemName == ProgressiveLevelAccessItemName)
                progressiveSeen++;

        for (var i = _state.AppliedItemCount; i < all.Count; i++)
        {
            if (all[i].ItemName == ProgressiveLevelAccessItemName)
                progressiveSeen++;

            // Stop at the first item that fails to apply rather than skipping past it -
            // it (and anything after it) gets retried from here on the next Drain() call.
            if (!ApplyPersistent(all[i].ItemName, progressiveSeen))
                break;

            _state.AppliedItemCount = i + 1;
        }

        _state.Save();
    }

    /// <summary>Called from Mod.OnUpdate() to retry applying any backlog once SaveMgr exists.</summary>
    public static void RetryPending(IReceivedItemsHelper items)
    {
        if (items != null)
            Drain(items);
    }

    internal static int PendingLevelUps => _state?.PendingLevelUps ?? 0;

    internal static int PendingFusers => _state?.PendingFusers ?? 0;

    /// <summary>
    /// Takes one pending run-scoped reward of the given kind, persisting the decrement immediately.
    /// Saving on every consume (rather than batching) is deliberate: these are delivered one per
    /// tick inside a live run, and a crash or force-quit mid-run shouldn't silently restore a
    /// reward the player already got the benefit of. Returns false if none are pending, which is
    /// the common case and costs nothing.
    /// </summary>
    internal static bool TryConsumePending(bool levelUp)
    {
        if (_state == null)
            return false;

        if (levelUp)
        {
            if (_state.PendingLevelUps <= 0)
                return false;
            _state.PendingLevelUps--;
        }
        else
        {
            if (_state.PendingFusers <= 0)
                return false;
            _state.PendingFusers--;
        }

        _state.Save();
        return true;
    }

    /// <summary>
    /// evosanity_jumpstart's one-time resource grant. The 12 buildings it comes with are ordinary
    /// precollected AP items and need nothing here - AP delivers them through the normal
    /// received-items stream, so ApplyPersistent's "Blueprint: X" branch handles them like any
    /// other blueprint. Resources aren't items though (they gate nothing in logic, and keeping
    /// them out of the pool keeps the apworld's item/location balance simple), so they're granted
    /// directly here and tracked by ApState.JumpstartApplied so a relaunch or reconnect doesn't
    /// hand out another pile.
    ///
    /// Uses SaveMgr.AddResources - the same call the Wood/Stone/Wheat/Gold filler items already
    /// use, and one of the methods proven safe to CALL (the hard rule from the placement-freeze
    /// saga is never to PATCH it - see EconomyHooks.cs).
    /// </summary>
    private static void ApplyJumpstartIfNeeded()
    {
        if (_state.JumpstartApplied || EconomyOptions.JumpstartResources == null)
            return;

        // Same guard as the rest of Drain: SaveMgr is a scene singleton that doesn't exist yet
        // when we first connect. Leave the flag unset and let the next tick retry.
        if (SaveMgr.I == null)
            return;

        var granted = new List<string>();
        foreach (var entry in EconomyOptions.JumpstartResources)
        {
            if (entry.Value <= 0)
                continue;

            var resourceType = entry.Key switch
            {
                "Wood" => ResourceType.kWood,
                "Stone" => ResourceType.kStone,
                "Wheat" => ResourceType.kWheat,
                "Gold" => ResourceType.kGold,
                _ => (ResourceType?)null,
            };

            if (resourceType == null)
            {
                _log.Warning($"[Jumpstart] Unknown resource '{entry.Key}' in slot data - skipping it.");
                continue;
            }

            IsApplyingItem = true;
            try
            {
                SaveMgr.I.AddResources(resourceType.Value, entry.Value, false, false);
            }
            catch (Exception e)
            {
                // Don't set JumpstartApplied - retry on the next tick rather than silently
                // shorting the player the rest of the grant.
                _log.Error($"[Jumpstart] Failed to grant {entry.Value} {entry.Key}, will retry: {e}");
                return;
            }
            finally
            {
                IsApplyingItem = false;
            }

            granted.Add($"{entry.Value} {entry.Key}");
        }

        _state.JumpstartApplied = true;
        _state.Save();

        if (granted.Count > 0)
        {
            _log.Msg($"[Jumpstart] Granted starting resources: {string.Join(", ", granted)}.");
            ApGui.ShowToast("Evosanity jumpstart: " + string.Join(", ", granted));
        }
    }

    private static void RecomputeInMemoryState(ReadOnlyCollection<ItemInfo> all)
    {
        var count = 0;
        foreach (var item in all)
        {
            if (item.ItemName == ProgressiveLevelAccessItemName)
                count++;
        }

        ProgressiveLevelAccessCount = count;
    }

    /// <summary>
    /// Character/Blueprint/resource grants - applied once and tracked by ApState's cursor.
    /// Returns false only for a transient failure worth retrying (the SaveMgr call itself
    /// threw); an unresolvable name is logged and treated as "handled" (true) since retrying
    /// it would never succeed. progressiveCount is only meaningful for a "Progressive Level
    /// Access" item - how many copies (including this one) have been seen so far, used to
    /// report which level this specific copy unlocked.
    /// </summary>
    private static bool ApplyPersistent(string itemName, int progressiveCount)
    {
        if (itemName.StartsWith("Character: "))
        {
            var display = itemName["Character: ".Length..];
            if (!GameNames.TryParseCharacter(display, out var charType))
            {
                _log.Warning($"Unknown character item: {itemName}");
                return true;
            }

            return TryApplyGuarded(() => SaveMgr.I.UnlockChar(charType), $"Unlocked character {charType}", itemName);
        }

        if (itemName.StartsWith("Blueprint: "))
        {
            var display = itemName["Blueprint: ".Length..];
            if (!GameNames.TryParseBuilding(display, out var buildingType))
            {
                _log.Warning($"Unknown blueprint item: {itemName}");
                return true;
            }

            return TryApplyGuarded(() => SaveMgr.I.GainBlueprint(buildingType), $"Gained blueprint {buildingType}", itemName);
        }

        if (itemName == ProgressiveLevelAccessItemName)
        {
            // The count itself is handled by RecomputeInMemoryState every Drain() call, not
            // here - this just reports which level this specific copy unlocked, if we know
            // the real order yet (LevelUnlockOrder.ApplyFromSlotData may not have run yet
            // this early after connecting).
            var unlockedLevel = LevelUnlockOrder.LevelForCount(progressiveCount);
            var toast = unlockedLevel.HasValue
                ? $"Received: Progressive Level Access ({GameNames.LevelDisplay(unlockedLevel.Value)} unlocked!)"
                : $"Received: {itemName}";
            ApGui.ShowToast(toast);
            return true;
        }

        // Run-scoped rewards: queued rather than applied here, and the cursor advances either way.
        //
        // Deliberately NOT returning false to get ItemReceiver's retry behaviour. Drain() stops at
        // the first item that fails to apply so nothing gets silently skipped, which is right for
        // a blueprint but catastrophic here - receiving one of these while sitting in your base
        // would stall every later item in the queue until you happened to start a run. Queueing
        // separates "this item has been accounted for" from "its effect has landed", which is
        // exactly the distinction a run-scoped reward needs.
        if (itemName is "Free Level Up" or "Fusion Reactor")
        {
            if (itemName == "Free Level Up")
                _state.PendingLevelUps++;
            else
                _state.PendingFusers++;

            // Saved by the caller once the cursor advances past this item; saving here too would
            // just double the writes.
            _log.Msg($"Queued run-scoped reward: {itemName} (pending: {_state.PendingLevelUps} level-ups, {_state.PendingFusers} fusers).");
            ApGui.ShowToast(RunScopedRewards.ToastFor(itemName));
            return true;
        }

        // Plain filler, plus the larger "bundle" denominations evosanity's extra locations are
        // padded with (Items.py's BUNDLE_FILLER_ITEM_IDS). A bundle is the same resource at
        // filler_bundle_multiplier times the amount - derived rather than given its own option per
        // resource, so there's only one knob to explain.
        var isBundle = itemName is "Wood Crate" or "Stone Crate" or "Wheat Crate" or "Gold Cache";
        if (isBundle || itemName is "Wood" or "Stone" or "Wheat" or "Gold")
        {
            var resourceType = itemName switch
            {
                "Wood" or "Wood Crate" => ResourceType.kWood,
                "Stone" or "Stone Crate" => ResourceType.kStone,
                "Wheat" or "Wheat Crate" => ResourceType.kWheat,
                "Gold" or "Gold Cache" => ResourceType.kGold,
                _ => throw new InvalidOperationException(),
            };
            // Yaml-configurable (Options.py filler_*_amount) - see EconomyOptions.cs.
            var baseAmount = resourceType switch
            {
                ResourceType.kWood => EconomyOptions.WoodFillerAmount,
                ResourceType.kStone => EconomyOptions.StoneFillerAmount,
                ResourceType.kWheat => EconomyOptions.WheatFillerAmount,
                ResourceType.kGold => EconomyOptions.GoldFillerAmount,
                _ => throw new InvalidOperationException(),
            };
            var amount = isBundle ? baseAmount * EconomyOptions.FillerBundleMultiplier : baseAmount;

            // The toast says "Received: 150 Wood", not "Received: 150 Wood Crate" - the amount
            // already conveys that it's the big one, and naming the resource keeps every filler
            // toast reading the same way.
            var toastResource = itemName switch
            {
                "Wood Crate" => "Wood",
                "Stone Crate" => "Stone",
                "Wheat Crate" => "Wheat",
                "Gold Cache" => "Gold",
                _ => itemName,
            };

            return TryApplyGuarded(
                () => SaveMgr.I.AddResources(resourceType, amount, false, false),
                $"Granted {amount} {resourceType}",
                itemName,
                toastText: $"Received: {amount} {toastResource}");
        }

        _log.Warning($"Unrecognized item: {itemName}");
        return true;
    }

    private static bool TryApplyGuarded(Action grant, string logMessage, string itemName, string toastText = null)
    {
        IsApplyingItem = true;
        try
        {
            grant();
        }
        catch (Exception e)
        {
            _log.Error($"Failed to apply item '{itemName}', will retry: {e}");
            return false;
        }
        finally
        {
            IsApplyingItem = false;
        }

        _log.Msg(logMessage);
        ApGui.ShowToast(toastText ?? $"Received: {itemName}");
        return true;
    }
}
