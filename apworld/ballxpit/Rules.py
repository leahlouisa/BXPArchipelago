from BaseClasses import ItemClassification, LocationProgressType

from .BlueprintPools import BLUEPRINT_POOLS_BY_LEVEL, TROPHY_BUILDING_BY_LEVEL
from .Items import PROGRESSIVE_LEVEL_ACCESS_COUNT, PROGRESSIVE_LEVEL_ACCESS_ITEM_NAME, building_enum_to_display
from .Locations import complete_level_location_names, land_expansion_location_names

# Declared order in game_data.json's "levels" array (== LevelType enum declaration order,
# also the order complete_level_location_names comes in) - NOT the real intended difficulty
# progression. Only used below to build an enum -> location lookup from that already-ordered
# list.
_LEVEL_ENUMS_IN_DECLARED_ORDER = [
    "kGraveyard", "kSnowy", "kSavanna", "kHell", "kClouds", "kMoon", "kShroom", "kDesert",
]

# The real intended difficulty/progression order - confirmed directly with the user, who
# has played this game extensively (the enum declaration order above is substantially
# different and not safe to assume: Desert is 3rd here, not last; Vast Void is last, not
# 6th). Levels are gated so that reaching level N requires already holding every earlier
# level's "Level Access" item too, not just its own - otherwise a player could receive a
# late-game level's access item before any earlier one (pure luck of the draw under
# per-item delivery order) and get dropped into content far above where they're actually
# equipped to survive.
LEVEL_UNLOCK_ORDER = [
    "kGraveyard", "kSnowy", "kDesert", "kShroom", "kSavanna", "kHell", "kClouds", "kMoon",
]


def set_rules(world) -> None:
    multiworld = world.multiworld
    player = world.player

    _set_level_order_rules(world)

    # "Land Expansion #n" and "Elevator Upgrade #n" locations have no access rule: purchases
    # are unrestricted vanilla, not gated on any received item - see
    # ConfirmExpansionLocationPatch in the mod. Whether a player can actually reach #n
    # in-game depends on the vanilla resource economy, which isn't modeled in logic. These
    # two aren't tied to any specific level either (elevator progress and land purchases are
    # earned/spent across the whole game, not gated behind one particular biome), so unlike
    # blueprint and trophy locations below, they genuinely don't need level-access gating.

    _set_land_expansion_rules(world)
    _set_blueprint_pool_rules(world)
    _set_trophy_rules(world)

    # Goal: be able to attempt (and therefore, in practice, complete) all 8 biomes. Actual
    # in-game completion is signaled by the mod calling session.SetGoalAchieved() once all
    # 8 levels are truly beaten - this is just the logic-side condition the generator uses
    # to guarantee the seed is solvable. Holding every copy of the progressive item implies
    # every level is individually reachable too (position 7's requirement subsumes all
    # smaller positions), so this alone is equivalent to the old state.has_all(...) check.
    multiworld.completion_condition[player] = lambda state: state.has(
        PROGRESSIVE_LEVEL_ACCESS_ITEM_NAME, player, PROGRESSIVE_LEVEL_ACCESS_COUNT
    )


def _level_access_positions() -> dict:
    """
    level_enum -> how many copies of PROGRESSIVE_LEVEL_ACCESS_ITEM_NAME are needed to reach
    and play that level (0 for the starting level, which needs none), per LEVEL_UNLOCK_ORDER.
    Shared by every rule-setting function below that gates something tied to a specific
    level, so they can never disagree with each other about what "reachable" means for a
    given level.

    Not one named item per level (that was the original design, replaced after a real report
    from live play): with 8 distinct "Level Access: X" items, a late-order level's item could
    (and did) arrive from the multiworld before an earlier one - completely normal for a
    randomizer, items have no reason to arrive in any particular order - and would just sit
    in inventory doing nothing until every earlier level's item had also arrived, since
    reaching it required holding all of them together. That's a real AP grant with zero
    visible in-game effect, confusing for a player with no visibility into the internal
    ordering logic. A single progressive item sidesteps this entirely: every copy received
    always unlocks whichever level is next in the player's own real order, so a grant is
    never wasted - it just does less some of the time (nothing left to unlock only once
    you're already caught up to your own progress) rather than doing nothing every time
    until a specific combination of items has arrived.
    """
    return {level_enum: position for position, level_enum in enumerate(LEVEL_UNLOCK_ORDER)}


