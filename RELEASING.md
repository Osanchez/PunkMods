# Automated releases

Every push/merge to `main` builds all mods and publishes the packaged zip as a GitHub **Release**
(tagged `vYYYY.MM.DD.<run-number>`). See `.github/workflows/release.yml`.

The mods compile against proprietary game / Unity / BepInEx DLLs that are **not** in git. CI pulls
them from a private release asset called **`refs`**, created once with `tools/make-refs.ps1`.

> ⚠️ Keep this repo **private**. `punk-refs.zip` contains closed-playtest and Unity assemblies that
> may not be redistributed. It is git-ignored and only ever lives as a private release asset.

## One-time setup

1. **Create the private repo** (run from inside `Mods\`):
   ```pwsh
   git init -b main
   git add .
   git commit -m "PUNK mods + CI release pipeline"
   gh repo create <owner>/PUNK-Mods --private --source . --remote origin --push
   ```
   (Or create the private repo on github.com and `git remote add origin ... ; git push -u origin main`.)

2. **Build and upload the reference DLLs** the CI compiles against:
   ```pwsh
   powershell -ExecutionPolicy Bypass -File tools\make-refs.ps1
   gh release create refs punk-refs.zip --repo <owner>/PUNK-Mods --prerelease `
     --title "CI refs" --notes "Reference assemblies for CI - do not distribute"
   ```
   No `gh`? Create the release manually on github.com: tag it `refs`, mark it a pre-release, and
   attach `punk-refs.zip`.

   > The `refs` release must exist **before** the first `main` build, or the download step fails.
   > If the very first run failed for this reason, just re-run it from the Actions tab afterward.

That's it. From now on, every push to `main` produces a Release automatically. You can also trigger
one manually from the **Actions** tab ("Build & Release Mods" → *Run workflow*).

## When you add a NEW DLL reference to a mod

CI only has the DLLs captured in `refs`. If you add a `<Reference>` to a new game/Unity assembly,
refresh the bundle:
```pwsh
powershell -ExecutionPolicy Bypass -File tools\make-refs.ps1
gh release upload refs punk-refs.zip --repo <owner>/PUNK-Mods --clobber
```

## How it fits together

- `build-package.ps1 -GameDir <dir> -Ci` — the existing packager, now parameterized. `-GameDir`
  overrides MSBuild's `GameDir` (via a global `-p:` property, so no `.csproj` changes), and `-Ci`
  skips refreshing your local `BepInEx\plugins`. Run with no args, it behaves exactly as before.
- `tools/make-refs.ps1` — parses every `.csproj`, copies the referenced DLLs (plus the BepInEx
  loader + `BepInEx.cfg`) into `punk-refs.zip`, mirroring the game folder layout.
