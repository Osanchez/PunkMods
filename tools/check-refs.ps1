# tools/check-refs.ps1
# Pre-push guard: make sure the CI reference bundle (punk-refs.zip, uploaded to the private
# Osanchez/PunkMods-refs 'refs' release) is CURRENT, so a push to main doesn't fail CI on a
# missing/stale reference DLL. Runs automatically via .githooks/pre-push; also runnable by hand.
#
#   powershell -File tools\check-refs.ps1
#
# Two checks, BOTH fixed the same way:  powershell -File tools\update-refs.ps1  (then commit game-version.json)
#   1. Every DLL a .csproj <Reference> points at is present inside punk-refs.zip
#      -> catches adding a NEW reference (e.g. ProCamera2D.Runtime.dll) without refreshing refs.
#   2. The installed Steam build id matches game-version.json
#      -> catches a GAME UPDATE that changed the reference DLLs.
#
# Exit code 0 = safe to push, 1 = blocked. Emergency bypass at the git level: git push --no-verify
$ErrorActionPreference = 'Stop'
$Tools   = $PSScriptRoot
$ModsDir = Split-Path $Tools -Parent
$GameDir = Split-Path $ModsDir -Parent

$fail = $false; $warn = $false
function Block($m) { Write-Host "  [BLOCK] $m" -ForegroundColor Red;    $script:fail = $true }
function Warn($m)  { Write-Host "  [warn]  $m" -ForegroundColor Yellow; $script:warn = $true }
function Ok($m)    { Write-Host "  [ok]    $m" -ForegroundColor Green }

Write-Host "Checking CI reference bundle is current (pre-push)..." -ForegroundColor Cyan

# ---- Check 1: every referenced DLL exists inside punk-refs.zip ----------------------------------
$zip = Join-Path $ModsDir 'punk-refs.zip'
if (-not (Test-Path $zip)) {
    Warn "punk-refs.zip not found - cannot verify references. Run tools\update-refs.ps1 to build+upload it."
}
else {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $managedInZip = @{}; $coreInZip = @{}
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($e in $archive.Entries) {
            $n = ($e.FullName -replace '\\', '/')
            if ($n -match '(?i)Punk_Data/Managed/([^/]+\.dll)$') { $managedInZip[$Matches[1].ToLower()] = $true }
            elseif ($n -match '(?i)BepInEx/core/([^/]+\.dll)$')  { $coreInZip[$Matches[1].ToLower()] = $true }
        }
    } finally { $archive.Dispose() }

    $projects = Get-ChildItem $ModsDir -Recurse -Filter *.csproj |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|dist|dist-refs)\\' }
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($p in $projects) {
        $txt = Get-Content $p.FullName -Raw
        foreach ($m in [regex]::Matches($txt, '<HintPath>\$\(ManagedDir\)\\(.+?\.dll)</HintPath>')) {
            if (-not $managedInZip.ContainsKey($m.Groups[1].Value.ToLower())) { $missing.Add("$($p.BaseName)  ->  Managed\$($m.Groups[1].Value)") }
        }
        foreach ($m in [regex]::Matches($txt, '<HintPath>\$\(BepInExCore\)\\(.+?\.dll)</HintPath>')) {
            if (-not $coreInZip.ContainsKey($m.Groups[1].Value.ToLower())) { $missing.Add("$($p.BaseName)  ->  core\$($m.Groups[1].Value)") }
        }
    }
    if ($missing.Count -gt 0) {
        Block "these referenced DLLs are NOT in punk-refs.zip - CI will fail to compile:"
        $missing | Sort-Object -Unique | ForEach-Object { Write-Host "            $_" -ForegroundColor Red }
    }
    else { Ok "all csproj-referenced DLLs are present in punk-refs.zip." }
}

# ---- Check 2: installed Steam build id matches the recorded refs --------------------------------
$installLeaf = Split-Path $GameDir -Leaf
$steamApps   = Split-Path (Split-Path $GameDir -Parent) -Parent   # ...\common\<game> -> ...\steamapps
$curBuild = $null; $targetBuild = $null
if (Test-Path $steamApps) {
    foreach ($a in Get-ChildItem $steamApps -Filter 'appmanifest_*.acf' -ErrorAction SilentlyContinue) {
        $t = Get-Content $a.FullName -Raw
        $mDir = [regex]::Match($t, '"installdir"\s*"([^"]*)"')
        if ($mDir.Success -and $mDir.Groups[1].Value -eq $installLeaf) {
            $mB = [regex]::Match($t, '"buildid"\s*"([^"]*)"');       if ($mB.Success) { $curBuild = $mB.Groups[1].Value }
            $mT = [regex]::Match($t, '"TargetBuildID"\s*"([^"]*)"'); if ($mT.Success) { $targetBuild = $mT.Groups[1].Value }
            break
        }
    }
}
$gvFile = Join-Path $ModsDir 'game-version.json'
if (-not $curBuild) {
    Warn "couldn't read installed Steam build id (no appmanifest) - skipping game-update check."
}
elseif (-not (Test-Path $gvFile)) {
    Warn "game-version.json not found - run tools\update-refs.ps1."
}
else {
    $recorded = (Get-Content $gvFile -Raw | ConvertFrom-Json).steamBuildId
    if ("$recorded" -ne "$curBuild") {
        Block "the game was UPDATED since refs were built (installed build $curBuild, refs built for $recorded)."
    }
    else {
        Ok "game build id matches refs ($curBuild)."
        if ($targetBuild -and "$targetBuild" -ne "$curBuild") {
            Warn "a game update is pending in Steam (target build $targetBuild). Re-run update-refs after it installs."
        }
    }
}

Write-Host ""
if ($fail) {
    Write-Host "PUSH BLOCKED - the CI reference bundle is stale. Fix it, then push again:" -ForegroundColor Red
    Write-Host "    powershell -File tools\update-refs.ps1" -ForegroundColor White
    Write-Host "    git add game-version.json; git commit -m 'ci: refresh refs bundle'" -ForegroundColor White
    Write-Host "  (Emergency bypass, skips this check:  git push --no-verify)" -ForegroundColor DarkGray
    exit 1
}
if ($warn) { Write-Host "Refs check passed (with warnings above)." -ForegroundColor Yellow; exit 0 }
Write-Host "Refs check passed - safe to push." -ForegroundColor Green
exit 0
