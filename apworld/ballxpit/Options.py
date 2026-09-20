from dataclasses import dataclass

from Options import Choice, DeathLink, PerGameCommonOptions, Range, Toggle


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
    Scales every building's cost (Wood/Stone/Wheat/Gold) - both its initial placement/blueprint
    cost and its UPGRADE cost - to this percent of its real vanilla cost. E.g. 25 means an
    upgrade that costs 800 Gold/200 Wheat in vanilla instead costs 200 Gold/50 Wheat. 100 =
    unchanged vanilla cost. Does not affect land expansion (see land_expansion_cost_percent) or
    elevator upgrade gear costs, which always stay vanilla.
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


class FillerBundleMultiplier(Range):
    """
    How much more a "bundle" filler item grants than its plain counterpart - a Wood Crate gives
    this many times a Wood, a Gold Cache this many times a Gold.

    Defaults to 1, i.e. a Crate/Cache grants exactly what its plain version does and the two are
    flavour rather than a real difference. That's deliberate: at the original default of 3, a
    player running raised filler amounts (say 2000 Gold) was receiving 6000 Gold per Gold Cache,
    which flooded the base economy badly enough that building costs stopped mattering. Raise it
    if you want bundles to be a genuine windfall, but raise filler_*_amount OR this, not both.
    """
    display_name = "Filler Bundle Multiplier"
    range_start = 1
    range_end = 10
    default = 1


class Evosanity(Choice):
    """
    Adds a check for every unique ball you discover ("evosanity", in the spirit of Stardew
    Valley's fishsanity/cooksanity).

    none: off - no ball checks at all, exactly as previous versions.

    evolutions: a check for each of the 69 EVOLVED balls, the ones you make by merging two or
    more level-3 balls at the Fusion Reactor (Bomb, Frozen Flame, Nosferatu, Satan...). Each one
    is also an encyclopedia entry. Fused balls are NOT included - those are the thousands of
    arbitrary two-ball combinations, which the game doesn't track individually.

    all_balls: the 69 evolved balls plus a check for each of the 21 BASE balls (Burn, Freeze,
    Iron...), i.e. every ball entry in the in-game encyclopedia. 90 checks total.

    Checks fire the moment a ball is discovered, mid-run - you don't have to finish the run.
    Balls you have already discovered on this save are checked as soon as you connect.

    Warning: this adds a lot of locations without adding any items, so most of what fills them is
    filler. See filler_bundle_multiplier.
    """
    display_name = "Evosanity"
    option_none = 0
    option_evolutions = 1
    option_all_balls = 2
    default = 0


class Goal(Choice):
    """
    What counts as winning.

    all_biomes: beat all 8 biomes. The default, and what every previous version did.

    evosanity: discover all 69 evolved balls AND beat all 8 biomes. A superset of all_biomes, not
    an alternative to it - deliberately, because no ball is unlocked by beating the 8th biome
    (Vast Void unlocks nothing at all), so "evolve everything" on its own would leave the hardest
    biome skippable. Expect a much longer game: collecting every evolution means many runs, since
    you can only carry a handful of balls at a time.

    Setting this to evosanity does NOT turn evosanity checks on by itself - set evosanity too, or
    the goal will simply have no checks associated with it.
    """
    display_name = "Goal"
    option_all_biomes = 0
    option_evosanity = 1
    default = 0


class EvosanityJumpstart(Toggle):
    """
    Start with the 12 buildings that make evolving easier, plus a pile of resources to place them
    with: Jeweler, Necromancer, Matchmaker, Candle Maker, Gambler's Den, Casino, Wishing Well,
    Evolution Chamber, Exorcist, Gemsmith, Bag Maker, and Adventurer's Guild.

    These are normally late-game blueprints, and hunting for all 69 evolutions without them is a
    slog. None of them changes which evolutions are POSSIBLE, only how quickly you reach them
    (the Evolution Chamber, for instance, just starts every new ball at level 2), so this is
    purely a pacing option and never affects whether a seed can be completed.

    Deliberately does NOT grant biome access - that stays the randomizer's main progression, and
    handing it over would let you skip most of the seed. Amounts are set by the jumpstart_*_amount
    options; if you also use building_cost_percent to discount buildings, you'll need less.

    Their blueprint items are removed from the item pool (you already have them) and replaced with
    filler, so their locations still hold something worth finding.
    """
    display_name = "Evosanity Jumpstart"


class JumpstartGoldAmount(Range):
    """How much Gold evosanity_jumpstart grants. Ignored unless that option is on."""
    display_name = "Jumpstart Gold Amount"
    range_start = 0
    range_end = 99999
    default = 5000


class JumpstartWoodAmount(Range):
    """How much Wood evosanity_jumpstart grants. Ignored unless that option is on."""
    display_name = "Jumpstart Wood Amount"
    range_start = 0
    range_end = 9999
    default = 1000


class JumpstartStoneAmount(Range):
    """How much Stone evosanity_jumpstart grants. Ignored unless that option is on."""
    display_name = "Jumpstart Stone Amount"
    range_start = 0
    range_end = 9999
    default = 1000


class JumpstartWheatAmount(Range):
    """How much Wheat evosanity_jumpstart grants. Ignored unless that option is on."""
    display_name = "Jumpstart Wheat Amount"
    range_start = 0
    range_end = 9999
    default = 1000


@dataclass
class BallXPitOptions(PerGameCommonOptions):
    death_link: DeathLink
    goal: Goal
    evosanity: Evosanity
    evosanity_jumpstart: EvosanityJumpstart
    jumpstart_gold_amount: JumpstartGoldAmount
    jumpstart_wood_amount: JumpstartWoodAmount
    jumpstart_stone_amount: JumpstartStoneAmount
    jumpstart_wheat_amount: JumpstartWheatAmount
    filler_wood_amount: FillerWoodAmount
    filler_stone_amount: FillerStoneAmount
    filler_wheat_amount: FillerWheatAmount
    filler_gold_amount: FillerGoldAmount
    filler_bundle_multiplier: FillerBundleMultiplier
    building_cost_percent: BuildingCostPercent
    land_expansion_cost_percent: LandExpansionCostPercent
