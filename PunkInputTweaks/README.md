# PUNK Input Tweaks

Reduces **gamepad input latency**. Standalone plugin (`PunkInputTweaks.dll`).

## What & why

The game never raises the Input System's polling rate, so gamepads are sampled at Unity's
**default 60 Hz** (~16 ms of latency before the game even sees a change). Keyboard/mouse are
event-driven, so they don't have this delay. Result: the controller player feels laggier than
the KB/M player.

This sets `InputSystem.pollingFrequency` to **250 Hz** (~4 ms) — configurable in
`BepInEx/config/com.osanchez.punk.inputtweaks.cfg` (`GamepadPollingHz`, 60–1000).

## Remote Play note

When a Remote Play friend uses a controller, Steam injects their input as a **virtual gamepad on
the host**, which Unity polls at this same rate. So raising the rate also trims the host-side
sampling delay for remote controllers. It does **not** affect the network/stream round-trip —
that latency is inherent to Remote Play — but it removes one real component.

## Notes

- Applied at startup and re-applied on each scene load (in case anything resets it).
- No Harmony patching; it just sets a global Input System property.
- Affects all gamepads (local and Remote Play virtual), not keyboard/mouse.
