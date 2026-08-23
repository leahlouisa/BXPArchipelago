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
///   "Level Access: {display}"     -> added to UnlockedLevels, read by LocationHooks'
///                                     LevelSelectItem.InitLocked gate
///   "Land Expansion (No Effect)"  -> purely cosmetic, no gameplay effect (land expansion
///                                     purchases are unrestricted vanilla - see
///                                     ConfirmExpansionLocationPatch in LocationHooks.cs) -
///                                     the name says so explicitly so it doesn't read as a
///                                     broken check when nothing visibly happens
///   "Wood" / "Stone" / "Wheat" / "Gold" -> SaveMgr.I.AddResources(...) filler grant
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
/// via ApState's cursor. Level Access has no representation anywhere in the game's save file
/// though - UnlockedLevels lives only in this mod's memory - so applying it once via the same
/// cursor would silently lose it on every reconnect or process restart (the cursor says
/// "already applied", so it'd never be re-added to the in-memory state). Recomputed from the
/// full history every time instead.
///
/// Deliberately not wired to session.Items.ItemReceived: that event fires on Archipelago's
/// network thread, and SaveMgr/IL2CPP calls aren't safe off Unity's main thread. Instead
/// Mod.OnUpdate() polls RetryPending() a couple times a second, so every actual grant
/// happens on the main thread. The cursor only advances past an item once it's confirmed
/// applied (see Drain) so a transient failure gets retried instead of silently dropped.
/// </summary>
public static class ItemReceiver
{
    private const int FillerResourceAmount = 20;

    private static MelonLogger.Instance _log;
    private static ApState _state;

    internal static bool IsApplyingItem { get; private set; }
    internal static readonly HashSet<LevelType> UnlockedLevels = new();

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

        for (var i = _state.AppliedItemCount; i < all.Count; i++)
        {
            // Stop at the first item that fails to apply rather than skipping past it -
            // it (and anything after it) gets retried from here on the next Drain() call.
            if (!ApplyPersistent(all[i].ItemName))
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

    private static void RecomputeInMemoryState(ReadOnlyCollection<ItemInfo> all)
    {
        UnlockedLevels.Clear();

        foreach (var item in all)
        {
            var itemName = item.ItemName;

            if (itemName.StartsWith("Level Access: "))
            {
                var display = itemName["Level Access: ".Length..];
                if (GameNames.TryParseLevel(display, out var levelType))
                    UnlockedLevels.Add(levelType);
                else
                    _log.Warning($"Unknown level access item: {itemName}");
            }
        }
    }

    /// <summary>
    /// Character/Blueprint/resource grants - applied once and tracked by ApState's cursor.
    /// Returns false only for a transient failure worth retrying (the SaveMgr call itself
    /// threw); an unresolvable name is logged and treated as "handled" (true) since retrying
    /// it would never succeed.
    /// </summary>
    private static bool ApplyPersistent(string itemName)
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

        if (itemName.StartsWith("Level Access: "))
        {
            // Handled by RecomputeInMemoryState every Drain() call, not here.
            return true;
        }

        if (itemName == "Land Expansion (No Effect)")
        {
            // Purely cosmetic - land expansion purchases are unrestricted vanilla, see
            // ConfirmExpansionLocationPatch. Nothing to apply.
            return true;
        }

        if (itemName is "Wood" or "Stone" or "Wheat" or "Gold")
        {
            var resourceType = itemName switch
            {
                "Wood" => ResourceType.kWood,
                "Stone" => ResourceType.kStone,
                "Wheat" => ResourceType.kWheat,
                "Gold" => ResourceType.kGold,
                _ => throw new InvalidOperationException(),
            };
            return TryApplyGuarded(
                () => SaveMgr.I.AddResources(resourceType, FillerResourceAmount, false, false),
                $"Granted {FillerResourceAmount} {resourceType}",
                itemName);
        }

        _log.Warning($"Unrecognized item: {itemName}");
        return true;
    }

    private static bool TryApplyGuarded(Action grant, string logMessage, string itemName)
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
        return true;
    }
}
