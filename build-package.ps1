# build-package.ps1
# Builds every PUNK mod in this folder and packages the results for distribution.
#
#   powershell -File build-package.ps1                 # single bundle (default, local use)
#   powershell -File build-package.ps1 -PerMod         # one zip per mod + BepInEx-Setup.zip (CI mode)
#   powershell -File build-package.ps1 -Debug          # LOCAL DEV: Debug build, deploy dll+pdb to plugins\, no zip
#   powershell -File build-package.ps1 -HotReload Mod  # LOCAL DEV: Debug-build one mod into BepInEx\scripts\ (PunkDevReload F10)
#
# Outputs go to Mods\dist\. No game files are ever included - only BepInEx (open source) + our mods.
# DISTRIBUTION IS ALWAYS RELEASE AND NEVER INCLUDES .pdb: -Debug/-HotReload are local-only and skip packaging.

param(
    # Root of the game install to build/reference against. Defaults to the folder above Mods\
    # (your local Steam install). CI passes the extracted reference-DLL stub here instead.
    [string]$GameDir,
    # CI mode: skip refreshing your local BepInEx\plugins install (there isn't one on a runner).
    [switch]$Ci,
    # Emit one zip per mod + a BepInEx-Setup.zip, instead of a single all-in-one bundle.
    [switch]$PerMod,
    # LOCAL DEV: build with symbols (Debug config) and deploy each mod's dll + .pdb to your local
    # BepInEx\plugins\ so a managed debugger (dnSpyEx / Rider) can step through source. No dist zip.
    [switch]$Debug,
    # LOCAL DEV: Debug-build the named mod (e.g. -HotReload PunkFourPlayer) into BepInEx\scripts\ for
    # live reload via ScriptEngine (press F6 in-game). Also removes it from plugins\ to avoid a double-load.
    [string]$HotReload
)
$ErrorActionPreference = 'Stop'
$ModsDir = $PSScriptRoot
if (-not $GameDir) { $GameDir = Split-Path $ModsDir -Parent }
$DistDir = Join-Path $ModsDir 'dist'
$Stamp   = Get-Date -Format 'yyyyMMdd'
# Dev builds (-Debug / -HotReload) compile unoptimized with symbols; distribution stays Release.
$Config  = if ($Debug -or $HotReload) { 'Debug' } else { 'Release' }
# Dev-only mods: built + deployed to the local install, but NEVER packaged into a distribution zip.
$DevOnly = @('PunkDevReload')

Write-Host "Game folder: $GameDir"
Write-Host "Mods folder: $ModsDir`n"

# 1) Build every mod project (skip bin/obj/dist). -HotReload builds just the one named mod.
$projects = Get-ChildItem $ModsDir -Recurse -Filter *.csproj |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|dist)\\' }
if (-not $projects) { throw "No .csproj projects found under $ModsDir" }

$buildSet = $projects
if ($HotReload) {
    $buildSet = $projects | Where-Object { $_.BaseName -eq $HotReload }
    if (-not $buildSet) { throw "HotReload mod '$HotReload' not found. Available: $(($projects | ForEach-Object BaseName) -join ', ')" }
}

foreach ($p in $buildSet) {
    Write-Host "Building $($p.BaseName) ($Config) ..." -ForegroundColor Cyan
    dotnet build $p.FullName -c $Config -v quiet -p:GameDir="$GameDir" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $($p.Name)" }
}

# Copy the BepInEx loader (redistributable parts only) into a staged game-root folder.
function Copy-Loader($dest) {
    foreach ($f in @('winhttp.dll','doorstop_config.ini','.doorstop_version','changelog.txt')) {
        $src = Join-Path $GameDir $f
        if (Test-Path $src) { Copy-Item $src $dest -Force }
    }
    $core = Join-Path $GameDir 'BepInEx\core'
    if (-not (Test-Path $core)) { throw "BepInEx core not found at $core - is BepInEx installed in the game?" }
    # Refuse to package a loader that can never start: Doorstop's target_assembly is the Mono
    # preloader, which a compile-refs-only stub (stale punk-refs.zip) doesn't contain. Shipping
    # without it produced a silent no-op loader for every downloader.
    if (-not (Test-Path (Join-Path $core 'BepInEx.Unity.Mono.Preloader.dll'))) {
        throw "BepInEx core at $core is missing BepInEx.Unity.Mono.Preloader.dll (partial install or stale refs stub) - refusing to package a broken loader. Re-run tools\update-refs.ps1 from a full install."
    }
    # Copy the source 'core' folder to <dest>\BepInEx\core. The target must NOT pre-exist, or Copy-Item
    # would nest it as core\core. Callers create <dest>\BepInEx\plugins first, so BepInEx\ already exists.
    Copy-Item $core "$dest\BepInEx\core" -Recurse -Force
    # Ship BepInEx.cfg so the console window stays OFF (Steam Remote Play can report "host is busy" otherwise).
    $bcfg = Join-Path $GameDir 'BepInEx\config\BepInEx.cfg'
    if (Test-Path $bcfg) {
        New-Item -ItemType Directory -Force -Path "$dest\BepInEx\config" | Out-Null
        Copy-Item $bcfg "$dest\BepInEx\config\BepInEx.cfg" -Force
    }
}

