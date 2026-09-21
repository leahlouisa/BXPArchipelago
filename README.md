# Ball X Pit Archipelago

An [Archipelago](https://archipelago.gg) randomizer integration for [Ball X Pit](https://store.steampowered.com/app/2765070/BALL_X_PIT/), made of two parts:

- `mod/BallXPitArchipelago` - a [MelonLoader](https://melonloader.co) mod that connects the game to an Archipelago server, receives items, and sends location checks.
- `apworld/ballxpit` - the Python "world" package that plugs into Archipelago's multiworld generator so `Ball x Pit` can be included in a multiworld.

---

## Randomizer Info (read before installing)

### Goal

Complete all 8 biome levels. Optionally (`goal: evosanity`) also discover all 69 evolved balls — see "Evosanity" below.

### Checks (140 by default, up to 230 with evosanity - see the table below)

- Unlocking a character (21 - see "What doesn't get randomized" below for the 22nd)
- Unlocking a building blueprint (80), in three flavors:
  - **62 buildings** are drawn from every level's real vanilla blueprint-discovery pool, in vanilla's real order and count for that biome - unlike an earlier version of this mod, this composition is now fixed, not shuffled, specifically so a hint naming a check tells you something true about vanilla play. These locations are named positionally rather than by building name (e.g. "Boneyard pooled blueprint #4") since a real building name only helps if you already have vanilla's biome-by-biome layout memorized - what you actually *receive* for finding one is still fully randomized.
  - **7 per-level first-completion "Trophies"** work similarly - suppressed and replaced by a random check reward the first time you beat that biome. These keep their real names (e.g. "Boneyard Trophy") since there's exactly one per level, no ambiguity to resolve.
  - **11 buildings** are tied to unlocking a specific character via in-game housing, through a real vanilla mechanism this mod hasn't been able to fully pin down (the trigger and, for one of the 11, even the home biome remain unconfirmed) - named positionally where a likely home biome is known (e.g. "Boneyard char-housing blueprint #1", not live-verified - treat it as a rough hint, not a guarantee), or "Char-housing blueprint (unconfirmed level)" for the one that isn't. These never hold anything critical to completing the seed, specifically because their real timing is unknown.
  - One specific building, the **Void Trophy**, is deliberately excluded from all of the above and never appears as a reward in any seed - it's permanently repurposed as internal plumbing that keeps the blueprint-discovery chain correct.
- Completing a biome level for the first time (8)
- Upgrading the elevator - still costs gears and works exactly as in vanilla, only the reward is randomized (7)
- Purchasing a base land expansion chunk - still costs resources and works exactly as in vanilla, only the reward is randomized (24 - the game has 25 total land tracts, 1 of which you start with)
- **Discovering a ball**, if `evosanity` is enabled (69 or 90 - off by default). See "Evosanity" below.

### Evosanity (optional)

`evosanity` adds a check for every unique ball you discover, in the spirit of Stardew Valley's fishsanity/cooksanity. Off by default.

- `evosanity: evolutions` adds **69** checks, one per **evolved ball** - the ones you construct by merging two or more level-3 balls at the Fusion Reactor (Bomb, Frozen Flame, Holy Laser, Nosferatu, Satan...). Each is also an in-game encyclopedia entry.
- `evosanity: all_balls` adds those 69 plus **21** more for the **base balls** (Burn, Freeze, Iron...) - i.e. every ball entry in the encyclopedia, 90 checks total.

Checks fire the instant a ball is discovered, mid-run; you don't have to finish the run. Balls you've already discovered on the current save are checked as soon as you connect, so evosanity is best started on a fresh save.

Fused balls are **not** included - those are the thousands of arbitrary two-ball combinations, which the game doesn't track individually and which have no encyclopedia entries.

Only 7 of the 90 balls are gated by anything in vanilla (one per biome, for the first 7 biomes - Vast Void unlocks no balls at all), and those stay completely vanilla here: beating the biome unlocks its ball as normal, with no AP item involved. Each ball check is logically gated behind the biomes its recipe transitively needs, so 42 of the 69 evolutions are available in your very first run and the rest unlock steadily as biomes do.

Two caveats worth knowing before you pick it:

- **It's long.** Collecting every evolution takes many runs, since you can only carry a handful of balls at a time and each ingredient must reach level 3.
- **It's filler-heavy.** Evosanity adds up to 90 locations without adding any items of its own, so everything filling them is filler by construction. Half of that is the run-scoped Free Level Up / Fusion Reactor rewards rather than resources, which keeps it from being both monotonous and inflationary - but if you also raise `filler_*_amount`, expect to end up very rich.

### Items (always matching the check count exactly)

- Characters (21) and Blueprints (80) - unlock that specific character/building directly, independent of however you'd normally earn it in vanilla. Unlocking a character doesn't require ever building their real housing building - receiving the item is enough.
- Progressive Level Access (7 copies of one item) - each copy received unlocks whichever biome is next in your own real difficulty order (not the same as the order levels are listed in-game), regardless of when or from where it arrives in the multiworld; this is what actually gates progress toward the goal
- Wood / Stone / Wheat / Gold - filler resource grants (land expansion purchases aren't gated by items in this randomizer, only by the vanilla resource cost - these checks just grant Wood/Stone/Wheat like any other filler)
- Wood Crate / Stone Crate / Wheat Crate / Gold Cache - the same resources in a different denomination (`filler_bundle_multiplier` times as much, default 1x - i.e. flavour unless you raise it). Only appear in seeds that need extra filler, i.e. with `evosanity` and/or `evosanity_jumpstart` on.
- **Free Level Up** and **Fusion Reactor** - run-scoped rewards that help only the run you're currently playing, rather than piling more into the permanent base economy. **Half of all filler is these**, in every configuration. If you receive one while you're not in a level, it's held and delivered as soon as you're properly into your next one (including waiting out any Gemsmith/Antique Shop bonus-pick screen).
  - *Free Level Up* grants a full level's worth of XP, so it's worth the same wherever you happen to be in the XP bar.
  - *Fusion Reactor* drops a real fuser pickup at your feet, exactly like a naturally-spawned one - you still have to collect it. Particularly welcome in an evosanity run, since one of a fuser's three options is the evolution option.

Note that balls themselves are **not** items - evosanity contributes checks only. Ball unlocks stay exactly as they are in vanilla.

### Counts by configuration

Items always match checks exactly. `evosanity_jumpstart` moves 12 blueprints out of the pool into your starting inventory and backfills them with filler, so it changes the mix without changing the totals.

| | `none` | `evolutions` | `all_balls` | `all_balls` + jumpstart |
|---|---:|---:|---:|---:|
| **Checks** | | | | |
| Characters | 21 | 21 | 21 | 21 |
| Building blueprints | 80 | 80 | 80 | 80 |
| Complete a biome | 8 | 8 | 8 | 8 |
| Elevator upgrades | 7 | 7 | 7 | 7 |
| Land expansions | 24 | 24 | 24 | 24 |
| Evolved balls | – | 69 | 69 | 69 |
| Base balls | – | – | 21 | 21 |
| **Total** | **140** | **209** | **230** | **230** |
| **Items** | | | | |
| Characters | 21 | 21 | 21 | 21 |
| Building blueprints | 80 | 80 | 80 | 68 *(+12 to start)* |
| Progressive Level Access | 7 | 7 | 7 | 7 |
| Filler | 32 | 101 | 122 | 134 |
| **Total** | **140** | **209** | **230** | **230** |

Roughly half of the filler in any column is run-scoped (Free Level Up / Fusion Reactor) and half is resources; an odd filler total splits one off-centre.

### What doesn't get randomized

The Influencer (unlocked in vanilla only via Twitch Extension integration - linking a Twitch account, letting your audience vote on in-run events) is left completely untouched: no item, no location. Randomizing it would force every player to set up Twitch integration just to get a "real" unlock from this mod, which isn't something this randomizer wants to require. If you go through vanilla's own Twitch steps, you'll unlock them exactly as in an unmodded game.

### Options

- `death_link` - if enabled, dying in Ball x Pit kills every other DeathLink-enabled player's character, and dying in their game ends your current run.
- `goal` - `all_biomes` (default) or `evosanity`. `evosanity` requires discovering all 69 evolved balls **and** beating all 8 biomes; it's a superset of `all_biomes`, not an alternative, deliberately, since no ball comes from the 8th biome and "evolve everything" alone would leave it skippable. Setting this doesn't enable the checks - set `evosanity` too.
- `evosanity` - `none` (default), `evolutions` (69 checks) or `all_balls` (90 checks). See "Evosanity" above.
- `evosanity_jumpstart` - start with the 12 buildings that make evolving easier (Jeweler, Necromancer, Matchmaker, Candle Maker, Gambler's Den, Casino, Wishing Well, Evolution Chamber, Exorcist, Gemsmith, Bag Maker, Adventurer's Guild) plus resources to place them. None of them changes which evolutions are *possible*, only how fast you get there, so this never affects whether a seed can be completed. Deliberately grants no biome access - that stays the randomizer's main progression. Their blueprint items leave the pool and are replaced with filler, so their locations still hold something worth finding. Default off.
- `jumpstart_gold_amount` / `jumpstart_wood_amount` / `jumpstart_stone_amount` / `jumpstart_wheat_amount` - how much `evosanity_jumpstart` grants. Defaults 5000/1000/1000/1000; you'll need less if you also discount buildings via `building_cost_percent`.
- `filler_wood_amount` / `filler_stone_amount` / `filler_wheat_amount` / `filler_gold_amount` - how much of that resource a single filler grant is worth. Vanilla-matching defaults: 50/50/50/200.
- `filler_bundle_multiplier` - how much more a Crate/Cache grants than its plain counterpart. Default 1 (bundles are flavour unless you raise it). Raise `filler_*_amount` OR this, not both - at 3x with raised amounts a single Gold Cache can hand you thousands of gold and flatten the building economy.
- `building_cost_percent` - scales every building's blueprint and upgrade cost (Wood/Stone/Wheat/Gold) to this percent of its real vanilla cost, e.g. 25 turns an 800 Gold/200 Wheat building into 200 Gold/50 Wheat. Default 100 (unchanged). Doesn't affect elevator upgrade gear costs, which always stay vanilla.
- `land_expansion_cost_percent` - same idea, independently, for land expansion chunks' Gold cost. Default 100 (unchanged).

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
- **Updating to a new version**: only replace the DLLs above - don't delete or wipe the whole `Mods`/`UserLibs` folders. Your connection settings and per-seed progress (which blueprint chain positions you've already found) live in `UserData/BallXPitArchipelago.*` instead, specifically so a routine DLL update can't accidentally wipe them.

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

MVP complete: Characters, Blueprints, Levels, Elevator Upgrades, and Land Expansion are all randomized (against vanilla's real per-biome blueprint composition, not a shuffled one), DeathLink is supported, received items pop an on-screen notification, and goal completion is reported to the server automatically. See `shared/game_data.json` for the generated list of characters/buildings/levels the integration is built around.
