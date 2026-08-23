# Ball X Pit Archipelago

An [Archipelago](https://archipelago.gg) randomizer integration for [Ball X Pit](https://store.steampowered.com/app/2765070/BALL_X_PIT/), made of two parts:

- `mod/BallXPitArchipelago` - a [MelonLoader](https://melonloader.co) mod that connects the game to an Archipelago server, receives items, and sends location checks.
- `apworld/ballxpit` - the Python "world" package that plugs into Archipelago's multiworld generator so `Ball x Pit` can be included in a multiworld.

---

## Randomizer Info (read before installing)

### Goal

Complete all 8 biome levels.

### Checks (~124 total)

- Unlocking a character (22)
- Unlocking a building blueprint, including each biome's first-completion "Trophy" (72)
- Completing a biome level for the first time (8)
- Upgrading the elevator - still costs gears and works exactly as in vanilla, only the reward is randomized (7)
- Purchasing a base land expansion chunk - still costs resources and works exactly as in vanilla, only the reward is randomized (15)

### Items (124 total, matching the check count exactly)

- Characters (22) and Blueprints (72) - unlock that specific character/building directly, independent of however you'd normally earn it in vanilla
- Level Access (8) - required to make the matching biome selectable; this is what actually gates progress toward the goal
- Land Expansion (No Effect) (15) - filler, does nothing on its own (land expansion purchases aren't gated by items in this randomizer, only by the vanilla resource cost)
- Wood / Stone / Wheat / Gold - small filler resource grants

### Options

- `death_link` - if enabled, dying in Ball x Pit kills every other DeathLink-enabled player's character, and dying in their game ends your current run.

---

## How to install

Important: this expects your own legitimate copy of Ball X Pit on Steam.

- Make sure you have [.NET 6.0](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) installed.
- Download and install [MelonLoader](https://melonloader.co) into your Ball X Pit install (this mod is built and tested against v0.7.3).
- Launch the game once, then close it - this lets MelonLoader finish generating its interop files.
- Download `BallXPitArchipelago.zip` from the [latest release](https://github.com/leahlouisa/BXPArchipelago/releases/latest) and extract it:
    - `BallXPitArchipelago.dll` goes in your game's `Mods` folder.
    - `Archipelago.MultiClient.Net.dll` and `Newtonsoft.Json.dll` go in your game's `UserLibs` folder.
- Launch the game again - a connect box will appear on screen. Enter your Archipelago server's host/port, your slot name, and password (if any), then connect.
- To uninstall, delete `BallXPitArchipelago.dll` from `Mods` (and the two dependency DLLs from `UserLibs`, if nothing else in your mod setup needs them).

If you're generating/hosting the multiworld rather than just playing in one, you'll also need `ballxpit.apworld` from the same release - drop it in Archipelago's `custom_worlds` folder before generating.

---

## For developers: building from source

Prerequisites:
- Your own legitimate copy of Ball X Pit, with [MelonLoader](https://melonloader.co) installed and the game launched at least once (so `MelonLoader\Il2CppAssemblies\` exists - the mod links against these to call into the game).
- [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0).

Steps:
1. In `mod/BallXPitArchipelago/`, copy `GameDir.props.example` to `GameDir.props` and edit the path inside to point at your own local Ball X Pit install. This file is gitignored - it's yours alone, never commit it.
2. `dotnet build mod/BallXPitArchipelago/BallXPitArchipelago.csproj` (or pass `-p:GameDir="C:\path\to\BALLxPIT"` instead of using the props file, if you prefer).
3. Copy `bin/Debug/net6.0/BallXPitArchipelago.dll` into your game's `Mods` folder, and `Archipelago.MultiClient.Net.dll` + `Newtonsoft.Json.dll` from the same output folder into `UserLibs`.

We don't (and can't) redistribute anything from the game itself - no assemblies, no assets. Everything under `mod/` is original code that references your own local copy of the game's interop assemblies only at build time; the resulting DLL is portable across any install of the same game version. There's also no CI build for this project: building requires the interop assemblies generated from your own legally-owned copy of the game, which can't be fetched by a public build server.

To package the apworld yourself instead of using a release build: zip the contents of `apworld/ballxpit/` into a folder named `ballxpit` inside `ballxpit.apworld` (a `.apworld` file is just a zip archive).

## Project status

MVP complete: Characters, Blueprints, Levels, Elevator Upgrades, and Land Expansion are all randomized, DeathLink is supported, and goal completion is reported to the server automatically. See `shared/game_data.json` for the generated list of characters/buildings/levels the integration is built around.
