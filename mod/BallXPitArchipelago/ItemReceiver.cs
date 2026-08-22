using Archipelago.MultiClient.Net.Helpers;
using Il2Cpp;
using MelonLoader;

namespace BallXPitArchipelago;

/// <summary>
/// Applies items received from the Archipelago server to the current save via SaveMgr.
///
/// Item name convention (must match the ballxpit apworld's item table):
///   "Character: {display}"        -> SaveMgr.I.UnlockChar(CharType.k{Token})
///   "Blueprint: {display}"        -> SaveMgr.I.GainBlueprint(BuildingType.k{Token})
///   "Level Access: {display}"     -> tracked for the level-access gate (Phase 3 hook; no-op for now)
///   "Progressive Land Expansion"  -> tracked as a count (Phase 3 hook; no-op for now)
///   "Wood" / "Stone" / "Wheat" / "Gold" -> SaveMgr.I.AddResources(...) filler grant
///
/// This is Phase 1 scaffolding: it proves the receive-and-apply loop end-to-end for the
/// systems SaveMgr already exposes a direct public grant method for (characters,
/// blueprints, resources). Level access and land expansion don't have a direct "just
/// grant it" API in vanilla - those need the Harmony gating hooks from Phase 3.
///
/// The Archipelago server replays a slot's *entire* item history every time we connect
/// (IReceivedItemsHelper.AllItemsReceived grows to hold the full history, not just new
/// items), so we track how many we've already applied in ApState and only apply the tail.
/// </summary>
public static class ItemReceiver
{
    private const int FillerResourceAmount = 20;

    private static MelonLogger.Instance _log;
    private static ApState _state;

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
        for (var i = _state.AppliedItemCount; i < all.Count; i++)
        {
            Apply(all[i].ItemName);
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

    private static void Apply(string itemName)
    {
        try
        {
            if (itemName.StartsWith("Character: "))
            {
                var token = "k" + itemName["Character: ".Length..].Replace(" ", "");
                if (Enum.TryParse<CharType>(token, out var charType))
                {
                    SaveMgr.I.UnlockChar(charType);
                    _log.Msg($"Unlocked character {charType}");
                }
                else
                {
                    _log.Warning($"Unknown character item: {itemName}");
                }
            }
            else if (itemName.StartsWith("Blueprint: "))
            {
                var token = "k" + itemName["Blueprint: ".Length..].Replace(" ", "");
                if (Enum.TryParse<BuildingType>(token, out var buildingType))
                {
                    SaveMgr.I.GainBlueprint(buildingType);
                    _log.Msg($"Gained blueprint {buildingType}");
                }
                else
                {
                    _log.Warning($"Unknown blueprint item: {itemName}");
                }
            }
            else if (itemName.StartsWith("Level Access: "))
            {
                // TODO(Phase 3): wire into the level-select gate once LocationHooks exists.
                _log.Msg($"Received {itemName} (level-access gating not implemented yet)");
            }
            else if (itemName == "Progressive Land Expansion")
            {
                // TODO(Phase 3): wire into the base-chunk purchase gate.
                _log.Msg($"Received {itemName} (land-expansion gating not implemented yet)");
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
                SaveMgr.I.AddResources(resourceType, FillerResourceAmount, false, false);
                _log.Msg($"Granted {FillerResourceAmount} {resourceType}");
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
}
