# recover-default-save.ps1
#
# Recovers Meta Loadout progress that versions before 2.1.0 filed under the class name "default".
#
# Why it happens: PUNK's "Continue" button skips the loadout selector, so a continued run carried no
# starting loadout and the mod keyed that run's saves as "default" instead of the class the run was
# actually started with. Everything earned in a continued session went to vault_default.json /
# profiles/<name>/default.json, and the next fresh run restored the older per-class file -- which
# looks exactly like the save being wiped. Nothing was deleted; it is in the "default" files.
#
# Usage (close the game first):
#   powershell -File recover-default-save.ps1                                   # report only
#   powershell -File recover-default-save.ps1 -To Starter_Worm_Drone -Apply     # restore into that class
#
# -Apply backs up whatever it overwrites to <file>.recovery-bak first.

[CmdletBinding()]
param(
    # Class to recover INTO, e.g. Starter_Worm_Drone. Run without it to see the choices.
    [string] $To,
    # Only touch this profile folder; default is every profile that has a default.json.
    [string] $ProfileName,
    # Actually copy. Without it the script only reports.
    [switch] $Apply,
    # Override the save location if the game stores data somewhere non-standard.
    [string] $Root = (Join-Path $env:USERPROFILE 'AppData\LocalLow\DefaultCompany\Punk\meta_loadouts')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Root)) {
    Write-Host "No Meta Loadout saves at: $Root" -ForegroundColor Yellow
    Write-Host "If your game data lives elsewhere, pass -Root <path>."
    exit 1
}

Write-Host "Meta Loadout saves: $Root`n" -ForegroundColor Cyan

# ---- inventory ----------------------------------------------------------------------------------
Write-Host 'Shared vault, one file per class:' -ForegroundColor White
Get-ChildItem $Root -Filter 'vault_*.json' | Sort-Object Name | ForEach-Object {
    $cls = $_.BaseName -replace '^vault_', ''
    $mark = if ($cls -eq 'default') { '   <-- stranded by the Continue bug' } else { '' }
    Write-Host ('  ' + ('{0,-40} {1,10:N0} B  {2}' -f $cls, $_.Length, $_.LastWriteTime) + $mark)
}

$profilesDir = Join-Path $Root 'profiles'
$profileDirs = @()
if (Test-Path $profilesDir) {
    $profileDirs = Get-ChildItem $profilesDir -Directory | Where-Object { -not $ProfileName -or $_.Name -eq $ProfileName }
}
foreach ($p in $profileDirs) {
    Write-Host "`nProfile '$($p.Name)', one ship build per class:" -ForegroundColor White
    Get-ChildItem $p.FullName -Filter '*.json' | Sort-Object Name | ForEach-Object {
        $mark = if ($_.BaseName -eq 'default') { '   <-- stranded by the Continue bug' } else { '' }
        Write-Host ('  ' + ('{0,-40} {1,10:N0} B  {2}' -f $_.BaseName, $_.Length, $_.LastWriteTime) + $mark)
    }
}

# ---- is there anything to recover? --------------------------------------------------------------
$vaultDefault = Join-Path $Root 'vault_default.json'
$gridDefaults = @($profileDirs | ForEach-Object { Join-Path $_.FullName 'default.json' } | Where-Object { Test-Path $_ })

if (-not (Test-Path $vaultDefault) -and $gridDefaults.Count -eq 0) {
    Write-Host "`nNo 'default' files here -- nothing was stranded. " -NoNewline -ForegroundColor Green
    Write-Host 'Your saves are already filed under their class.'
    exit 0
}

$classes = Get-ChildItem $Root -Filter 'vault_*.json' |
           ForEach-Object { $_.BaseName -replace '^vault_', '' } |
           Where-Object { $_ -ne 'default' }

if (-not $To) {
    Write-Host "`nThere is stranded 'default' data. Compare its size and timestamp against the class you" -ForegroundColor Yellow
    Write-Host 'were playing above -- if "default" is the newer/bigger one, that is your missing progress.'
    Write-Host "`nTo restore it, close the game and run:" -ForegroundColor Yellow
    foreach ($c in $classes) { Write-Host "  powershell -File recover-default-save.ps1 -To $c -Apply" }
    if (-not $classes) { Write-Host '  powershell -File recover-default-save.ps1 -To <ClassName> -Apply' }
    exit 0
}

# ---- recover ------------------------------------------------------------------------------------
$plan = @()
if (Test-Path $vaultDefault) { $plan += ,@($vaultDefault, (Join-Path $Root "vault_$To.json")) }
foreach ($g in $gridDefaults) { $plan += ,@($g, (Join-Path (Split-Path $g) "$To.json")) }

Write-Host "`nPlan -- recover 'default' into class '$To':" -ForegroundColor Cyan
foreach ($pair in $plan) {
    $src, $dst = $pair
    $same = (Test-Path $dst) -and ((Get-FileHash $src).Hash -eq (Get-FileHash $dst).Hash)
    $note = if ($same) { '  (identical -- nothing to gain)' } else { '' }
    Write-Host "  $(Split-Path $src -Leaf)  ->  $(Split-Path $dst -Leaf)$note"
}

if (-not $Apply) {
    Write-Host "`nReport only. Re-run with -Apply to perform the copy." -ForegroundColor Yellow
    exit 0
}

foreach ($pair in $plan) {
    $src, $dst = $pair
    if (Test-Path $dst) {
        Copy-Item $dst "$dst.recovery-bak" -Force
        Write-Host "  backed up $(Split-Path $dst -Leaf) -> $(Split-Path $dst -Leaf).recovery-bak" -ForegroundColor DarkGray
    }
    Copy-Item $src $dst -Force
    Write-Host "  restored  $(Split-Path $dst -Leaf)" -ForegroundColor Green
}

Write-Host "`nDone. Start a NEW run with the '$To' class (not Continue) to load it." -ForegroundColor Green
Write-Host 'If it looks wrong, the previous contents are in the .recovery-bak files next to them.'