def _set_land_expansion_rules(world) -> None:
    """
    Land Expansion purchase cost is a fixed, steadily escalating gold curve (confirmed with
    the user: #1 is 200g, +100g each expansion after, #24 is 2500g - not counting whatever
    was already spent buying the ones before it) - not something this world models in
    logic (see set_rules' comment on why access_rule is left alone for these). Left
    completely unrestricted, the generator could place a real progression item (a Level
    Access item, or a blueprint chain item gating further progress) behind "Land Expansion
    #24" - the seed would still be technically solvable, since the location does eventually
    become reachable given enough playtime, but a player could be stalled for a very long
    time grinding gold for a purchase whose only purpose was holding something they
    actually needed to keep moving.

    Restricting all 24 via item_rule leaves the fill algorithm too little room to work with
    (confirmed live: real generation failed outright, 12 progression items short of a valid
    placement - Land Expansion's total lack of access_rule makes it one of the only
    "reachable from the very start" pools of locations available to place early-chain
    progression items into, so removing all 24 from eligibility starves the fill). Confirmed
    live that the deficit scales exactly 1:1 with how many are restricted, so #1-15
    (200g-1700g) stay unrestricted - 3 positions of margin above the exact 12-open
    break-even point, not just the bare minimum, since shipping at an exact break-even is
    risky (a future item pool change of even one item could tip it back into failure).
    #16-24 (1800g-2500g) are restricted to non-progression items.

    Within the 15 unrestricted, #1-5 (200g-600g, the cheapest) are also nudged toward
    holding progression items via LocationProgressType.PRIORITY - a soft bias, not a hard
    requirement (deliberately NOT an access_rule chain like the blueprint/level-order
    chains: that would make #2-15 each conditionally reachable instead of immediately
    available, shrinking the "reachable from the start" pool the fill algorithm actually
    needed more of to resolve the deficit above in the first place - counterproductive
    here). Confirmed live that `multiworld.priority_locations` doesn't exist on this AP
    core version's MultiWorld (AttributeError) - progress_type on the Location itself is
    the correct mechanism instead.
    """
    multiworld = world.multiworld
    player = world.player

    for loc_name in land_expansion_location_names[:5]:
        multiworld.get_location(loc_name, player).progress_type = LocationProgressType.PRIORITY

    for loc_name in land_expansion_location_names[15:]:
        location = multiworld.get_location(loc_name, player)
        location.item_rule = lambda item: not (item.classification & ItemClassification.progression)


