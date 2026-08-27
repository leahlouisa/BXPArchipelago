# Ball X Pit Archipelago

An [Archipelago](https://archipelago.gg) randomizer integration for [Ball X Pit](https://store.steampowered.com/app/2765070/BALL_X_PIT/), made of two parts:

- `mod/BallXPitArchipelago` - a [MelonLoader](https://melonloader.co) mod that connects the game to an Archipelago server, receives items, and sends location checks.
- `apworld/ballxpit` - the Python "world" package that plugs into Archipelago's multiworld generator so `Ball x Pit` can be included in a multiworld.

---

## Randomizer Info (read before installing)

### Goal

Complete all 8 biome levels.

### Checks (141 total)

- Unlocking a character (22)
- Unlocking a building blueprint (80), in three flavors:
  - **62 buildings** are drawn from every level's real boss-drop pool and pooled together across all 8 biomes, then re-partitioned per seed with a randomized count per level - one seed might offer 3 blueprints in the Boneyard and 12 in the Savanna, the next something entirely different. The vanilla building itself is fully replaced by a random check reward; discovery order and how many a biome offers are both shuffled per-seed.
  - **7 per-level first-completion "Trophies"** work the same way - suppressed and replaced by a random check reward the first time you beat that biome.
  - **A handful of other buildings** (tied to specific characters via housing) still work the older way - you get the real vanilla building *and* a bonus check reward from the same trigger.
  - One specific building, the **Void Trophy**, is deliberately excluded from all of the above and never appears as a reward in any seed - it's permanently repurposed as internal plumbing that keeps the blueprint-discovery chain correct (see the "For developers" section if you're curious why).
- Completing a biome level for the first time (8)
- Upgrading the elevator - still costs gears and works exactly as in vanilla, only the reward is randomized (7)
- Purchasing a base land expansion chunk - still costs resources and works exactly as in vanilla, only the reward is randomized (24 - the game has 25 total land tracts, 1 of which you start with)

### Items (141 total, matching the check count exactly)

- Characters (22) and Blueprints (80) - unlock that specific character/building directly, independent of however you'd normally earn it in vanilla
- Level Access (8) - required to make the matching biome selectable; this is what actually gates progress toward the goal
- Land Expansion (No Effect) (24) - filler, does nothing on its own (land expansion purchases aren't gated by items in this randomizer, only by the vanilla resource cost)
- Wood / Stone / Wheat / Gold - small filler resource grants

### Options

- `death_link` - if enabled, dying in Ball x Pit kills every other DeathLink-enabled player's character, and dying in their game ends your current run.

---

## How to install

Important: this expects your own legitimate copy of Ball X Pit on Steam.

### Windows

- Make sure you have [.NET 6.0](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) installed.
- Download and install [MelonLoader](https://melonloader.co) into your Ball X Pit install (this mod is built and tested against v0.7.3).
- Launch the game once, then close it - this lets MelonLoader finish generating its interop files.
- Download `BallXPitArchipelago.zip` from the [latest release](https://github.com/leahlouisa/BXPArchipelago/releases/latest) and extract it:
    - `BallXPitArchipelago.dll` goes in your game's `Mods` folder.
    - `Archipelago.MultiClient.Net.dll` and `Newtonsoft.Json.dll` go in your game's `UserLibs` folder.
- Launch the game again - a connect box will appear on screen. Enter your Archipelago server's host/port, your slot name, and password (if any), then connect.
- To uninstall, delete `BallXPitArchipelago.dll` from `Mods` (and the two dependency DLLs from `UserLibs`, if nothing else in your mod setup needs them).

### SteamOS / Steam Deck (via Proton)

MelonLoader officially supports Wine/Steam Proton, and this mod has no Windows-specific code of its own - it's ordinary .NET/Harmony code, so it runs the same way under Proton as it does natively. A few extra one-time setup steps are needed first though:

1. Switch to Desktop Mode and open a terminal.
2. Install Protontricks via Flatpak (SteamOS's root filesystem is read-only, so this can't go through a normal package manager like `pacman`/AUR):
   ```
   flatpak install flathub com.github.Matoking.protontricks
   ```
   Every `protontricks` command below needs the `flatpak run com.github.Matoking.protontricks` prefix in place of bare `protontricks`.
3. Find Ball X Pit's AppID: `flatpak run com.github.Matoking.protontricks -s "BALL x PIT"`.
4. Install the .NET 6.0 Desktop Runtime into the game's Proton prefix: `flatpak run com.github.Matoking.protontricks [appid] dotnetdesktop6`.
5. In Steam, right-click Ball X Pit -> Properties -> General -> Launch Options, and set:
   ```
   WINEDLLOVERRIDES="version=n,b" %command%
   ```
6. Install MelonLoader manually rather than via its Windows installer: download the MelonLoader release zip, then right-click Ball X Pit -> Manage -> Browse local files to open the install folder directly, and extract the zip's contents (the `MelonLoader/` folder and `version.dll`) straight into it.
7. Launch the game once to let MelonLoader finish its setup, then follow the same steps as the Windows instructions above (download `BallXPitArchipelago.zip`, place the DLLs in `Mods`/`UserLibs`).

If MelonLoader doesn't seem to load (no `MelonLoader/Latest.log` appears in the game folder), double-check the launch option is exact and that step 4 actually completed - those are the two most common points of failure.

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

MVP complete: Characters, Blueprints, Levels, Elevator Upgrades, and Land Expansion are all randomized, blueprint discovery order (and how many blueprints each biome offers) is shuffled per-seed, DeathLink is supported, received items pop an on-screen notification, and goal completion is reported to the server automatically. See `shared/game_data.json` for the generated list of characters/buildings/levels the integration is built around.
