# Ball X Pit Archipelago

An [Archipelago](https://archipelago.gg) randomizer integration for [Ball X Pit](https://store.steampowered.com/app/2765070/BALL_X_PIT/), made of two parts:

- `mod/BallXPitArchipelago` - a [MelonLoader](https://melonloader.co) mod that connects the game to an Archipelago server, receives items, and (eventually) sends location checks.
- `apworld/ballxpit` - the Python "world" package that plugs into Archipelago's multiworld generator so `Ball x Pit` can be included in a multiworld. *(not written yet - see the project plan)*

This project is a work in progress. See `shared/game_data.json` for the generated list of characters/buildings/levels the integration is built around.

## For players

Once a release is published, playing will just mean:

1. Install [MelonLoader](https://melonloader.co) into your own Ball X Pit install (launch the game once afterward so MelonLoader can generate its interop files).
2. Download the latest `BallXPitArchipelago` release from this repo's Releases page and unzip it: the mod DLL goes in the game's `Mods` folder, the two dependency DLLs (`Archipelago.MultiClient.Net.dll`, `Newtonsoft.Json.dll`) go in `UserLibs`.
3. Launch the game once so the mod writes `Mods\BallXPitArchipelago.json`, then edit that file with your Archipelago server host/port/slot name/password.
4. Launch again to connect.

No release exists yet - the mod isn't functionally complete (see the project plan/phases below). Until then, playing means building from source (next section).

**Nothing in this repo or in the mod itself is tied to any particular computer.** The compiled mod DLL only needs the *same version* of the game to run - it doesn't embed any machine-specific paths. Machine-specific paths only ever exist at *build time* (see below), and are gitignored so they never end up in the repo.

## For developers: building from source

Prerequisites:
- Your own legitimate copy of Ball X Pit, with [MelonLoader](https://melonloader.co) installed and the game launched at least once (so `MelonLoader\Il2CppAssemblies\` exists - the mod links against these to call into the game).
- [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0).

Steps:
1. In `mod/BallXPitArchipelago/`, copy `GameDir.props.example` to `GameDir.props` and edit the path inside to point at your own local Ball X Pit install. This file is gitignored - it's yours alone, never commit it.
2. `dotnet build mod/BallXPitArchipelago/BallXPitArchipelago.csproj` (or pass `-p:GameDir="C:\path\to\BALLxPIT"` instead of using the props file, if you prefer).
3. Copy `bin/Debug/net6.0/BallXPitArchipelago.dll` into your game's `Mods` folder, and `Archipelago.MultiClient.Net.dll` + `Newtonsoft.Json.dll` from the same output folder into `UserLibs`.

We don't (and can't) redistribute anything from the game itself - no assemblies, no assets. Everything under `mod/` is original code that references your own local copy of the game's interop assemblies only at build time; the resulting DLL is portable across any install of the same game version. There's also no CI build for this project: building requires the interop assemblies generated from your own legally-owned copy of the game, which can't be fetched by a public build server.

## Project status

See the phased plan this project follows for full context on design decisions and what's done vs. pending.
