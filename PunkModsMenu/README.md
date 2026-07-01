# PUNK Mods Menu

Injects a native **MODS** tab into the vanilla Settings/Options screen. Each mod can have its
own section; the first one is **Meta Loadout → KEEP / CLEAR PROGRESS**, with a confirmation
dialog.

## How it works

The plugin Harmony-postfixes `OptionsScreen.Awake` and builds the tab from the game's own
prefabs (no hand-built UI):

1. **Tab content** — clones `tabs[0]` (the Gameplay tab GameObject, which carries the
   `VerticalLayoutGroup` + `AnimatedScreen`), removes the cloned `OptionsTab` component, strips
   its items down to one 2-button menu-item template, relabels it **META LOADOUT** with buttons
   **KEEP** / **CLEAR PROGRESS**, and adds our own `ModsOptionsTab : OptionsTab`.
2. **Tab button** — clones the last tab button (e.g. `AudioButton`), relabels it **MODS**, and
   rewires its click to `OptionsScreen.ShowTab(3)`.
3. **Commit** — extends `OptionsScreen`'s private `tabs[]` and `tabButtonImages[]` arrays by one.
   Tab cycling (next/prev tab) and the tab button both reach the new tab.

Selecting **CLEAR PROGRESS** opens the game's own `Prompt` (`Open(yes, no)`); confirming calls
`PunkMetaLoadout.MetaLoadout.ClearProgress()` (via reflection, so the two plugins stay
decoupled), which deletes `meta_loadout.json` and suppresses re-saving until the next run.

The whole injection is wrapped in try/catch: **any failure leaves the vanilla 3-tab screen
untouched** (just no MODS tab) instead of breaking the menu.

## Adding more mods to the tab

`ModsActions` is where per-mod rows are wired. To add another mod's section, clone another
menu-item under the MODS tab and hook its `SelectionChanged` the same way. (A small
registration API — `ModMenu.AddSection(name, onAction)` — is the natural next step if more mods
need entries.)

## Build / install

```sh
cd "C:/Program Files (x86)/Steam/steamapps/common/PUNK Playtest/Mods/PunkModsMenu"
dotnet build -c Release
# copy bin/Release/PunkModsMenu.dll into BepInEx/plugins/
```

## Notes & caveats

- **Fragile by nature.** It depends on the live Settings hierarchy (tab/item/button names and
  structure). If the game updates that screen, re-run the diagnostic dump and adjust. The dump
  code lives in git history of this file (v0.1.0-diagnostic).
- **Confirmation dialog** is the most likely thing to need a tweak — the reused `Prompt` may
  need sort-order/parenting adjustment to sit above the options screen. If no `Prompt` is found,
  CLEAR acts directly (still a deliberate selection).
- **Best cleared from the main menu** between runs. Mid-run it still works (re-saving is
  suppressed until the next run), but resetting between runs is the intended flow.
