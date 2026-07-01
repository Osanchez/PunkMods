# tools/update-refs.ps1
# Refresh the CI reference bundle after a game update, in ONE step:
# regenerates punk-refs.zip from your current install and re-uploads it to the private
# Osanchez/PunkMods-refs 'refs' release, which CI pulls on the next push to main.
#
#   powershell -ExecutionPolicy Bypass -File tools\update-refs.ps1
#
# Requires the GitHub CLI (gh) authenticated with access to the refs repo. Works if gh is on Windows
# PATH, or only inside WSL (this script falls back to `wsl gh` automatically).
param(
    [string]$GameDir,
    [string]$RefsRepo = 'Osanchez/PunkMods-refs'
)
$ErrorActionPreference = 'Stop'
$Tools   = $PSScriptRoot
$ModsDir = Split-Path $Tools -Parent

# 1) Rebuild the bundle from the (freshly updated) local install.
& (Join-Path $Tools 'make-refs.ps1') -GameDir $GameDir
$zip = Join-Path $ModsDir 'punk-refs.zip'
if (-not (Test-Path $zip)) { throw "punk-refs.zip was not produced by make-refs.ps1" }

# 2) Upload, replacing the existing asset. Prefer native gh; fall back to gh inside WSL.
Write-Host "`nUploading punk-refs.zip to $RefsRepo (refs release)..." -ForegroundColor Cyan
if (Get-Command gh -ErrorAction SilentlyContinue) {
    gh release upload refs "$zip" --repo $RefsRepo --clobber
}
elseif (Get-Command wsl -ErrorAction SilentlyContinue) {
    $wslZip = (wsl wslpath -a "$zip").Trim()
    wsl gh release upload refs "$wslZip" --repo $RefsRepo --clobber
}
else {
    throw "gh not found (native or WSL). Upload manually: gh release upload refs `"$zip`" --repo $RefsRepo --clobber"
}
if ($LASTEXITCODE -ne 0) { throw "Upload failed (exit $LASTEXITCODE)." }
Write-Host "Done. CI will use the refreshed DLLs on the next push to main." -ForegroundColor Green
