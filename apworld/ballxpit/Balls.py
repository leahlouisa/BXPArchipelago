"""
The ball roster and evolution recipe graph, plus the one derived fact the access rules need.

Ball data lives in game_data.json's "balls" section (generated from a live [BALLDUMP] log by
shared/gen_balls_from_dump.py - see that file and DebugBallDump.cs). This module is the shaping
layer over it, in the same spirit as BlueprintPools.py/CharHousing.py.

Deliberately knows NOTHING about level ordering. Rules.py owns LEVEL_UNLOCK_ORDER and its
_level_access_positions() helper, and is the only place that turns "needs Clouds" into "needs 6
copies of Progressive Level Access" - so this module reports which BIOMES a ball transitively
depends on and lets Rules.py price that. Keeps the ordering knowledge in exactly one place and
avoids a circular import (Rules.py -> Items.py -> ... -> here).
"""
import json
from importlib import resources
from typing import Dict, List, Mapping

# .apworld files are loaded as zip archives via zipimport, so a plain open() on a path built
# from __file__ doesn't work here - importlib.resources is zip-safe. Same as Items.py/Locations.py.
_game_data = json.loads(
    (resources.files(__package__) / "game_data.json").read_text(encoding="utf-8-sig")
)

_balls = _game_data["balls"]

BALL_DISPLAY: Dict[str, str] = {b["enum"]: b["display"] for b in _balls}
BALL_ID: Dict[str, int] = {b["enum"]: b["id"] for b in _balls}

# 21 base balls (obtained straight from level-up offers) and 69 evolved ones (made by merging).
# The split is vanilla's own UpgradeInfo.IsMerged(), not a guess: confirmed live for all 90.
BASE_BALL_ENUMS: List[str] = [b["enum"] for b in _balls if not b["merged"]]
EVOLVED_BALL_ENUMS: List[str] = [b["enum"] for b in _balls if b["merged"]]

# ball_enum -> list of complete alternative recipes, each a list of ingredient ball enums.
# NOT slots of interchangeable ingredients (that was the initial misreading of
# UpgradeInfo.MergeComponents): kAssassin's [["kHeavy","kGhost"],["kHeavy","kDark"]] means
# "Iron+Ghost OR Iron+Dark". Ingredients can themselves be evolved balls - kSatan is
# Incubus+Succubus - so resolving one bottoms out through a tree, not a single step.
BALL_RECIPES: Dict[str, List[List[str]]] = {
    b["enum"]: [list(r) for r in b["recipes"]] for b in _balls
}

# The only 7 balls vanilla gates behind anything: one per biome for the first 7 biomes in
# difficulty order. Vast Void (kMoon) gates NOTHING - confirmed two ways, via each ball's own
# GetRequiredLevel() and via InfoDB.UnlocksByLevel showing an empty list for kMoon. The other
# 83 balls are available from a fresh save.
#
# These stay 100% vanilla in the randomizer - beating the biome unlocks its ball as it always
# has, with no AP item and no suppression. Turning them into AP grants was considered and
# rejected: it would need a patch on HeroInfo.IsUnlocked() and would stay leaky anyway, since
# characters hand out their starting ball regardless of unlock state (e.g. the Carouser starts
# with Charm), and the randomizer can deliver a character long before its ball's biome.
REQUIRED_BIOME_BY_BALL: Dict[str, str] = {
    b["enum"]: b["req_level"] for b in _balls if b["req_level"]
}


def required_access_by_ball(biome_position: Mapping[str, int]) -> Dict[str, int]:
    """
    ball_enum -> how many Progressive Level Access copies its easiest recipe path needs.

    `biome_position` is Rules.py's _level_access_positions() (level_enum -> copies needed to
    reach that biome). Taking it as an argument rather than importing it is what lets this
    module stay free of level-ordering knowledge while still costing paths CORRECTLY - an
    earlier version of this function tried to stay pure by minimising the number of distinct
    biomes instead, which is a different and wrong metric: one recipe needing only Clouds (6
    copies) would beat one needing Graveyard+Snowy (1 copy) on biome count while being far more
    expensive in the only currency the access rule actually spends.

    Within a recipe every ingredient is required, so a recipe costs the MAX of its ingredients.
    Across recipes the player takes whichever is cheapest, so that's a MIN. Getting those two
    the wrong way round would under-restrict and could produce an unwinnable seed.

    Fixpoint iteration rather than plain recursion: an ingredient may itself be an evolved ball
    whose own cost isn't known yet on the first pass (kSatan is Incubus+Succubus), so each pass
    recomputes from scratch and costs only relax downward as more becomes known. Settles in 2
    passes on the real graph, whose maximum merge depth is 2; the loop bound is a cheap guard in
    case a future update introduces something deeper or cyclic.
    """
    cost: Dict[str, int] = {}
    for enum in BASE_BALL_ENUMS:
        biome = REQUIRED_BIOME_BY_BALL.get(enum)
        cost[enum] = biome_position[biome] if biome else 0

    for _ in range(len(_balls)):
        changed = False
        for enum in EVOLVED_BALL_ENUMS:
            best = None
            for recipe in BALL_RECIPES[enum]:
                if any(ingredient not in cost for ingredient in recipe):
                    continue  # not resolvable yet - a later pass will pick it up
                need = max(cost[ingredient] for ingredient in recipe)
                if best is None or need < best:
                    best = need
            if best is not None and cost.get(enum) != best:
                cost[enum] = best
                changed = True
        if not changed:
            break

    unresolved = [e for e in EVOLVED_BALL_ENUMS if e not in cost]
    if unresolved:
        raise AssertionError(
            f"ball recipe graph did not resolve for {unresolved} - check game_data.json's "
            "'balls' section for a recipe referencing a ball that doesn't exist"
        )

    return cost
