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

## Pinned downloads and checksums

Every `mod.json` names the exact release asset PUNK Nexus should download, plus that file's
`sha256`. **CI does this for you** — the `Pin downloads and checksums` step runs after the release
is published, hashes the zips this run produced, writes them into the manifests, and commits back to
`main` with `[skip ci]`.

The point of the hash is to pin the identity of a *published artifact*: once a zip is on a release,
the manifest says "this exact file and no other", so a file swapped at the download URL afterwards
is refused rather than installed.

You only need the tool by hand to repair drift:

```bash
python3 tools/pin-downloads.py                          # newest release, via the API
python3 tools/pin-downloads.py v2026.08.09.16           # a specific release
python3 tools/pin-downloads.py --dist dist --tag v1.2.3 # hash local zips (what CI runs)
python3 tools/pin-downloads.py --check                  # verify only, no writes
```

**Why the url is pinned as well as hashed.** A manifest may name its download as a fixed `url` or as
`repo` + `assetPattern` resolved against the newest release. The second form cannot carry a
checksum, because what it points at is allowed to change underneath it — and here it always does:
every push rebuilds every zip, and the zips are not reproducible (`Compress-Archive` stamps
timestamps). Confirmed by pulling `BepInEx-Setup.zip` from two consecutive releases whose content
had not changed:

```
v2026.08.08.14  76a3070fb99fe762581f9c92b081682df4a979e4644ccce0a81c4143c76f656a
v2026.08.09.16  5f530a14d08301b18f1b6784a1e0d4c953c069e9b0e1db346471cd2ef96ed040
```

So "latest" plus a hash is a contradiction: the hash names one build while the pointer follows
another. The client **blocks an install on a mismatch**, so that contradiction would turn working
mods into uninstallable ones. Pinning both together removes it.

**There is no broken window during a build.** Until the pinning step lands, the manifests still name
the *previous* release and its hash — an immutable asset GitHub keeps forever — so the catalog is
always self-consistent. It just lags by the length of one CI job.

**The `[skip ci]` marker is load-bearing.** That commit goes to `main`, and pushes to `main` are what
trigger this workflow; without it the release loop never terminates.
