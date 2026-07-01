# PUNK Debug Menu Key (F1)

A tiny BepInEx plugin that lets you open PUNK's built-in **developer debug menu** by pressing
**F1** (instead of the awkward Ctrl+Alt+D chord, which fails if you're holding a movement key).

**Esc** still closes the menu. The original Ctrl+Alt+D keeps working too — this just *adds* F1.

## How it works (v2)

PUNK ships a full cheat/debug menu (`DebugMenu`) wired into the in-game scene, opened by the
game with Ctrl+Alt+D. This plugin adds **F1** as an alternative.

It Harmony-postfixes `DebugMenu.Update()`: each frame it checks `Keyboard.current.f1Key`
(the new Input System) and, on a fresh press, **replays the game's own open branch** — sets
the private `isOpened` flag, disables ship control, hovers the ship, slows time to 0.1×, opens
the `UIScreen`, and refreshes the weapon dropdown. Because it sets `isOpened`, the game's own
`Update` still handles **Esc to close** and reverses everything cleanly.

> **Why not just rebind the key?** v1 added an F1 binding to the game's `showDebugInputAction`
> and called `SetActive` on the menu during scene load. On this brand-new Unity 6 build, mutating
> a live `InputAction` (Disable/AddBinding/Enable) **hard-crashed the game**. v2 never touches the
> input system or activates objects — it only reads a key and calls public game methods, fully
> wrapped in try/catch. Much safer.

To rebind to another key, change the `OpenPressed()` body in `Plugin.cs` (e.g.
`kb.f3Key.wasPressedThisFrame` or `kb.backquoteKey.wasPressedThisFrame`).

**Limitation:** F1 only works while the `DebugMenu` GameObject is active (so its `Update` runs).
If a future build ships it inactive, neither F1 nor Ctrl+Alt+D would work without also
re-activating it.

## Prerequisites

1. **Install a BepInEx loader into the game first.** Unity 6.3 needs **BepInEx 6 (Bleeding
   Edge), the *Mono* x64 build** — BepInEx 5 predates Unity 6. Unzip it into the game root
   (`PUNK Playtest/`) so you get `winhttp.dll` + a `BepInEx/` folder, then **launch the game
   once** so BepInEx generates `BepInEx/core/*.dll` and `BepInEx/plugins/`.
2. **.NET SDK** (already present on this machine: `dotnet 10`).

## Build

```sh
cd "C:/Program Files (x86)/Steam/steamapps/common/PUNK Playtest/Mods/PunkDebugKey"
dotnet build -c Release
```

The csproj references the game's DLLs from `..\..\Punk_Data\Managed` and BepInEx/Harmony from
`..\..\BepInEx\core` (both via absolute `GameDir`). If your install path differs, edit
`<GameDir>` in `PunkDebugKey.csproj`.

Output: `bin/Release/PunkDebugKey.dll`.

## Install

Copy the built DLL into the loader's plugins folder:

```
PUNK Playtest/BepInEx/plugins/PunkDebugKey.dll
```

Launch the game. The BepInEx console / `BepInEx/LogOutput.log` should show:

```
[Info :PUNK Debug Menu Key (F1)] ... loaded. Press F1 in a run to open the debug menu (Esc closes).
[Info :PUNK Debug Menu Key (F1)] Bound F1 to the debug menu. Press F1 to open, Esc to close.
```

(The second line appears once you're in an actual run, where the menu exists.)

## Troubleshooting

- **`BepInEx.Core.dll` not found when building** — you haven't installed BepInEx yet, or not
  the Mono build. Install it and run the game once so `BepInEx/core/` is populated.
- **`BaseUnityPlugin` / `using BepInEx.Unity.Mono;` doesn't resolve** — you're on BepInEx 5,
  not 6. Reference `BepInEx.dll` instead of the two split assemblies in the csproj, and delete
  the `using BepInEx.Unity.Mono;` line in `Plugin.cs`.
- **No "Bound F1" log line in a run** — the field `showDebugInputAction` may have been renamed
  in a newer build. Check `BepInEx/LogOutput.log` for the warning; the doc set can be
  regenerated to find the new name. Ctrl+Alt+D still works in the meantime.
- **BepInEx itself won't hook the game (no log at all)** — try the latest BepInEx 6 BE Mono
  build; if that fails on this Unity version, MelonLoader is the fallback loader.

## BepInEx version note

Written for **BepInEx 6 (Mono)**. The only version-sensitive parts are the `BaseUnityPlugin`
base class and the `[BepInPlugin]` attribute — everything else (Unity + game API) is identical
across loaders.
