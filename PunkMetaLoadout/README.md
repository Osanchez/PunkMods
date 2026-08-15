# PUNK Meta Loadout (persistent build)

Roguelite meta-progression: your ship **build survives death**. The plugin snapshots your module
grid and the vault stash, then re-applies them when a new run spawns — so dying no longer wipes
your progress.

## What a save is keyed by

Three things decide which file your progress lands in. This is the part worth understanding, since
"my stuff vanished" is almost always a run that used a different key than you expected:

| Key | Meaning |
| --- | --- |
| **Profile** | A named person. Assigned per player slot on the ready-up / player-select screen. A slot set to **No Profile** saves nothing and always starts fresh. |
| **Class** | The starting loadout you picked for the run (e.g. `Starter_Worm_Drone`). Each class keeps its own build — your Worm Drone build and your Popper build are separate. |
| **Player slot** | P1–P4. Whoever holds a profile carries that profile's build, whatever seat they sit in. |

```
%USERPROFILE%\AppData\LocalLow\DefaultCompany\Punk\meta_loadouts\
    profiles.json                       profile list, slot assignments, keep mode, current class
    profiles\<Profile>\<Class>.json     that person's ship build for that class
    vault_<Class>.json                  the shared stash for that class (all profiles share it)
    *.bak                               one automatic backup per file, from when the game launched
```

To wipe everything, delete that folder — or use **Clear Profile** / **Delete Profile** in the Mods
menu. Files are Odin JSON (the game's own serializer), so they carry some `$type`/`$id` metadata but
are readable.

## When it saves

Continuously during a run, not just at the end:

- a module is installed or removed on the ship grid (`ModuleGrid` events)
- a module enters or leaves the vault (`Vault.Store` / `Vault.Remove`, Harmony-patched — neither
  raises an event of its own)
- ingredient / consumable amounts change (`Vault` events)
- **the game itself saves** — i.e. pause → Save & Exit (`GameSaver.Save`, Harmony-patched). This is
  the catch-all: station upgrades mutate a module in place without an install event, so this is what
  gets them to disk.
- game over (`GameController.GameOver`)

Writes go through a temp file and are renamed into place, so a crash or alt-F4 mid-write can't leave
a truncated save. The first write to each file in a session first copies the old contents to `.bak`.

**Not saved:** currency, station upgrade purchases, map progress, anything else in the game's
`RunData`. Those are per-run by design and belong to PUNK's own save system, not this mod.

## When it restores

Only at the start of a **new** run, before you take control: each player's assigned profile provides
their build for that class, and the shared vault for that class is restored once. A **continued**
run restores nothing — the game's own save already holds that run's real state; the plugin just
keeps its files in sync from there.

"Keep Across Runs" in the Mods menu chooses what carries over: **Ship + Vault** (default), **Ship**
only, or **Vault** only. The side you switch off is frozen, not cleared — switch back later and the
old values are still there.

## Recovering a save that looks wiped

`recover-default-save.ps1` (in this folder) inspects the save folder and, if asked, restores
stranded data. Run it with the game closed:

```powershell
powershell -File recover-default-save.ps1                                # report only
powershell -File recover-default-save.ps1 -To Starter_Worm_Drone -Apply  # restore into that class
```

It backs up anything it overwrites to `<file>.recovery-bak`.

### The bug it exists for (fixed in 2.1.0)

PUNK's **Continue** button goes straight to the game scene — it never passes through the loadout
selector, so `RunArguments.startingLoadout` is null on every continued run. Versions up to 2.0.0
read the class from that field and therefore keyed continued runs as `default`. Everything earned
after resuming a save went into `vault_default.json` / `profiles/<name>/default.json`, and the next
fresh run restored the older per-class file instead — indistinguishable from the save being wiped,
even though nothing was deleted.

2.1.0 stamps the class onto every save the game writes (`lastClassSolo` / `lastClassCoop` in
`profiles.json`) and reads it back when that save is continued, so a continued run lands in the same
files as the run that created it. It also no longer writes a snapshot at run start, where a restore
that found nothing would overwrite a good file with the fresh starting ship.

If `default` files already exist, the plugin logs a pointer to them once per session and the script
above will move them back.

## Build / install

```sh
cd "C:/Program Files (x86)/Steam/steamapps/common/PUNK Playtest/Mods/PunkMetaLoadout"
dotnet build -c Release
# then copy bin/Release/PunkMetaLoadout.dll into BepInEx/plugins/PunkMetaLoadout/
```

Or `powershell -File ../build-package.ps1` from the `Mods` folder to build and deploy every mod.

## Notes & caveats

- **Balance:** full build carryover is a deliberate snowball — the game gets easier each run. To make
  it partial, strip part of the memento before saving or narrow the save triggers.
- **Co-op:** the vault is shared per class across profiles; ship builds are per profile.
- **Resilience:** if a saved module ID no longer exists (game updated), the restore is caught and the
  run falls back to the starting loadout instead of crashing — see `BepInEx/LogOutput.log`.
- If the game changes `ModuleGrid.Memento`, `Vault.Memento`, `RunArguments`, or `GameSaver.Save`,
  this needs revisiting; regenerate the notes in `../docs/` to find the new signatures.
