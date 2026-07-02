# Automated releases

Every push/merge to `main` builds all mods and publishes a GitHub **Release** (tagged
`vYYYY.MM.DD.<run-number>`) with **one zip per mod** plus a one-time `BepInEx-Setup.zip`.
See `.github/workflows/release.yml`.

## Repository layout

- **`Osanchez/PunkMods`** (this repo) — source, workflow, and public releases. Safe to be public:
  git history contains no binaries, and the released zips contain only BepInEx (open source) + our
  own mod DLLs. **No proprietary game/Unity DLLs are ever published.**
- **`Osanchez/PunkMods-refs`** (private) — holds `punk-refs.zip`, the proprietary game / Unity /
  BepInEx assemblies the mods *compile* against. CI downloads it using the `REFS_TOKEN` secret.

## What friends download

- `BepInEx-Setup.zip` — extract into the game folder **once** (installs the BepInEx loader).
- `<Mod>.zip` — extract into the game folder for each mod you want (drops
  `BepInEx/plugins/<Mod>/`). Update a mod by re-extracting its zip.

## One-time setup

1. **Private refs repo + bundle** (done): `Osanchez/PunkMods-refs` exists and its `refs` release holds
   `punk-refs.zip`, produced by `tools/make-refs.ps1`.
2. **`REFS_TOKEN` secret** — create a **fine-grained PAT** (github.com → Settings → Developer settings
   → Fine-grained tokens) with **Only select repositories → PunkMods-refs** and **Repository
   permissions → Contents: Read-only**. Then add it to this repo:
   ```bash
   gh secret set REFS_TOKEN --repo Osanchez/PunkMods --body '<paste-PAT>'
   ```

## Making this repo public (optional)

Do these in order so nothing breaks:
1. Confirm the `REFS_TOKEN` secret is set (above) and a build has succeeded with it.
2. Delete the now-unused old refs asset from *this* repo:
   ```bash
   gh release delete refs --repo Osanchez/PunkMods --cleanup-tag --yes
   ```
3. Flip visibility:
   ```bash
   gh repo edit Osanchez/PunkMods --visibility public --accept-visibility-change-consequences
   ```
   (Fork PRs won't have `REFS_TOKEN`, so their builds skip — only your pushes to `main` release.)

## After a game update (keep the reference DLLs fresh)

The game can update and shift the assemblies the mods compile against. Refresh in one step:
```pwsh
powershell -ExecutionPolicy Bypass -File tools\update-refs.ps1
```
This rebuilds `punk-refs.zip` from your current install and re-uploads it to the `refs` release in
`PunkMods-refs` (falls back to `wsl gh` if `gh` isn't on Windows PATH). The next push to `main`
builds against the refreshed DLLs. Run it too whenever you add a new `<Reference>` to a mod.

It also refreshes **`game-version.json`** (via `tools/get-game-version.ps1`) with the installed
game's Unity version + Steam build id. **Commit that file** — CI reads it to stamp each Release
description with a "Built for PUNK Playtest ..." blurb (the runner has no real game install, so the
version must be captured here). To refresh it on its own: `powershell -File tools\get-game-version.ps1`.

## How it fits together

- `build-package.ps1` — no args: single all-in-one bundle (handy for local use). `-PerMod`: one zip
  per mod + `BepInEx-Setup.zip` (what CI uses). `-GameDir` overrides the build/reference root;
  `-Ci` skips refreshing your local `BepInEx\plugins`.
- `tools/make-refs.ps1` — builds `punk-refs.zip` from your install (parses each `.csproj`).
- `tools/get-game-version.ps1` — reads the installed game's Unity version (from
  `Punk_Data\globalgamemanagers`) + Steam build id (from the Steam `appmanifest`) into the tracked
  `game-version.json`, which CI turns into the Release-description blurb.
- `tools/update-refs.ps1` — make-refs + get-game-version + upload to the private refs release.
