# Ball x Pit Setup Guide

## Required Software

- [Ball X Pit](https://store.steampowered.com/app/2765070/BALL_X_PIT/) (Steam)
- [MelonLoader](https://melonloader.co), installed into your Ball X Pit install
- The BallXPitArchipelago mod (see this project's README for install instructions)

## Installation

1. Install MelonLoader into your Ball X Pit game folder and launch the game once so it
   finishes setting itself up.
2. Install the BallXPitArchipelago mod DLL and its dependencies as described in the
   project README.
3. Launch the game - a connect box will appear on screen. Enter your Archipelago server's
   host/port, your slot name, and password (if any), then connect.

## What Gets Randomized

- Character unlocks
- Building blueprints
- Base (land) expansion
- Which biomes/levels you can access

Your goal is to gain access to and complete all 8 biomes.

## Evosanity (optional)

Set `evosanity` in your YAML to add a check for every unique ball you discover:

- `none` (default) - no ball checks.
- `evolutions` - 69 checks, one per evolved ball (the ones you make by merging level-3 balls).
- `all_balls` - those 69 plus 21 for the base balls: every ball entry in the encyclopedia.

Checks fire the moment you discover a ball, mid-run. Balls already discovered on your current
save are checked as soon as you connect, so **start evosanity on a fresh save** unless you want
a pile of checks immediately.

Set `goal: evosanity` to make discovering all 69 evolved balls part of winning, on top of
beating all 8 biomes. Expect a much longer game than the default goal.

`evosanity_jumpstart: true` starts you with the 12 buildings that speed evolutions up, plus
resources to place them. It never changes which evolutions are possible, and deliberately grants
no biome access.

Fused balls (the thousands of arbitrary two-ball combinations) are not included - only the named
evolved balls that get their own encyclopedia entry.
