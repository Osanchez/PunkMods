# PUNK Mods — Install Guide

A bundle of community mods for **PUNK** (the Steam **Playtest** build), powered by
[BepInEx](https://github.com/BepInEx/BepInEx). BepInEx is included in this package — you don't
need to download anything else.

## Requirements

- The **PUNK Playtest** installed via Steam (same build these mods were made for).
- Windows (64‑bit).

## Install (onto a fresh game)

1. In Steam, find **PUNK Playtest** → right‑click → **Manage → Browse local files**. This opens
   the game folder — the one containing **`Punk.exe`**.
2. Extract **everything in this zip into that folder**, so that `winhttp.dll` and the `BepInEx`
   folder sit **right next to `Punk.exe`**. (Choose "merge/replace" if Windows asks.)
3. Launch the game once. BepInEx initializes on first launch (it's a little slower that once).

That's it.

## Verify it worked

Open `BepInEx\LogOutput.log` in the game folder. You should see, near the end:

```
[Message:   BepInEx] Chainloader startup complete
```

plus a line for each mod loading (e.g. `PUNK Input Tweaks ... loaded`).

## What this adds to your game folder

The zip has **no wrapper folder** — its contents go straight into the game folder, next to
`Punk.exe`. After extracting, your folder looks like this (game files unchanged; new items marked):

```
PUNK Playtest\                  <- your game folder (where Punk.exe is)
├─ Punk.exe                     (existing - game)
├─ Punk_Data\  MonoBleedingEdge\ ...   (existing - game)
│
├─ winhttp.dll                  [NEW] BepInEx loader
├─ doorstop_config.ini          [NEW]
├─ .doorstop_version            [NEW]
├─ changelog.txt                [NEW] BepInEx changelog
├─ INSTALL.md                   [NEW] this file
│
└─ BepInEx\                     [NEW]
   ├─ core\                     BepInEx runtime (the loader itself)
   ├─ plugins\                  THE MODS - one folder each (dll + its config + assets)
   │   ├─ PunkDebugKey\     PunkDebugKey.dll
   │   ├─ PunkFourPlayer\   PunkFourPlayer.dll   (+ config.cfg after first launch)
   │   ├─ PunkInputTweaks\  PunkInputTweaks.dll  (+ config.cfg)
   │   ├─ PunkMetaLoadout\  PunkMetaLoadout.dll
   │   ├─ PunkModsMenu\     PunkModsMenu.dll
   │   ├─ PunkSimController\PunkSimController.dll (+ config.cfg)
   │   └─ PunkUiFixes\      PunkUiFixes.dll
   └─ patchers\                 (empty)
```

Each mod keeps its **own folder** with its `.dll`, its `config.cfg` (created on first launch), and
any assets. Edit a mod's `config.cfg` with a text editor and relaunch to change its settings; most
toggles are also in the in-game **Settings -> MODS** tab. Also created on first launch:
`BepInEx\cache\` and `BepInEx\LogOutput.log`.

To remove a single mod later, just delete its **folder** from `BepInEx\plugins\`.

## What's included

| Mod | What it does | Keys / where |
|---|---|---|
| **Input Tweaks** | Lowers controller input latency (raises gamepad polling 60→250 Hz) | automatic |
| **Four Player** | Local **up to 4 players** in a co‑op run (controllers) | start a co‑op run; config `PlayerCount` |
| **Mods Menu** | Adds a **MODS** tab to the Settings screen (framework other mods plug into) | Settings → MODS |
| **Meta Loadout** | Keep your ship build + vault **across death** (roguelite progression) | Settings → MODS → Clear Progress |
| **Sim Controller** | Debug: emulate multiple controllers with one keyboard | Settings → MODS → toggle on; F6 add, F7/F8 select, F9 remove, J/L move, K ready |
| **Debug Menu Key** | Opens the game's built‑in developer/cheat menu | **F1** in a run (Esc closes) |
| **UI Fixes** | Fixes the co‑op ASSIGN INPUT header alignment on ultrawide | automatic |

## Settings

- **In game:** open **Settings → MODS** for toggles (enable controller emulation, clear saved
  progress, …).
- **Config files:** after the first launch, each mod has a `.cfg` in `BepInEx\config\`. Edit with
  a text editor and relaunch (e.g. `…fourplayer.cfg` → `PlayerCount`, `…inputtweaks.cfg` →
  `GamepadPollingHz`).

## Multiplayer note

Local and Remote Play multiplayer uses **controllers**: one keyboard/mouse player plus controllers
for everyone else (a Unity limitation means only one keyboard/mouse can be a player). For Remote
Play friends, set them to a **controller** in the Steam Remote Play player list.

## Uninstall

Delete these from the game folder: `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`,
`changelog.txt`, and the `BepInEx` folder. (Or just **Verify integrity of game files** in Steam.)

## Caveats

- This targets a **Playtest** build — a game update can break mods until they're rebuilt.
- Unofficial and provided as‑is. Each mod is a separate file in `BepInEx\plugins\`; you can delete
  any one you don't want.
