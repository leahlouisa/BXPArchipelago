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
///   "Progressive Land Expansion"  -> counted into LandExpansionCount, read by LocationHooks'
///                                     BaseGridMgr.ConfirmExpansion gate
///   "Wood" / "Stone" / "Wheat" / "Gold" -> SaveMgr.I.AddResources(...) filler grant
///
/// Characters and Blueprints are also grantable by vanilla game logic - LocationHooks
/// suppresses those vanilla grants (converting the vanilla trigger into a location check
/// instead) so IsApplyingItem is used to tell LocationHooks "this SaveMgr call came from
/// an AP item being applied, let it through" as opposed to a vanilla trigger to suppress.
///
/// The Archipelago server replays a slot's *entire* item history every time we connect
/// (IReceivedItemsHelper.AllItemsReceived grows to hold the full history, not just new
/// items). Character/Blueprint/resource grants write into the game's own save file, so
/// it's safe (and necessary, to avoid double-granting) to apply each one only once, tracked
/// via ApState's cursor. Level Access and Land Expansion have no representation anywhere
/// in the game's save file though - UnlockedLevels/LandExpansionCount live only in this
/// mod's memory - so applying *those* once via the same cursor would silently lose them on
/// every reconnect or process restart (the cursor says "already applied", so they'd never
/// be re-added to the in-memory state). Recomputed from the full history every time instead.
/// </summary>
public static class ItemReceiver
{
    private const int FillerResourceAmount = 20;

    private static MelonLogger.Instance _log;
    private static ApState _state;

    internal static bool IsApplyingItem { get; private set; }
    internal static readonly HashSet<LevelType> UnlockedLevels = new();
    internal static int LandExpansionCount { get; private set; }

    public static void CatchUp(IReceivedItemsHelper items, string slot, MelonLogger.Instance log)
    {
        _log = log;
        _state = ApState.Load(slot);
        Drain(items);
    }

    public static void OnItemReceived(ReceivedItemsHelper helper)
    {
        Drain(helper);
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
            ApplyPersistent(all[i].ItemName);
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
        var landExpansionCount = 0;

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
            else if (itemName == "Progressive Land Expansion")
            {
                landExpansionCount++;
            }
        }

        LandExpansionCount = landExpansionCount;
    }

    /// <summary>Character/Blueprint/resource grants - applied once and tracked by ApState's cursor.</summary>
    private static void ApplyPersistent(string itemName)
    {
        try
        {
            if (itemName.StartsWith("Character: "))
            {
                var display = itemName["Character: ".Length..];
                if (GameNames.TryParseCharacter(display, out var charType))
                    ApplyGuarded(() => SaveMgr.I.UnlockChar(charType), $"Unlocked character {charType}");
                else
                    _log.Warning($"Unknown character item: {itemName}");
            }
            else if (itemName.StartsWith("Blueprint: "))
            {
                var display = itemName["Blueprint: ".Length..];
                if (GameNames.TryParseBuilding(display, out var buildingType))
                    ApplyGuarded(() => SaveMgr.I.GainBlueprint(buildingType), $"Gained blueprint {buildingType}");
                else
                    _log.Warning($"Unknown blueprint item: {itemName}");
            }
            else if (itemName.StartsWith("Level Access: ") || itemName == "Progressive Land Expansion")
            {
                // Handled by RecomputeInMemoryState every Drain() call, not here.
            }
            else if (itemName is "Wood" or "Stone" or "Wheat" or "Gold")
            {
                var resourceType = itemName switch
                {
                    "Wood" => ResourceType.kWood,
                    "Stone" => ResourceType.kStone,
                    "Wheat" => ResourceType.kWheat,
                    "Gold" => ResourceType.kGold,
                    _ => throw new InvalidOperationException(),
                };
                ApplyGuarded(
                    () => SaveMgr.I.AddResources(resourceType, FillerResourceAmount, false, false),
                    $"Granted {FillerResourceAmount} {resourceType}");
            }
            else
            {
                _log.Warning($"Unrecognized item: {itemName}");
            }
        }
        catch (Exception e)
        {
            _log.Error($"Failed to apply item '{itemName}': {e}");
        }
    }

    private static void ApplyGuarded(Action grant, string logMessage)
    {
        IsApplyingItem = true;
        try
        {
            grant();
        }
        finally
        {
            IsApplyingItem = false;
        }

        _log.Msg(logMessage);
    }
}
