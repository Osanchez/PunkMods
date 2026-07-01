# PUNK Meta Loadout (persistent build)

Roguelite meta-progression: your ship **build survives death**. The plugin saves your module
grid + vault stash to a JSON file and re-applies it whenever a new run spawns, so dying no
longer wipes your progress.

- **Scope:** installed ship build (module grid) **+** vault stash (spare modules, ingredients,
  consumables).
- **Reset rule:** never — the build persists across every death until you delete the file.

## File location

```
%USERPROFILE%\AppData\LocalLow\DefaultCompany\Punk\meta_loadout.json
```

To wipe progress and start fresh, delete that file (or empty it). It's written in Odin's JSON
format (the game's own serializer) — readable, though it carries some `$type`/`$id` metadata.

## How it works

It reuses the game's own snapshot types and serializer, so the data round-trips exactly like
the native save system:

- **Save** — on every upgrade and on death:
  - module installed/uninstalled on the ship grid (`ModuleGrid` events)
  - module picked up into the vault (`Vault.Store`, Harmony-patched)
  - ingredient/consumable amount changes (`Vault` events)
  - `GameController.GameOver` (captures the final build)
  - …each writes `{ grid: ModuleGrid.Memento, vault: Vault.Memento }` via
    `SerializationUtility.SerializeValue(data, DataFormat.JSON)`.
- **Restore** — Harmony postfix on `LoadoutTemplate.Apply(ModuleGridOwner.Data)`. Run start
  applies the starting loadout to the new ship; we then overwrite it with the saved build
  (`ModuleGrid.RestoreFromMemento`) and restore the run's `Vault` once. No save file (first
  run) → the postfix no-ops and the vanilla starting loadout stands.

Modules/ingredients/consumables are stored by **string ID** and looked up from the registries
on restore, so the file is robust as long as those IDs still exist in the build.

## Build / install

Same toolchain as the other plugin (BepInEx 6 Mono + .NET SDK):

```sh
cd "C:/Program Files (x86)/Steam/steamapps/common/PUNK Playtest/Mods/PunkMetaLoadout"
dotnet build -c Release
# then copy bin/Release/PunkMetaLoadout.dll into BepInEx/plugins/
```

## Notes & caveats

- **Balance:** full build carryover is a deliberate snowball — the game gets easier each run.
  That's the chosen behavior. To make it a partial/earned progression later, change the save
  trigger or strip part of the memento on death.
- **Co-op:** one shared build file; both ships get the same restored build. Fine for solo;
  rough for versus-style co-op.
- **Continued runs:** loading a suspended run keeps that run's real state; the plugin just
  keeps the meta file in sync with it (it never overwrites a loaded save — `Apply` only runs
  on brand-new runs).
- **Resilience:** if a saved ID no longer exists (game updated), the restore is caught and the
  run falls back to the starting loadout instead of crashing (see `BepInEx/LogOutput.log`).
- If the game changes `LoadoutTemplate.Apply`, `ModuleGrid.Memento`, or `Vault.Memento`, this
  may need updating; the docs in `../docs/` can be regenerated to find new signatures.
