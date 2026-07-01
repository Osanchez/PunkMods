# PUNK Four Player (Phase-1 PoC)

Adds local players **3 and 4** to a co-op run — a proof of concept to validate that the engine
can actually run 4 ships before investing in a polished input screen and HUD layout.

This is a **separate, standalone plugin** (`PunkFourPlayer.dll`). Delete it and the game returns
to native 1–2 players; the other mods are unaffected.

## How to use

1. Start a **co-op** run from the menu (pick your 2 devices as usual).
2. The mod adds ships up to the configured **PlayerCount** (default 4), auto-assigning the next
   free **gamepads** to players 3–4.
3. The shared camera (ProCamera2D) auto-frames all ships; the shared wallet/score is already
   shared across all players.

Config: `BepInEx/config/com.osanchez.punk.fourplayer.cfg` → `PlayerCount` (2–4).

## Scope & limits (PoC)

- **New co-op runs only.** Single-player and *continued* saves are untouched.
- **Extra players use spare gamepads only.** If you don't have enough controllers, players 3–4
  still **spawn** (proving camera/economy/HUD flow) but sit uncontrolled. Keyboard is not
  auto-assigned to extras yet.
- **HUDs are rough** — players 3–4 get cloned HUDs offset upward; layout is not final.
- **P1/P2 UI model.** The game identifies players by a binary `IsPlayerTwo` flag, so players 3–4
  alternate onto the two existing UI "sides" (grid/shop mirroring, rumble). Functional, not
  polished.

## How it works (hooks)

- `ShipManager.PlaceShipEntitiesToStartPosition` (prefix) — captures the starting loadout.
- `ShipManager.SpawnShipGameObjects` (postfix) — places + spawns the extra ship entities near the
  start node (reusing the game's private `PlaceShipEntity` / `Spawn` / `AssignTheme`), assigning
  spare gamepads and themes. Robust against entity ordering (spawns only ships that don't already
  exist).
- `GameController.AssignHuds` (postfix) — clones the last HUD for each extra ship.

Everything is gated to new co-op runs and wrapped in try/catch, so a failure degrades to "2
players" rather than crashing.

## Phase 2 (not done yet)

- A real N-slot **input-selection screen** (the vanilla one is left/right = P1/P2).
- Proper **4-player HUD layout** (four corners).
- Player-**index** identity to replace the binary `IsPlayerTwo` (clean P3/P4 UI).
- Keyboard support for extra players; continued-save support for 4-player runs.
