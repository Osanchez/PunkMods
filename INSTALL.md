# PUNK Mods — Install Guide

Community mods for **PUNK** (the Steam **Playtest** build), powered by
[BepInEx](https://github.com/BepInEx/BepInEx). This `BepInEx-Setup.zip` is the **loader only** —
install it once, then add each mod's own `<Mod>.zip` from the release page.

## Requirements

- The **PUNK Playtest** installed via Steam (the build these mods were made for).
- Windows (64‑bit).

## Install

### 1. Find the game folder
In Steam: **PUNK Playtest → right‑click → Manage → Browse local files** — the folder containing
**`Punk.exe`**. Everything extracts here.

### 2. Install the loader (once)
Extract **everything in this `BepInEx-Setup.zip`** into the game folder, so that `winhttp.dll` and
the `BepInEx` folder sit **next to `Punk.exe`** (choose merge/replace if Windows asks). Launch the
game once — BepInEx initializes on this first, slightly slower launch — then quit.

### 3. Add mods
For each mod you want, download its **`<Mod>.zip`** from the release page and extract into the *same*
game folder. Each drops a `BepInEx\plugins\<Mod>\` folder (its `.dll`, plus a `config.cfg` and any
assets after you launch).

## Verify it worked

Open `BepInEx\LogOutput.log` in the game folder. Near the end you should see:

```
[Message:   BepInEx] Chainloader startup complete
```

plus a line for each mod loading (e.g. `PUNK Input Tweaks ... loaded`).

## Layout after install

```
PUNK Playtest\                  <- game folder (where Punk.exe is)
├─ Punk.exe, Punk_Data\, ...    (existing - game)
├─ winhttp.dll                  [loader]  + doorstop_config.ini, .doorstop_version, changelog.txt
└─ BepInEx\
   ├─ core\                     BepInEx runtime (the loader itself)
   ├─ config\                   each mod's .cfg (created on first launch)
   └─ plugins\                  THE MODS - one folder each
       ├─ PunkModsMenu\         (install the mods you want; one folder per mod)
       ├─ PunkFourPlayer\
       └─ ...
```

Remove a single mod later by deleting its **folder** from `BepInEx\plugins\`.

## Settings

- **In game:** open **Settings → MODS** for toggles (most mods register here via Mods Menu).
- **Config files:** after the first launch, each mod has a `.cfg` in `BepInEx\config\`. Edit with a
  text editor and relaunch (e.g. `…fourplayer.cfg` → `PlayerCount`, `…inputtweaks.cfg` →
  `GamepadPollingHz`).

## Multiplayer note

Local and Remote Play multiplayer uses **controllers**: one keyboard/mouse player plus controllers
for everyone else (a Unity limitation means only one keyboard/mouse can be a player). For Remote Play
friends, set them to a **controller** in the Steam Remote Play player list.

## Uninstall

Delete these from the game folder: `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`,
`changelog.txt`, and the `BepInEx` folder. (Or just **Verify integrity of game files** in Steam.)

## Caveats

- This targets a **Playtest** build — a game update can break mods until they're rebuilt.
- Unofficial and provided as‑is. Each mod is a separate folder in `BepInEx\plugins\`; delete any one
  you don't want.