# Stage one mod's plugin folder (DLL + mod.yaml) under $dest\BepInEx\plugins\<Mod>\. Returns $true on success.
function Copy-Plugin($project, $dest) {
    $dll = Join-Path $project.Directory.FullName "bin\Release\$($project.BaseName).dll"
    if (-not (Test-Path $dll)) { Write-Warning "missing build output: $dll"; return $false }
    $modDir = Join-Path $dest "BepInEx\plugins\$($project.BaseName)"
    New-Item -ItemType Directory -Force -Path $modDir | Out-Null
    Copy-Item $dll $modDir -Force
    $yaml = Join-Path $project.Directory.FullName 'mod.yaml'
    if (Test-Path $yaml) { Copy-Item $yaml $modDir -Force }   # label metadata read by PunkModsMenu
    return $true
}

# Deploy one mod's build output (dll, optional pdb, mod.yaml) into a live BepInEx\plugins\<Mod>\ folder.
function Deploy-LocalPlugin($project, $config, $withPdb) {
    $livePlugins = Join-Path $GameDir 'BepInEx\plugins'
    if (-not (Test-Path $livePlugins)) { return $false }
    Remove-Item (Join-Path $livePlugins "$($project.BaseName).dll") -Force -ErrorAction SilentlyContinue  # drop old flat copy
    $modDir = Join-Path $livePlugins $project.BaseName
    New-Item -ItemType Directory -Force -Path $modDir | Out-Null
    $bin = Join-Path $project.Directory.FullName "bin\$config"
    Copy-Item (Join-Path $bin "$($project.BaseName).dll") $modDir -Force
    if ($withPdb) {
        $pdb = Join-Path $bin "$($project.BaseName).pdb"
        if (Test-Path $pdb) { Copy-Item $pdb $modDir -Force }
    }
    $yaml = Join-Path $project.Directory.FullName 'mod.yaml'
    if (Test-Path $yaml) { Copy-Item $yaml $modDir -Force }
    return $true
}

# --- LOCAL DEV: hot-reload one mod into BepInEx\scripts\ (ScriptEngine reloads it on F6). No dist. ---
if ($HotReload) {
    $proj = $buildSet | Select-Object -First 1
    $scripts = Join-Path $GameDir 'BepInEx\scripts'
    New-Item -ItemType Directory -Force -Path $scripts | Out-Null
    $bin = Join-Path $proj.Directory.FullName "bin\$Config"
    Copy-Item (Join-Path $bin "$($proj.BaseName).dll") $scripts -Force
    $pdb = Join-Path $bin "$($proj.BaseName).pdb"
    if (Test-Path $pdb) { Copy-Item $pdb $scripts -Force }
    # Avoid a double-load: the same plugin left in plugins\ would also load at startup.
    Remove-Item (Join-Path $GameDir "BepInEx\plugins\$($proj.BaseName)") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $GameDir "BepInEx\plugins\$($proj.BaseName).dll") -Force -ErrorAction SilentlyContinue
    Write-Host "`nHot-reload staged: $($proj.BaseName) -> BepInEx\scripts\ (removed from plugins\)." -ForegroundColor Green
    Write-Host "In-game: press the PunkDevReload reload key (default F10) after each rebuild." -ForegroundColor Green
    Write-Host "Restore it to a normal plugin later by re-running build-package.ps1 without -HotReload." -ForegroundColor DarkGray
    return
}

# --- LOCAL DEV: Debug build with symbols deployed to plugins\ for a step debugger. No dist. ---
if ($Debug) {
    if ($Ci) { Write-Warning "CI mode - -Debug does nothing on a runner."; return }
    if (Get-Process Punk -ErrorAction SilentlyContinue) {
        Write-Warning "Game is running - close it and re-run to refresh the local install with symbols."
        return
    }
    $n = 0
    foreach ($p in $projects) { if (Deploy-LocalPlugin $p 'Debug' $true) { $n++ } }
    Write-Host "`nDeployed $n mod(s) to BepInEx\plugins\ as Debug builds with .pdb symbols." -ForegroundColor Green
    Write-Host "Attach dnSpyEx / Rider to Punk.exe to step through mod source. (Distribution is unaffected.)" -ForegroundColor Green
    return
}