def _set_level_order_rules(world) -> None:
    """
    Gates "Complete Level: X" locations on holding every earlier level's "Level Access"
    item too (per LEVEL_UNLOCK_ORDER), not just level X's own - LocationHooks.cs's
    LevelSelectItemInitLockedPatch enforces the identical requirement at the actual
    level-select screen (parsed from fill_slot_data()'s level_unlock_order, not
    hand-duplicated on the mod side, so the two can never disagree about the order). This
    function has to encode the *same* full requirement, not just "needs the immediately
    prior level's item": access rules are evaluated purely from what's currently held, with
    no memory of how that state was reached, so state.has("Level Access: Shroom") alone
    doesn't imply the player also holds earlier levels' items - AP is free to deliver
    Shroom's access item from a completely unrelated check with no relation to whether
    earlier levels' items have arrived yet.

    The first level in LEVEL_UNLOCK_ORDER is the vanilla starting level - already unlocked
    the moment a save is created, no item needed to reach it (confirmed live) - so it's
    excluded from the chain entirely: its own location gets no access rule, and later
    levels don't require its "Level Access" item either. Requiring it would be meaningless
    (nothing gates it) and could actively hurt: if that item happened to land somewhere
    inconvenient, it would artificially delay every later level behind an item whose
    corresponding level was reachable from the very start.
    """
    multiworld = world.multiworld
    player = world.player

    location_by_level = dict(zip(_LEVEL_ENUMS_IN_DECLARED_ORDER, complete_level_location_names))

    for level_enum, position in _level_access_positions().items():
        location = multiworld.get_location(location_by_level[level_enum], player)
        if position:
            location.access_rule = lambda state, n=position: state.has(
                PROGRESSIVE_LEVEL_ACCESS_ITEM_NAME, player, n
            )
        else:
            location.access_rule = lambda state: True

    # Read back by fill_slot_data() - the mod enforces this exact order at the level-select
    # screen rather than hand-duplicating it, so generation-time logic and runtime behavior
    # can never diverge.
    world.level_unlock_order = LEVEL_UNLOCK_ORDER


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

    Each position ALSO requires actually being able to reach the level it's assigned to
    this seed (per _level_access_positions) - a position being first-in-its-chain only
    means "no other blueprint needs to be found first", not "reachable from the start of
    the game". Missing this was a real bug (reported live): with level order enforced but
    blueprint locations left ungated, the generator could place a level's own "Level
    Access" item behind a blueprint location in a level that isn't reachable without that
    exact item - or one further down LEVEL_UNLOCK_ORDER, needing even more - an
    unreachable, circular requirement the generator had no way to know to avoid.
    """
    multiworld = world.multiworld
    player = world.player

    level_access_positions = _level_access_positions()

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

        level_position = level_access_positions.get(level_enum, 0)

        previous_item = None
        for building_enum in level_buildings:
            loc_name = f"Blueprint: {building_enum_to_display[building_enum]}"
            location = multiworld.get_location(loc_name, player)
            pos = level_position
            prev = previous_item
            if pos and prev is not None:
                location.access_rule = lambda state, pos=pos, prev=prev: (
                    state.has(PROGRESSIVE_LEVEL_ACCESS_ITEM_NAME, player, pos) and state.has(prev, player)
                )
            elif pos:
                location.access_rule = lambda state, pos=pos: state.has(
                    PROGRESSIVE_LEVEL_ACCESS_ITEM_NAME, player, pos
                )
            elif prev is not None:
                location.access_rule = lambda state, prev=prev: state.has(prev, player)
            else:
                location.access_rule = lambda state: True
            previous_item = loc_name

    # Read back by fill_slot_data() - the mod applies this exact order rather than
    # recomputing its own, so generation-time logic and runtime behavior can never diverge.
    world.blueprint_order = blueprint_order


def _set_trophy_rules(world) -> None:
    """
    The 7 remaining Trophy locations (Void Trophy is permanently excluded - see
    BlueprintPools.py) are one-time level-completion rewards, not part of any sequential
    chain, but they're still tied to a specific level: TROPHY_BUILDING_BY_LEVEL says which.
    Same bug as blueprint locations if left ungated (reported live, generalized here): a
    trophy for a level you can't reach yet is just as real a place for the generator to
    strand a required "Level Access" item as an ungated blueprint position would be.
    """
    multiworld = world.multiworld
    player = world.player

    level_access_positions = _level_access_positions()

    for level_enum, building_enum in TROPHY_BUILDING_BY_LEVEL.items():
        loc_name = f"Blueprint: {building_enum_to_display[building_enum]}"
        location = multiworld.get_location(loc_name, player)
        position = level_access_positions.get(level_enum, 0)
        if position:
            location.access_rule = lambda state, n=position: state.has(
                PROGRESSIVE_LEVEL_ACCESS_ITEM_NAME, player, n
            )
        else:
            location.access_rule = lambda state: True
