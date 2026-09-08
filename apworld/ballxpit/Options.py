from dataclasses import dataclass

from Options import DeathLink, PerGameCommonOptions, Range


class FillerWoodAmount(Range):
    """How much Wood a single Wood filler item grants. Vanilla-matching default: 50."""
    display_name = "Filler Wood Amount"
    range_start = 0
    range_end = 999
    default = 50


class FillerStoneAmount(Range):
    """How much Stone a single Stone filler item grants. Vanilla-matching default: 50."""
    display_name = "Filler Stone Amount"
    range_start = 0
    range_end = 999
    default = 50


class FillerWheatAmount(Range):
    """How much Wheat a single Wheat filler item grants. Vanilla-matching default: 50."""
    display_name = "Filler Wheat Amount"
    range_start = 0
    range_end = 999
    default = 50


class FillerGoldAmount(Range):
    """How much Gold a single Gold filler item grants. Vanilla-matching default: 200."""
    display_name = "Filler Gold Amount"
    range_start = 0
    range_end = 9999
    default = 200


class BuildingCostPercent(Range):
    """
    Scales every building's UPGRADE cost (Wood/Stone/Wheat/Gold) to this percent of its real
    vanilla cost - e.g. 25 means an upgrade that costs 800 Gold/200 Wheat in vanilla instead
    costs 200 Gold/50 Wheat. 100 = unchanged vanilla cost. Does not affect land expansion (see
    land_expansion_cost_percent) or elevator upgrade gear costs, which always stay vanilla.

    KNOWN LIMITATION (as of this apworld version): this currently only scales UPGRADE cost, not
    a building's initial placement/blueprint cost - a bug in the mod's placement-cost discount
    causes a guaranteed freeze on any purchase, so it's temporarily disabled there until fixed.
    Buildings still cost their full vanilla amount to place.
    """
    display_name = "Building Cost Percent"
    range_start = 1
    range_end = 100
    default = 100


class LandExpansionCostPercent(Range):
    """
    Scales every land expansion chunk's Gold cost to this percent of its real vanilla cost.
    100 = unchanged vanilla cost. Independent of building_cost_percent, so land can be
    discounted without discounting buildings or vice versa.
    """
    display_name = "Land Expansion Cost Percent"
    range_start = 1
    range_end = 100
    default = 100


@dataclass
class BallXPitOptions(PerGameCommonOptions):
    death_link: DeathLink
    filler_wood_amount: FillerWoodAmount
    filler_stone_amount: FillerStoneAmount
    filler_wheat_amount: FillerWheatAmount
    filler_gold_amount: FillerGoldAmount
    building_cost_percent: BuildingCostPercent
    land_expansion_cost_percent: LandExpansionCostPercent
