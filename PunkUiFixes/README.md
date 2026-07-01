# PUNK UI Fixes

Small vanilla UI alignment fixes. Standalone plugin (`PunkUiFixes.dll`); delete it to revert.

## Fix #1 — co-op "ASSIGN INPUT" header on ultrawide

On the co-op controller-assignment screen, the device-rows container is anchored to the
**Window center**, but the header (`Players`: the `P1 / ASSIGN INPUT / P2` labels + underline) is
anchored to the Window's **top-left** with a fixed pixel offset (`anchoredPosition.x = 596`).

- At **16:9** both resolve to the same x — looks fine.
- At **wider** aspect ratios the rows follow screen center while the header stays left, so the
  keyboard/controller icons appear shoved right of their P1/P2 labels (the P2 icon even spills
  past the underline).

The fix re-anchors the header to be horizontally centered (`anchorMin.x = anchorMax.x = 0.5`,
`anchoredPosition.x = 0`) so the header and the rows share the screen center on any aspect ratio.
It's a **no-op at 16:9** and only changes ultrawide/odd aspects. Applied via a Harmony postfix on
`InputSelectorScreen.OnEnable`, wrapped so a failure leaves the vanilla layout untouched.
