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

**Start with `PunkModsMenu.zip`** — it adds the in‑game **MODS** tab to Settings that most other mods
plug their toggles into. Note it shows **nothing on its own**: the tab only appears once you *also*
install at least one other mod that registers into it. Installing only Mods Menu and looking for a tab
is the #1 "it's not working" report — that's expected, not a broken install.

## Verify it worked

Open `BepInEx\LogOutput.log` in the game folder. Near the end you should see:

```
[Message:   BepInEx] Chainloader startup complete
```

plus a line for each mod loading (e.g. `PUNK Damage Slow-Mo v1.0.0 loaded.`).

## Troubleshooting

`BepInEx\LogOutput.log` (next to `Punk.exe`) answers almost everything — read it top‑down:

- **No `LogOutput.log`, or no `Chainloader startup complete` in it** → BepInEx isn't loading. Make sure
  `winhttp.dll` is **directly next to `Punk.exe`** (not nested in a subfolder), that you launched the
  game once after installing the loader, and that you're on 64‑bit Windows. If Steam re‑verified the
  game files, `winhttp.dll` can get deleted — reinstall `BepInEx-Setup.zip`.
- **No MODS tab in Settings** → you need a mod *besides* Mods Menu installed (see step 3). Look for
  `Injected MODS tab with N row(s).` in the log; if it's absent, nothing registered a row.
- **`Chainloader startup complete` is there, but a mod's `... loaded.` line is missing** → that mod's
  DLL isn't where BepInEx looks. It must be at `BepInEx\plugins\<Mod>\<Mod>.dll`. Re‑extract that
  mod's zip into the game folder (the zip already contains the `BepInEx\plugins\<Mod>\` path — don't
  nest it deeper).
- **A mod loads but seems to do nothing** → check `BepInEx\plugins\<Mod>\config.cfg` (some default to
  off), and scan the log for a `... failed` warning from that mod.
- **It worked before and suddenly doesn't** → the Playtest probably updated; these mods can break on a
  game update until they're rebuilt.

When asking for help, share `BepInEx\LogOutput.log` — it pinpoints the failing stage.

## Layout after install

```
PUNK Playtest\                  <- game folder (where Punk.exe is)
├─ Punk.exe, Punk_Data\, ...    (existing - game)
├─ winhttp.dll                  [loader]  + doorstop_config.ini, .doorstop_version, changelog.txt
└─ BepInEx\
   ├─ core\                     BepInEx runtime (the loader itself)
   ├─ config\                   BepInEx's own config (not the mods')
   └─ plugins\                  THE MODS - one folder each
       ├─ PunkModsMenu\         (install the mods you want; one folder per mod)
       ├─ PunkFourPlayer\       each mod folder gets its own config.cfg on first launch
       └─ ...
```

Remove a single mod later by deleting its **folder** from `BepInEx\plugins\`.

## Settings

- **In game:** open **Settings → MODS** for toggles (most mods register here via Mods Menu — which
  must be installed alongside at least one other mod for the tab to appear).
- **Config files:** after the first launch, each mod writes a `config.cfg` into **its own folder**,
  `BepInEx\plugins\<Mod>\config.cfg`, pre‑filled with working defaults. Edit with a text editor and
  relaunch (e.g. `BepInEx\plugins\PunkFourPlayer\config.cfg` → `PlayerCount`,
  `BepInEx\plugins\PunkInputTweaks\config.cfg` → `GamepadPollingHz`). Mods work out of the box with
  the defaults, with or without the MODS tab.

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
