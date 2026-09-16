"""
Generates mod/BallXPitArchipelago/GameNames.Balls.Generated.cs from shared/game_data.json.

Usage:
    py -3.12 gen_ball_names.py

Run after shared/gen_balls_from_dump.py has refreshed the "balls" section (e.g. after a game
update changes the roster). Writes a separate partial-class file rather than touching
GameNames.Generated.cs, so regenerating ball tables can never disturb the character/building/
level tables - those came from a different dump and have their own provenance notes.

Why a table at all, instead of deriving names from the HeroType enum token: the real display
names genuinely don't reverse-parse. kHeavy is "Iron", kLaserCross is "Holy Laser", kNuke is
"Nuclear Bomb", kEggSacSac is "Voluptuous Egg Sac". Same lesson GameNames.Generated.cs's own
header records for buildings.
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SRC = os.path.join(HERE, "game_data.json")
DEST = os.path.join(REPO, "mod", "BallXPitArchipelago", "GameNames.Balls.Generated.cs")

HEADER = '''// AUTO-GENERATED from shared/game_data.json by shared/gen_ball_names.py. Do not hand-edit -
// regenerate instead. Separate from GameNames.Generated.cs on purpose: that file's tables come
// from a different live dump with its own provenance, and regenerating one shouldn't churn the
// other.
//
// Explicit table, not a formula, for the same reason as the building names: real ball names
// don't reverse-parse from their enum token (kHeavy is "Iron", kLaserCross is "Holy Laser",
// kNuke is "Nuclear Bomb"). Sourced from a live [BALLDUMP] walk of InfoDB.I.Heroes - see
// DebugBallDump.cs and shared/gen_balls_from_dump.py, never from the wiki.
//
// EvolvedBalls is vanilla's own UpgradeInfo.IsMerged() for each ball, not a guess: the 69
// evolved balls are the ones you construct by merging, the other 21 are base balls offered
// directly at level-up. BallHooks uses the split to pick each ball's location name prefix and
// to decide what the evosanity goal requires.
using Il2Cpp;

namespace BallXPitArchipelago;

internal static partial class GameNames
{
'''


def main() -> None:
    with open(SRC, encoding="utf-8") as fh:
        balls = json.load(fh)["balls"]

    evolved = [b for b in balls if b["merged"]]
    base = [b for b in balls if not b["merged"]]

    out = [HEADER]
    out.append(f"    /// <summary>All {len(balls)} balls: HeroType -> real in-game display name.</summary>\n")
    out.append("    internal static readonly System.Collections.Generic.Dictionary<HeroType, string> BallNames = new()\n    {\n")
    for b in balls:
        out.append(f'        {{ HeroType.{b["enum"]}, "{b["display"]}" }},\n')
    out.append("    };\n\n")

    out.append(
        f"    /// <summary>The {len(evolved)} evolved (merged) balls - what evosanity's "
        f"\"Evolve: X\" locations and the evosanity goal cover.</summary>\n"
    )
    out.append("    internal static readonly System.Collections.Generic.HashSet<HeroType> EvolvedBalls = new()\n    {\n")
    for b in evolved:
        out.append(f"        HeroType.{b['enum']},\n")
    out.append("    };\n\n")

    out.append(
        f"    /// <summary>The {len(base)} base balls - evosanity's \"Discover Ball: X\" "
        "locations, only used when evosanity is set to all_balls.</summary>\n"
    )
    out.append("    internal static readonly System.Collections.Generic.HashSet<HeroType> BaseBalls = new()\n    {\n")
    for b in base:
        out.append(f"        HeroType.{b['enum']},\n")
    out.append("    };\n}\n")

    with open(DEST, "w", encoding="utf-8", newline="\r\n") as fh:
        fh.write("".join(out))

    print(f"Wrote {DEST}")
    print(f"  {len(balls)} balls ({len(base)} base, {len(evolved)} evolved)")


if __name__ == "__main__":
    main()
