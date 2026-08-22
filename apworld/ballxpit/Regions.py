from BaseClasses import Entrance, Region

from .Locations import BallXPitLocation, location_table


def create_regions(world) -> None:
    multiworld = world.multiworld
    player = world.player

    menu = Region("Menu", player, multiworld)
    base = Region("Base", player, multiworld)

    # This game has no meaningful topology to model - everything is reachable from a
    # single flat "Base" region, with individual access rules (see Rules.py) gating the
    # few locations/goal that actually depend on specific items.
    base.locations += [
        BallXPitLocation(player, name, data.code, base)
        for name, data in location_table.items()
    ]

    connection = Entrance(player, "Menu -> Base", menu)
    menu.exits.append(connection)
    connection.connect(base)

    multiworld.regions += [menu, base]
