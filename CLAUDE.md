# CLAUDE.md

Guidance for AI agents working in this repo. Human-facing docs: `README.md` (overview, install) and
`RELEASING.md` (CI/release details).

## What this is

BepInEx / Harmony mods for the Unity 6 (Mono) game **PUNK** (Steam Playtest). The repo root is the
`Mods/` folder inside the game install. Each subfolder is one mod — a `netstandard2.1` project.

## Build

```
powershell -File build-package.ps1          # build all + refresh local install + single bundle
powershell -File build-package.ps1 -PerMod  # one zip per mod + BepInEx-Setup.zip (CI mode)
```

The machine's `CurrentUser` execution policy is `RemoteSigned`, so this locally-authored script runs
without an `-ExecutionPolicy Bypass` flag. Do **not** add that flag — it weakens a security control
and Claude Code's auto-mode classifier hard-denies it.

- Mods reference proprietary game/Unity/BepInEx DLLs via `HintPath`s under `$(ManagedDir)` /
  `$(BepInExCore)`, both derived from the **`GameDir`** MSBuild property. `GameDir` defaults to the
  game install; override on the command line with `-p:GameDir=...` (CI points it at an extracted stub).
- There is no `Directory.Build.props`; each `.csproj` sets its own references.

## Releases & CI (details in RELEASING.md)

- Public repo `Osanchez/PunkMods`. Push/merge to `main` → GitHub Actions builds all mods → a per-mod
  Release tagged `vYYYY.MM.DD.<run#>`.
- The proprietary reference DLLs are **never committed**. They live in the **private** repo
  `Osanchez/PunkMods-refs` (`refs` release → `punk-refs.zip`), pulled in CI via the `REFS_TOKEN` secret.
- After a game update, or after adding a new DLL `<Reference>` to a mod, run `tools/update-refs.ps1`
  to rebuild and re-upload the reference bundle. That also refreshes `game-version.json` (game
  version + Steam build id, stamped into the Release description by CI) — **commit it**.

## Gotchas

- **Never commit** `*.dll`, `*.zip`, or anything under `dist/`, `bin/`, `obj/`, or `gamedir/` — it
  would leak proprietary binaries into a public repo. `.gitignore` covers these; keep it that way.
- The `gh` CLI on this machine is installed and authenticated **only inside WSL**, not on Windows.
  From the Windows side, call it as `wsl gh ...`. For commands with spaced paths (the game dir contains
  `(x86)` and spaces), write a `.sh` file and run `wsl bash <script>` — inline PowerShell→wsl→bash
  quoting mangles those paths.
- Each mod folder has a `mod.yaml` (name/author/version/description) read at runtime by the Mods Menu
  for its section header — edit it to relabel a mod without recompiling.
