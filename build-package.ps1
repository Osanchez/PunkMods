# build-package.ps1
# Builds every PUNK mod in this folder and packages the results for distribution.
#
#   powershell -ExecutionPolicy Bypass -File build-package.ps1            # single bundle (default, local use)
#   powershell -ExecutionPolicy Bypass -File build-package.ps1 -PerMod    # one zip per mod + BepInEx-Setup.zip
#
# Outputs go to Mods\dist\. No game files are ever included - only BepInEx (open source) + our mods.

param(
    # Root of the game install to build/reference against. Defaults to the folder above Mods\
    # (your local Steam install). CI passes the extracted reference-DLL stub here instead.
    [string]$GameDir,
    # CI mode: skip refreshing your local BepInEx\plugins install (there isn't one on a runner).
    [switch]$Ci,
    # Emit one zip per mod + a BepInEx-Setup.zip, instead of a single all-in-one bundle.
    [switch]$PerMod
)
$ErrorActionPreference = 'Stop'
$ModsDir = $PSScriptRoot
if (-not $GameDir) { $GameDir = Split-Path $ModsDir -Parent }
$DistDir = Join-Path $ModsDir 'dist'
$Stamp   = Get-Date -Format 'yyyyMMdd'

Write-Host "Game folder: $GameDir"
Write-Host "Mods folder: $ModsDir`n"

# 1) Build every mod project (skip bin/obj/dist)
$projects = Get-ChildItem $ModsDir -Recurse -Filter *.csproj |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|dist)\\' }
if (-not $projects) { throw "No .csproj projects found under $ModsDir" }

foreach ($p in $projects) {
    Write-Host "Building $($p.BaseName) ..." -ForegroundColor Cyan
    dotnet build $p.FullName -c Release -v quiet -p:GameDir="$GameDir" | Out-Null
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
