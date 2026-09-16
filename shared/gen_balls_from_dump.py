"""
Regenerates game_data.json's "balls" section from a [BALLDUMP] MelonLoader log.

Usage:
    py -3.12 gen_balls_from_dump.py "D:\\...\\BALLxPIT\\MelonLoader\\Latest.log"

Run the mod's Debug build once (DebugBallDump.cs emits the [BALLDUMP] lines as soon as InfoDB
exists - no Archipelago connection or generated seed needed), then point this at the log. It
rewrites the "balls" array in BOTH game_data.json copies, which must stay byte-identical:
shared/game_data.json is the source of truth the C# side generates from, and
apworld/ballxpit/game_data.json is the copy that actually ships inside the .apworld.

Why parse a log rather than read the game's assets directly: Unity compiles IL2CPP method
bodies to native code, so ilspycmd can only show signatures - MergeComponents' real contents
only exist at runtime. See DebugBallDump.cs for the dump's own documentation.

Ball ids are 900800 + the ball's HeroType ordinal, matching how every other section of
game_data.json derives ids from enum position. That range is append-only, exactly like the
existing ones - see the file's own "_comment" header. Only locations use these ids (there are
no ball ITEMS - the 7 biome-gated base balls stay vanilla, see Balls.py), but the id still
comes from the frozen registry so a future change of heart doesn't have to renumber anything.
"""
import json
import os
import re
import sys

BALL_ID_BASE = 900800

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
TARGETS = [
    os.path.join(REPO, "shared", "game_data.json"),
    os.path.join(REPO, "apworld", "ballxpit", "game_data.json"),
]


def parse(log_path: str):
    """
    Returns (balls, recipes). Only the LAST dump in the log is used - a log can hold several
    launches, and an older one may predate a game update.
    """
    hero_lines, slot_lines = [], []
    for raw in open(log_path, encoding="utf-8", errors="replace"):
        if "[BALLDUMP]" not in raw:
            continue
        body = raw.split("[BALLDUMP]", 1)[1].strip()
        if body.startswith("=== begin"):
            # A new dump started - discard anything collected from an earlier launch.
            hero_lines, slot_lines = [], []
        elif body.startswith("HERO|"):
            hero_lines.append(body)
        elif body.startswith("SLOT|"):
            slot_lines.append(body)

    if not hero_lines:
        sys.exit(f"No [BALLDUMP] HERO lines found in {log_path}")

    def fields(line):
        return dict(p.split("=", 1) for p in line.split("|")[1:] if "=" in p)

    # SLOT lines are recipe VARIANTS, not "slots of alternatives" - each one lists every
    # ingredient that one complete recipe needs. See DebugBallDump.DumpRecipe's comment.
    recipes = {}
    for line in slot_lines:
        f = fields(line)
        recipes.setdefault(f["type"], []).append(f["alts"].split(","))

    balls = []
    for line in hero_lines:
        f = fields(line)
        idx = int(f["idx"])
        enum = f["type"]
        merged = f["isMerged"] == "True"

        if f["isInGame"] != "True":
            # Nothing in the real roster hits this (all 90 are in-game, unlike BuildingType's
            # cut entries) - but if a future update adds a dead enum value, drop it here rather
            # than shipping an unreachable location for it.
            print(f"  skipping {enum}: isInGame=False")
            continue

        balls.append({
            "enum": enum,
            "display": f["name"],
            "id": BALL_ID_BASE + idx,
            "merged": merged,
            # Which biome must be beaten before this ball enters the level-up pool. Only 7 of
            # the 90 have one; the rest are available from a fresh save. null, not "kNum".
            "req_level": None if f["reqLevel"] == "kNum" else f["reqLevel"],
            # Post-launch content marker (0 = shipped at launch). 30 of the 90 are non-zero.
            "min_version": int(f["minVersion"]),
            "recipes": recipes.get(enum, []),
        })

    return balls


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)

    balls = parse(sys.argv[1])
    merged = [b for b in balls if b["merged"]]
    base = [b for b in balls if not b["merged"]]
    gated = [b for b in balls if b["req_level"]]

    print(f"parsed {len(balls)} balls: {len(base)} base, {len(merged)} evolved, {len(gated)} biome-gated")

    missing = [b["enum"] for b in merged if not b["recipes"]]
    if missing:
        sys.exit(f"evolved balls with no recipe (dump incomplete?): {missing}")

    known = {b["enum"] for b in balls}
    for b in merged:
        for recipe in b["recipes"]:
            unknown = [i for i in recipe if i not in known]
            if unknown:
                sys.exit(f"{b['enum']} recipe references unknown ingredients: {unknown}")

    for path in TARGETS:
        with open(path, encoding="utf-8") as fh:
            data = json.load(fh)
        data["balls"] = balls
        with open(path, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(data, fh, indent=2, ensure_ascii=False)
            fh.write("\n")
        print(f"  wrote {path}")


if __name__ == "__main__":
    main()