# 2) Refresh THIS local install's plugins (per-mod folders), unless CI or the game is running.
$livePlugins = Join-Path $GameDir 'BepInEx\plugins'
if ($Ci) {
    Write-Host "CI mode - skipped refreshing local install." -ForegroundColor DarkGray
}
elseif (Get-Process Punk -ErrorAction SilentlyContinue) {
    Write-Warning "Game is running - skipped updating your own install (close it and re-run to refresh locally)."
}
elseif (Test-Path $livePlugins) {
    foreach ($p in $projects) {
        Remove-Item (Join-Path $livePlugins "$($p.BaseName).dll") -Force -ErrorAction SilentlyContinue  # drop old flat copy
        $modDir = Join-Path $livePlugins $p.BaseName
        New-Item -ItemType Directory -Force -Path $modDir | Out-Null
        Copy-Item (Join-Path $p.Directory.FullName "bin\Release\$($p.BaseName).dll") $modDir -Force
        $yaml = Join-Path $p.Directory.FullName 'mod.yaml'
        if (Test-Path $yaml) { Copy-Item $yaml $modDir -Force }
    }
    Write-Host "Local install refreshed (per-mod folders)." -ForegroundColor Green
}

# 3) Package
if (-not (Test-Path $DistDir)) { New-Item -ItemType Directory -Path $DistDir | Out-Null }

if ($PerMod) {
    # ---- Per-mod distribution: one zip per mod + a one-time BepInEx-Setup.zip ----
    Get-ChildItem $DistDir -Filter *.zip -ErrorAction SilentlyContinue | Remove-Item -Force
    $work = Join-Path $DistDir '_stage'
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }

    # BepInEx-Setup.zip: the loader only (install once). No plugins.
    $setup = Join-Path $work 'setup'
    New-Item -ItemType Directory -Force -Path "$setup\BepInEx\plugins" | Out-Null
    Copy-Loader $setup
    Copy-Item (Join-Path $ModsDir 'INSTALL.md') $setup -Force
    $setupZip = Join-Path $DistDir 'BepInEx-Setup.zip'
    Compress-Archive -Path (Get-ChildItem $setup -Force | Select-Object -ExpandProperty FullName) -DestinationPath $setupZip -Force
    Write-Host "  + BepInEx-Setup.zip" -ForegroundColor Green

    # One zip per mod: just BepInEx\plugins\<Mod>\.
    foreach ($p in $projects) {
        if ($DevOnly -contains $p.BaseName) { continue }   # dev-only tools are never distributed
        $modStage = Join-Path $work $p.BaseName
        if (-not (Copy-Plugin $p $modStage)) { continue }
        $modZip = Join-Path $DistDir "$($p.BaseName).zip"
        # Zip the BepInEx folder so extraction lands at <game>\BepInEx\plugins\<Mod>\.
        Compress-Archive -Path (Join-Path $modStage 'BepInEx') -DestinationPath $modZip -Force
        Write-Host "  + $($p.BaseName).zip" -ForegroundColor Green
    }

    Remove-Item $work -Recurse -Force
    $zips = Get-ChildItem $DistDir -Filter *.zip
    Write-Host "`nPackaged $($zips.Count) zips into $DistDir" -ForegroundColor Green
    Write-Host "Friends: extract BepInEx-Setup.zip once, then each mod zip you want - all into the PUNK Playtest folder." -ForegroundColor Green
}
else {
    # ---- Single bundle (default, for handing a friend a full ready-to-play install) ----
    $Stage = Join-Path $DistDir 'PUNK-Mods'
    if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path "$Stage\BepInEx\plugins" | Out-Null
    Copy-Loader $Stage
    foreach ($p in $projects) {
        if ($DevOnly -contains $p.BaseName) { continue }   # dev-only tools are never distributed
        if (Copy-Plugin $p $Stage) { Write-Host "  + $($p.BaseName)\$($p.BaseName).dll" -ForegroundColor Green }
    }
    Copy-Item (Join-Path $ModsDir 'INSTALL.md') $Stage -Force
    $zip = Join-Path $DistDir "PUNK-Mods-$Stamp.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Get-ChildItem $Stage -Force | Select-Object -ExpandProperty FullName) -DestinationPath $zip -Force
    $size = [math]::Round((Get-Item $zip).Length / 1MB, 2)
    Write-Host "`nPackaged: $zip ($size MB)" -ForegroundColor Green
    Write-Host "Friends: extract its contents into the PUNK Playtest folder (next to Punk.exe)." -ForegroundColor Green
}
