# tools/get-game-version.ps1
# Captures the installed PUNK game version + Steam build id and writes them to a small tracked
# JSON file (Mods\game-version.json). CI reads that file to stamp the GitHub Release description
# with "Built for PUNK Playtest ...", because the CI runner does NOT have the real game installed
# (it only has the extracted reference-DLL stub). So the version must be captured HERE, on a machine
# that has the game, and committed to the repo.
#
#   powershell -File tools\get-game-version.ps1
#
# The output file holds only a version string + Steam build id (no proprietary content), so it is
# safe to commit to the public repo. Re-run this (or tools\update-refs.ps1, which calls it) after a
# game update, then commit the refreshed game-version.json.
param(
    # Root of your local game install (folder that contains Punk.exe / Punk_Data). Defaults to the
    # folder above Mods\ (your local Steam install).
    [string]$GameDir,
    # Where to write the captured info. Defaults to Mods\game-version.json.
    [string]$OutFile
)
$ErrorActionPreference = 'Stop'
$ModsDir = Split-Path $PSScriptRoot -Parent            # repo root = Mods\
if (-not $GameDir) { $GameDir = Split-Path $ModsDir -Parent }
if (-not $OutFile) { $OutFile = Join-Path $ModsDir 'game-version.json' }

# --- 1) Unity product version (Application.version / bundleVersion) -------------------------------
# Unity bakes PlayerSettings into Punk_Data\globalgamemanagers. Strings there are length-prefixed
# (int32 LE) and 4-byte aligned. We walk the serialized strings near the start of the file and pick
# the bundleVersion: the dotted-numeric string with the most components (e.g. "0.12.10" wins over the
# default per-platform build numbers like "1.0"). The engine version ("6000.3.4f1") is skipped
# because it is not purely numeric-and-dots.
$ggm = Join-Path $GameDir 'Punk_Data\globalgamemanagers'
$version = $null
if (Test-Path $ggm) {
    $bytes = [System.IO.File]::ReadAllBytes($ggm)
    $scanEnd = [Math]::Min($bytes.Length, 262144)      # PlayerSettings lives near the top; cap the scan
    $best = $null; $bestParts = -1; $bestPos = -1
    $i = 0
    while ($i -lt $scanEnd - 4) {
        $len = [BitConverter]::ToInt32($bytes, $i)
        if ($len -ge 1 -and $len -le 64 -and ($i + 4 + $len) -le $bytes.Length) {
            $printable = $true
            for ($j = 0; $j -lt $len; $j++) { $c = $bytes[$i + 4 + $j]; if ($c -lt 32 -or $c -gt 126) { $printable = $false; break } }
            if ($printable) {
                $s = [System.Text.Encoding]::ASCII.GetString($bytes, $i + 4, $len)
                if ($s -match '^[0-9]+(\.[0-9]+)+$') {
                    $parts = ($s -split '\.').Count
                    if ($parts -gt $bestParts -or ($parts -eq $bestParts -and $i -gt $bestPos)) {
                        $best = $s; $bestParts = $parts; $bestPos = $i
                    }
                }
                $adv = 4 + $len; $pad = (4 - ($adv % 4)) % 4; $i += $adv + $pad; continue
            }
        }
        $i++
    }
    $version = $best
}
if (-not $version) { Write-Warning "Could not read Unity bundleVersion from $ggm" }

# --- 2) Steam build id (from the app manifest a few dirs up from the install) ---------------------
# steamapps\appmanifest_<appid>.acf carries the immutable buildid + LastUpdated for this depot.
# Match by installdir (the leaf folder name of the game install) so we don't hardcode the appid.
$installLeaf = Split-Path $GameDir -Leaf
$steamApps   = Split-Path (Split-Path $GameDir -Parent) -Parent   # ...\common\<game> -> ...\steamapps
$steamAppId = $null; $steamBuildId = $null; $steamName = $null; $steamLastUpdatedUtc = $null
if (Test-Path $steamApps) {
    foreach ($acf in Get-ChildItem $steamApps -Filter 'appmanifest_*.acf' -ErrorAction SilentlyContinue) {
        $txt = Get-Content $acf.FullName -Raw
        $mDir = [regex]::Match($txt, '"installdir"\s*"([^"]*)"')
        if ($mDir.Success -and $mDir.Groups[1].Value -eq $installLeaf) {
            $mId    = [regex]::Match($txt, '"appid"\s*"([^"]*)"')
            $mBuild = [regex]::Match($txt, '"buildid"\s*"([^"]*)"')
            $mName  = [regex]::Match($txt, '"name"\s*"([^"]*)"')
            $mUpd   = [regex]::Match($txt, '"LastUpdated"\s*"([0-9]+)"')
            if ($mId.Success)    { $steamAppId   = $mId.Groups[1].Value }
            if ($mBuild.Success) { $steamBuildId = $mBuild.Groups[1].Value }
            if ($mName.Success)  { $steamName    = $mName.Groups[1].Value }
            if ($mUpd.Success)   {
                $epoch = [long]$mUpd.Groups[1].Value
                $steamLastUpdatedUtc = [DateTimeOffset]::FromUnixTimeSeconds($epoch).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ')
            }
            break
        }
    }
}
if (-not $steamBuildId) { Write-Warning "Could not find Steam buildid for installdir '$installLeaf' under $steamApps" }

# --- 3) Write the tracked JSON -------------------------------------------------------------------
$data = [ordered]@{
    gameName            = if ($steamName) { $steamName } else { $installLeaf }
    version             = $version
    steamAppId          = $steamAppId
    steamBuildId        = $steamBuildId
    steamLastUpdatedUtc = $steamLastUpdatedUtc
    capturedUtc         = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
}
$json = $data | ConvertTo-Json
# ConvertTo-Json emits UTF-16 via Set-Content by default; force UTF-8 (no BOM) for a clean commit.
[System.IO.File]::WriteAllText($OutFile, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Wrote $OutFile" -ForegroundColor Green
Write-Host ("  gameName     : {0}" -f $data.gameName)
Write-Host ("  version      : {0}" -f $data.version)
Write-Host ("  steamAppId   : {0}" -f $data.steamAppId)
Write-Host ("  steamBuildId : {0}" -f $data.steamBuildId)
