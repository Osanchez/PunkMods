# PUNK Mods

A suite of [BepInEx](https://github.com/BepInEx/BepInEx) / Harmony mods for the Unity game **PUNK** (Steam Playtest), by **Trihardest**.

## Install

Mods are distributed as **separate downloads** on the [latest release](https://github.com/Osanchez/PunkMods/releases/latest): `BepInEx-Setup.zip` (the loader — install once) plus one `<Mod>.zip` per mod. No other downloads are needed; BepInEx is included.

**Requirements:** the **PUNK Playtest** installed via Steam, on 64‑bit Windows.

### 1. Find your game folder
In Steam: **PUNK Playtest → right‑click → Manage → Browse local files**. This opens the folder containing **`Punk.exe`** — everything below extracts *here*.

### 2. Install the loader (first time only)
Download **`BepInEx-Setup.zip`** and extract its contents into the game folder, so that `winhttp.dll` and the `BepInEx\` folder sit **next to `Punk.exe`** (choose merge/replace if Windows asks). Launch the game once — BepInEx initializes on this first, slightly slower launch — then quit. You only do this step once.

### 3. Add the mods you want
For each mod, download its **`<Mod>.zip`** and extract into the *same* game folder. Each drops a `BepInEx\plugins\<Mod>\` folder. Take as many or as few as you like — **Mods Menu** is recommended, since most mods expose their toggles through its in‑game menu.

### 4. Launch & configure
Start the game and open **Settings → MODS** for per‑mod toggles. After the first launch, each mod also writes a `.cfg` into `BepInEx\config\` that you can edit in a text editor (e.g. `PlayerCount`, `GamepadPollingHz`) and relaunch.

**Verify it worked:** open `BepInEx\LogOutput.log` in the game folder — near the end you should see `Chainloader startup complete`, plus a line for each mod that loaded.

### Updating & removing
- **Update** a mod: re‑download its zip and extract over the old files (replace when asked).
- **Remove** a mod: delete its `BepInEx\plugins\<Mod>\` folder.
- **Uninstall everything:** delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt`, and the `BepInEx\` folder — or **Verify integrity of game files** in Steam.

### Multiplayer note
Local & Remote Play co‑op uses **controllers**: one keyboard/mouse player plus a controller for everyone else (a Unity limitation allows only one keyboard/mouse player). For Remote Play friends, set them to a **controller** in the Steam Remote Play player list.

> ⚠️ These target a **Playtest** build — a game update can temporarily break mods until they're rebuilt.

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
- **Sell Vault Items** — Hover a spare module in the Vault and press the north face button (Y / Triangle, or a keyboard key) to sell it for its base shop buy price, credited to your run currency.
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

### Development (debugging & hot-reload)

These are **local-only** workflows. Distribution is always a Release build and **never** ships `.pdb` symbols; the dev flags below only touch your own install and skip packaging.

PUNK runs on **Mono** (not IL2CPP), so managed debugging works.

#### Step-through debugging

1. Build with symbols and deploy them to your local install:
   ```
   powershell -File build-package.ps1 -Debug
   ```
   This does a Debug (unoptimized) build and copies each mod's `.dll` **and** `.pdb` into `BepInEx/plugins/<Mod>/`. (Run it again without `-Debug` to go back to optimized Release DLLs.)
2. Enable the player's managed debugger — add these two lines to **`Punk_Data/boot.config`** (a game file, so it's local-only and not distributed):
   ```
   player-connection-debug=1
   wait-for-managed-debugger=0
   ```
3. Launch the game, then attach a debugger and set breakpoints in the mod source:
   - **[dnSpyEx](https://github.com/dnSpyEx/dnSpy)** — *Debug → Attach to Process → Unity*, pick `Punk.exe`. It uses the deployed `.pdb` for your mods and decompiles the game itself so you can step into `Punk.Main` too.
   - **JetBrains Rider / Visual Studio** — *Attach to Unity Process* (needs the `.pdb`).

#### Live hot-reload

Rebuild a single mod and reload it in-game without restarting, via the in-repo **PunkDevReload** plugin (no external tools). It's built and deployed to your local install by any normal `build-package.ps1` run, and is **excluded from distribution**. It watches `BepInEx/scripts/`.

1. Stage the mod you're iterating on into `scripts/`:
   ```
   powershell -File build-package.ps1 -HotReload PunkFourPlayer
   ```
   This Debug-builds just that mod into `BepInEx/scripts/` and removes it from `plugins/` (so it isn't loaded twice).
2. In-game, press the reload key (default **F10**, configurable in `BepInEx/config/com.osanchez.punk.devreload.cfg`) after each rebuild. PunkDevReload destroys the old instance — running its `OnDestroy` teardown — then loads the new build from bytes; a matching `.pdb` is loaded too, so breakpoints still hit.
3. When done, restore the mod to a normal plugin by re-running the build without `-HotReload` (Release build) or with `-Debug`.

> **Reload cleanliness:** every mod unpatches its Harmony hooks and tears down its runtime objects in `OnDestroy`, so PunkDevReload can reload it without stacking duplicate patches or leaking UI. Keep that pattern (store the `Harmony` instance in `Awake`, undo everything in `OnDestroy`) when adding new mods. Note Mono can't truly unload assemblies, so many reloads in one session will slowly grow memory — restart occasionally.

### Releases

Pushes/merges to `main` build every mod and publish a **per-mod GitHub Release** automatically (one `<Mod>.zip` each, plus `BepInEx-Setup.zip`). See **[RELEASING.md](RELEASING.md)** for the CI setup, the private reference-DLL repo, and how to refresh the DLLs after a game update.

## Per-mod metadata

Each mod folder has a `mod.yaml` (`name` / `author` / `version` / `description`) read at runtime by the Mods Menu for its section header and description tooltip — edit it to change a label without recompiling.
