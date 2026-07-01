# PUNK Mods

A suite of [BepInEx](https://github.com/BepInEx/BepInEx) / Harmony mods for the Unity game **PUNK** (Steam Playtest), by **Trihardest**.

## Install

Grab the [latest release](https://github.com/Osanchez/PunkMods/releases/latest):

1. Extract **`BepInEx-Setup.zip`** into your PUNK Playtest folder (next to `Punk.exe`) — **once**. This installs the BepInEx loader.
2. Extract each **`<Mod>.zip`** you want into the same folder (each drops `BepInEx/plugins/<Mod>/`).
3. Launch the game, then open **Settings → MODS** to configure them.

Update a mod by re-extracting its zip; remove one by deleting its `BepInEx/plugins/<Mod>/` folder.

## Mods

### Framework / UI
- **Mods Menu** — Framework that adds a MODS tab to Settings; other mods register toggles, lists, and buttons into it (alphabetized per-mod sections with `Name By Author (vVersion)` headers and a description tooltip).
- **UI Fixes** — Fixes vanilla UI alignment, e.g. the co-op ASSIGN INPUT header drifting on ultrawide aspect ratios.
- **Debug Menu Key** — Opens the game's built-in developer debug menu with F1.

### Co-op
- **Four Player** — Local + Remote Play co-op for up to 4 players: dynamic join screen, extra ships & HUDs, press-Start-to-join, the ready-up profile picker, one-player-per-slot, P3/P4 colors + rumble settings.
- **Sim Controller** — Debug tool: emulate controllers from your keyboard/mouse to test co-op solo.
- **Meta Loadout** — Persists each player's ship build per class and profile across runs; profiles are chosen on the player-select screen.
- **Player Highlight** — Neon ring + P1–P4 label around each player's ship, tinted to that ship's color.
- **Scoreboard** — Hold Tab for a co-op scoreboard (kills, bosses, deaths, damage, time alive, HP, fuel).

### Gameplay
- **Seed Picker** — World-seed entry screen after class selection; also shows the seed on the pause menu.
- **Damage Slow-Mo** — Toggles the brief slow-motion the game plays when a player takes damage.
- **Dash I-Frames** — Short invincibility window while dashing.
- **Revive Item** — Adds the Revive Beacon, a shop consumable that revives a random downed player.
- **Input Tweaks** — Raises the Input System polling rate to cut gamepad input latency.

## Building

Each mod is a `netstandard2.1` project referencing the game's `Punk_Data/Managed` DLLs and BepInEx core via `HintPath`s under the `GameDir` MSBuild property (defaults to the game install; override with `-p:GameDir=...`).

```
# build + deploy to the local install + package a single all-in-one bundle
powershell -ExecutionPolicy Bypass -File build-package.ps1

# or one zip per mod + BepInEx-Setup.zip (what CI publishes)
powershell -ExecutionPolicy Bypass -File build-package.ps1 -PerMod
```

The script auto-discovers every `*.csproj`, builds it, copies each mod's DLL (+ `mod.yaml`) into `BepInEx/plugins/<Mod>/`, and zips the result into `dist/`.

### Releases

Pushes/merges to `main` build every mod and publish a **per-mod GitHub Release** automatically (one `<Mod>.zip` each, plus `BepInEx-Setup.zip`). See **[RELEASING.md](RELEASING.md)** for the CI setup, the private reference-DLL repo, and how to refresh the DLLs after a game update.

## Per-mod metadata

Each mod folder has a `mod.yaml` (`name` / `author` / `version` / `description`) read at runtime by the Mods Menu for its section header and description tooltip — edit it to change a label without recompiling.
