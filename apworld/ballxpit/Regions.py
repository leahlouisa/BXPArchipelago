from BaseClasses import Entrance, Region

from .Locations import (
    BallXPitLocation,
    active_ball_location_names,
    all_ball_location_names,
    location_table,
)


def _excluded_location_names(world) -> set:
    """
    Names in location_table that this particular seed should NOT actually create.

    location_table has to hold every location name the game could ever use, with a stable id, so
    that item_name_to_id/location_name_to_id (AP's per-game data package) stay static regardless
    of anyone's options. Whether a seed USES a given location is a separate question, and this is
    where it's answered - currently only evosanity varies, since every other category is always on.
    """
    return set(all_ball_location_names) - set(active_ball_location_names(world))


def create_regions(world) -> None:
    multiworld = world.multiworld
    player = world.player

    menu = Region("Menu", player, multiworld)
    base = Region("Base", player, multiworld)

    excluded = _excluded_location_names(world)

    # This game has no meaningful topology to model - everything is reachable from a
    # single flat "Base" region, with individual access rules (see Rules.py) gating the
    # few locations/goal that actually depend on specific items.
    base.locations += [
        BallXPitLocation(player, name, data.code, base)
        for name, data in location_table.items()
        if name not in excluded
    ]

    connection = Entrance(player, "Menu -> Base", menu)
    menu.exits.append(connection)
    connection.connect(base)

    multiworld.regions += [menu, base]
