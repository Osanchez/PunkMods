# PUNK Sim Controller (debug)

Emulate multiple controllers with a single keyboard, so a local‑multiplayer join screen (and
4‑player gameplay) can be tested **solo**. Standalone plugin (`PunkSimController.dll`).

Each "sim controller" is a **real virtual gamepad** added to the Unity Input System
(`InputSystem.AddDevice<Gamepad>()`), so the game treats it exactly like a physical pad — it
appears as a row on the join screen and can drive a ship. Your keyboard puppets whichever sim is
**selected**.

## Keys

Chosen to avoid the join screen's own keyboard row (which uses A/D/arrows/Enter/Space):

| Key | Action |
|---|---|
| **F6** | Add a sim controller (and select it) |
| **F7 / F8** | Select previous / next sim controller |
| **F9** | Remove the selected sim controller |
| **J / L** | Move the selected sim **left / right** (drives its left stick) |
| **K** | Press **A / ready** on the selected sim (its South button) |

A small overlay (top‑left) shows how many sims exist and which is selected.

## Typical 4‑player test

1. Start a **co‑op** run → the input‑assignment screen appears.
2. **F6** → sim #1 appears as a row. **J/L** to move it into a column, **K** to ready.
3. **F6** → sim #2 (auto‑selected) → move to the next column, **K** to ready.
4. Repeat for #3 / #4. (Use **F7/F8** to re‑select an earlier sim if you need to fix it.)
5. Once enough players are readied, the run starts — and each sim keeps driving its ship in‑game
   (one at a time, whichever is selected).

## Notes & caveats

- **One puppet at a time.** Only the *selected* sim mirrors your keyboard; the rest hold neutral.
  In‑game you can drive only the selected ship — fine for testing spawn/economy/UI, not for
  actually playing 4 ships at once.
- The sims are removed when the plugin unloads (game quit). **F9** removes one manually.
- Outside the join screen, **J/L** still moves the selected sim's stick, which a menu *might* read
  as gamepad navigation — only use the movement keys when you mean to.
- This pairs with `PunkFourPlayer`: the virtual pads satisfy its "spare gamepad" assignment, so you
  can validate multi‑ship spawning without physical controllers.
