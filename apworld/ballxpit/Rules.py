from .BlueprintPools import BLUEPRINT_POOLS_BY_LEVEL
from .Items import building_enum_to_display, level_access_item_names
from .Locations import complete_level_location_names


def set_rules(world) -> None:
    multiworld = world.multiworld
    player = world.player

    # Beating a biome requires having been granted access to it - see LocationHooks.cs on
    # the mod side, which overrides the level-select screen's locked/unlocked decision to
    # read from received "Level Access" items instead of the vanilla elevator/gear economy.
    for level_loc_name, access_item in zip(complete_level_location_names, level_access_item_names):
        location = multiworld.get_location(level_loc_name, player)
        location.access_rule = lambda state, item=access_item: state.has(item, player)

    # "Land Expansion #n" locations have no access rule (same as Elevator Upgrade
    # locations): purchases are unrestricted vanilla, not gated on any received item - see
    # ConfirmExpansionLocationPatch in the mod. Whether a player can actually reach #n
    # in-game depends on the vanilla resource economy, which isn't modeled in logic.

    _set_blueprint_pool_rules(world)

    # Goal: be able to attempt (and therefore, in practice, complete) all 8 biomes. Actual
    # in-game completion is signaled by the mod calling session.SetGoalAchieved() once all
    # 8 levels are truly beaten - this is just the logic-side condition the generator uses
    # to guarantee the seed is solvable.
    multiworld.completion_condition[player] = lambda state: state.has_all(level_access_item_names, player)


def _set_blueprint_pool_rules(world) -> None:
    """
    Every position in this chain is represented in-game by a dedicated placeholder
    building (Void Trophy, permanently suppressed - see BlueprintShuffle.cs) rather than
    by whatever real building this function assigns to that position. That's a deliberate
    fix for a real bug: vanilla's "next undiscovered blueprint" walk is driven by a
    building's *global* ownership flag, which can flip out-of-band the instant the
    matching AP item is received from anywhere in the multiworld - not just when its own
    level's boss is actually killed. If the list held real building identities, a
    lucky/unlucky race could silently and permanently skip a check the moment its item
    arrived early, dropping whatever another player's location happened to hold there.
    Void Trophy's ownership is never touched by anything except this mechanism, so it can
    never desync - the mod swaps a fresh placeholder in every time one gets consumed,
    decoupling "does vanilla still have something to offer this level" from "what item did
    the player happen to receive and when."

    That decoupling is also why positions no longer need to match each level's original
    vanilla count or which buildings vanilla originally put there: the pool below is the
    full flat set of every real, confirmed-live building across all 8 levels, and this
    function randomly re-partitions it per seed (every level guaranteed at least one
    position, the remainder handed out one at a time) - "5 in Boneyard, 16 in Snowy" is a
    legitimate possible seed. Exported via fill_slot_data() and applied by the mod at
    connect time, rather than the mod computing its own order or partition, specifically
    so the two can never fall out of sync.

    Confirmed live (see project memory) that vanilla's own "next undiscovered blueprint
    for this level" logic walks each level's pool sequentially by position, skipping
    entries whose blueprint has already been granted - suppressing Void Trophy's grant
    unconditionally means that flag never flips, so position N's location only becomes
    reachable once position N-1's item has been received (from anywhere in the
    multiworld). That's a genuine progressive dependency chain, not just flavor: getting
    this rule wrong would let the generator place a required item (most importantly a
    Level Access item) somewhere provably unreachable without it - an unwinnable seed.
    """
    multiworld = world.multiworld
    player = world.player

    levels = list(BLUEPRINT_POOLS_BY_LEVEL.keys())
    pool = [b for lvl in levels for b in BLUEPRINT_POOLS_BY_LEVEL[lvl]]

    shuffled = list(pool)
    world.random.shuffle(shuffled)

    # Every level starts with a guaranteed 1 position, then the rest of the pool is handed
    # out one building at a time to a randomly chosen level - counts end up varying a lot
    # seed to seed rather than mirroring each level's original vanilla size.
    counts = [1] * len(levels)
    for _ in range(len(shuffled) - len(levels)):
        counts[world.random.randrange(len(levels))] += 1

    blueprint_order = {}
    cursor = 0
    for level_enum, count in zip(levels, counts):
        level_buildings = shuffled[cursor:cursor + count]
        cursor += count
        blueprint_order[level_enum] = level_buildings

        previous_item = None
        for building_enum in level_buildings:
            loc_name = f"Blueprint: {building_enum_to_display[building_enum]}"
            location = multiworld.get_location(loc_name, player)
            if previous_item is None:
                location.access_rule = lambda state: True
            else:
                location.access_rule = lambda state, item=previous_item: state.has(item, player)
            previous_item = loc_name

    # Read back by fill_slot_data() - the mod applies this exact order rather than
    # recomputing its own, so generation-time logic and runtime behavior can never diverge.
    world.blueprint_order = blueprint_order
