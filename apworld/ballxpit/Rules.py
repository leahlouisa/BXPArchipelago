from .Items import level_access_item_names
from .Locations import complete_level_location_names, land_expansion_location_names


def set_rules(world) -> None:
    multiworld = world.multiworld
    player = world.player

    # Beating a biome requires having been granted access to it - see LocationHooks.cs on
    # the mod side, which overrides the level-select screen's locked/unlocked decision to
    # read from received "Level Access" items instead of the vanilla elevator/gear economy.
    for level_loc_name, access_item in zip(complete_level_location_names, level_access_item_names):
        location = multiworld.get_location(level_loc_name, player)
        location.access_rule = lambda state, item=access_item: state.has(item, player)

    # Land Expansion locations unlock progressively: the mod only lets you purchase the
    # Nth base chunk once you've received N copies of "Progressive Land Expansion".
    for count, loc_name in enumerate(land_expansion_location_names, start=1):
        location = multiworld.get_location(loc_name, player)
        location.access_rule = lambda state, n=count: state.has("Progressive Land Expansion", player, n)

    # Goal: be able to attempt (and therefore, in practice, complete) all 8 biomes. Actual
    # in-game completion is signaled by the mod calling session.SetGoalAchieved() once all
    # 8 levels are truly beaten - this is just the logic-side condition the generator uses
    # to guarantee the seed is solvable.
    multiworld.completion_condition[player] = lambda state: state.has_all(level_access_item_names, player)
