# Real Tracks for Motorsport Manager

Real Tracks adds two additive Quick Race venues to the 2016 Steam version of Motorsport Manager:

**Download:** [Nexus Mods](https://www.nexusmods.com/motorsportmanager/mods/30) · [GitHub releases](https://github.com/kyledavidson8511/motorsport-manager-real-tracks/releases)

- **Austin** — COTA Grand Prix layout
- **Monte Carlo** — Monaco street circuit

It is a Unity Mod Manager mod. It does not replace stock circuits or alter career saves.

## Install

1. Install Unity Mod Manager for Motorsport Manager.
2. Download `RealTracks-1.0.0.zip` from [Nexus Mods](https://www.nexusmods.com/motorsportmanager/mods/30) or the [latest GitHub release](https://github.com/kyledavidson8511/motorsport-manager-real-tracks/releases/latest).
3. Extract its `RealTracks` folder to `Steam/steamapps/common/Motorsport Manager/Mods/`.
4. Start the game, open Unity Mod Manager, and confirm **Real Tracks** is active.
5. Open Quick Race and select Austin or Monte Carlo.

The packaged `MD5SUMS.txt` lets you verify a manual install. Do not overwrite game files or remove existing mods to install this mod.

## Compatibility and scope

- Tested with Motorsport Manager + Unity Mod Manager 0.33.2.
- All venue-specific art options are configurable in the UMM Art panel.
- Monaco uses generated scenery and road presentation. The game’s own road and vehicle systems remain responsible for driving and collision behavior.

## Build from source

The source under `src/` targets .NET 3.5 and references the game-managed assemblies plus Unity Mod Manager and Harmony. Those game assemblies are intentionally not in this repository. Place compatible references locally, build `src/RealTracks.csproj`, then package the resulting DLL with `Info.json`, `tracks/`, and `ui/` as the `RealTracks` mod folder.

## Credits and data licence

See `packs/austin/credits.md` and `packs/monaco/credits.md`. Pack geometry derived from OpenStreetMap is attributed and shared under ODbL 1.0. Terrain and orthophoto sources are credited in the relevant pack.

## Feedback

Please open an issue with your game version, UMM version, active-mod list, a screenshot or short clip, and the relevant `output_log` excerpt.
