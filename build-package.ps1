# build-package.ps1
# Builds every PUNK mod in this folder and packages them WITH BepInEx into a single zip that a
# friend can extract straight into their game folder. Run from anywhere:
#     powershell -ExecutionPolicy Bypass -File build-package.ps1
#
# Output: Mods\dist\PUNK-Mods-<date>.zip
# (Includes BepInEx + all mod DLLs + INSTALL.md. Does NOT include any game files.)

param(
    # Root of the game install to build/reference against. Defaults to the folder above Mods\
    # (your local Steam install). CI passes the extracted reference-DLL stub here instead.
    [string]$GameDir,
    # CI mode: skip refreshing your local BepInEx\plugins install (there isn't one on a runner).
    [switch]$Ci
)
$ErrorActionPreference = 'Stop'
$ModsDir = $PSScriptRoot
if (-not $GameDir) { $GameDir = Split-Path $ModsDir -Parent }
$DistDir = Join-Path $ModsDir 'dist'
$Stage   = Join-Path $DistDir 'PUNK-Mods'
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

# 2) Stage a clean game-root layout
if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path "$Stage\BepInEx\plugins"  | Out-Null
New-Item -ItemType Directory -Force -Path "$Stage\BepInEx\patchers" | Out-Null

# 2a) BepInEx loader (copied from your working install) - the redistributable parts only
foreach ($f in @('winhttp.dll','doorstop_config.ini','.doorstop_version','changelog.txt')) {
    $src = Join-Path $GameDir $f
    if (Test-Path $src) { Copy-Item $src $Stage -Force }
}
$core = Join-Path $GameDir 'BepInEx\core'
if (-not (Test-Path $core)) { throw "BepInEx core not found at $core - is BepInEx installed in the game?" }
Copy-Item $core "$Stage\BepInEx\core" -Recurse -Force

# Ship BepInEx.cfg too, so the console window stays OFF for friends. The console is a separate
# window and Steam Remote Play (which streams the game window) can report "host is busy" when it's open.
$bcfg = Join-Path $GameDir 'BepInEx\config\BepInEx.cfg'
if (Test-Path $bcfg) {
    New-Item -ItemType Directory -Force -Path "$Stage\BepInEx\config" | Out-Null
    Copy-Item $bcfg "$Stage\BepInEx\config\BepInEx.cfg" -Force
}

# 2b) Our plugin DLLs - one folder per mod (BepInEx/plugins/<Mod>/<Mod>.dll). Its config + assets
#     live alongside the DLL in that folder.
$plugins = "$Stage\BepInEx\plugins"
foreach ($p in $projects) {
    $dll = Join-Path $p.Directory.FullName "bin\Release\$($p.BaseName).dll"
    if (Test-Path $dll) {
        $modDir = Join-Path $plugins $p.BaseName
        New-Item -ItemType Directory -Force -Path $modDir | Out-Null
        Copy-Item $dll $modDir -Force
        $yaml = Join-Path $p.Directory.FullName 'mod.yaml'
        if (Test-Path $yaml) { Copy-Item $yaml $modDir -Force }   # label metadata read by PunkModsMenu
        Write-Host "  + $($p.BaseName)\$($p.BaseName).dll" -ForegroundColor Green
    }
    else { Write-Warning "missing build output: $dll" }
}

# 2c) Install guide
Copy-Item (Join-Path $ModsDir 'INSTALL.md') $Stage -Force

# 2d) Refresh THIS install's plugins too (per-mod folders), unless the game is running and locks them.
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
        if (Test-Path $yaml) { Copy-Item $yaml $modDir -Force }   # label metadata read by PunkModsMenu
    }
    Write-Host "Local install refreshed (per-mod folders)." -ForegroundColor Green
}

# 3) Zip the staged CONTENTS (so extracting drops files at the game root). -Force includes dotfiles.
if (-not (Test-Path $DistDir)) { New-Item -ItemType Directory -Path $DistDir | Out-Null }
$zip = Join-Path $DistDir "PUNK-Mods-$Stamp.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
$items = Get-ChildItem -Path $Stage -Force | Select-Object -ExpandProperty FullName
Compress-Archive -Path $items -DestinationPath $zip -Force

$size = [math]::Round((Get-Item $zip).Length / 1MB, 2)
Write-Host "`nPackaged: $zip ($size MB)" -ForegroundColor Green
Write-Host "Friends: extract its contents into the PUNK Playtest folder (next to Punk.exe)." -ForegroundColor Green
