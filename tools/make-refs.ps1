# tools/make-refs.ps1
# ONE-TIME setup (re-run whenever you add a NEW DLL reference to any mod):
# Collects the proprietary game / Unity / BepInEx reference assemblies your mods compile against
# into punk-refs.zip, mirroring the real game folder layout, then prints how to upload it as the
# private 'refs' release asset that CI downloads at build time.
#
#   powershell -ExecutionPolicy Bypass -File tools\make-refs.ps1
#
# IMPORTANT: these DLLs are NOT redistributable (closed playtest + Unity assemblies).
# Keep the GitHub repo PRIVATE. Nothing here is ever committed to git.
param(
    # Root of your local game install. Defaults to the folder above Mods\.
    [string]$GameDir
)
$ErrorActionPreference = 'Stop'
$ModsDir = Split-Path $PSScriptRoot -Parent          # repo root = Mods\
if (-not $GameDir) { $GameDir = Split-Path $ModsDir -Parent }

$Managed = Join-Path $GameDir 'Punk_Data\Managed'
$Core    = Join-Path $GameDir 'BepInEx\core'
if (-not (Test-Path $Managed)) { throw "Managed dir not found: $Managed" }
if (-not (Test-Path $Core))    { throw "BepInEx core not found: $Core  (install BepInEx into the game first)" }

# Stage a mini game-root: BepInEx\core + Punk_Data\Managed with only the DLLs we reference.
$Stage        = Join-Path $ModsDir 'dist-refs'
$StageManaged = Join-Path $Stage 'Punk_Data\Managed'
$StageCore    = Join-Path $Stage 'BepInEx\core'
if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $StageManaged, $StageCore | Out-Null

# Discover which DLLs the projects actually reference, resolving the two HintPath roots.
$projects = Get-ChildItem $ModsDir -Recurse -Filter *.csproj |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|dist|dist-refs)\\' }
$managedDlls = [System.Collections.Generic.HashSet[string]]::new()
$coreDlls    = [System.Collections.Generic.HashSet[string]]::new()
foreach ($p in $projects) {
    $txt = Get-Content $p.FullName -Raw
    foreach ($m in [regex]::Matches($txt, '<HintPath>\$\(ManagedDir\)\\(.+?\.dll)</HintPath>'))  { [void]$managedDlls.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($txt, '<HintPath>\$\(BepInExCore\)\\(.+?\.dll)</HintPath>')) { [void]$coreDlls.Add($m.Groups[1].Value) }
}

foreach ($d in $managedDlls) {
    $src = Join-Path $Managed $d
    if (-not (Test-Path $src)) { throw "Referenced DLL missing from game: $src" }
    Copy-Item $src $StageManaged -Force
}
foreach ($d in $coreDlls) {
    $src = Join-Path $Core $d
    if (-not (Test-Path $src)) { throw "Referenced DLL missing from BepInEx core: $src" }
}
# Stage the FULL BepInEx core, not just the compile-time references. CI's Copy-Loader packages this
# folder into the public BepInEx-Setup.zip, and at runtime Doorstop needs the whole preloader chain
# (BepInEx.Unity.Mono.Preloader, BepInEx.Preloader.Core, Mono.Cecil.*, MonoMod.*, ...). Staging only
# the referenced DLLs shipped a loader whose target_assembly didn't exist - it silently never started.
# (BepInEx is open source and redistributable; only Punk_Data\Managed is proprietary.)
Copy-Item (Join-Path $Core '*') $StageCore -Recurse -Force

# Loader redistributables + config, so CI can assemble the same extract-into-game package you build locally.
foreach ($f in @('winhttp.dll','doorstop_config.ini','.doorstop_version','changelog.txt')) {
    $src = Join-Path $GameDir $f
    if (Test-Path $src) { Copy-Item $src $Stage -Force }
}
$bcfg = Join-Path $GameDir 'BepInEx\config\BepInEx.cfg'
if (Test-Path $bcfg) {
    New-Item -ItemType Directory -Force -Path (Join-Path $Stage 'BepInEx\config') | Out-Null
    Copy-Item $bcfg (Join-Path $Stage 'BepInEx\config\BepInEx.cfg') -Force
}

$zip = Join-Path $ModsDir 'punk-refs.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
$items = Get-ChildItem -Path $Stage -Force | Select-Object -ExpandProperty FullName
Compress-Archive -Path $items -DestinationPath $zip -Force
Remove-Item $Stage -Recurse -Force

$size = [math]::Round((Get-Item $zip).Length / 1MB, 2)
Write-Host "`nCreated $zip ($size MB)"  -ForegroundColor Green
Write-Host "  Managed DLLs: $($managedDlls.Count)    Core DLLs: $($coreDlls.Count)"
Write-Host "`nUpload it as the private 'refs' release asset (replace <owner>/<repo>):" -ForegroundColor Cyan
Write-Host "  gh release create refs punk-refs.zip --repo <owner>/<repo> --prerelease --title `"CI refs`" --notes `"Reference assemblies for CI - do not distribute`""
Write-Host "  # later, to refresh after re-running this script:"
Write-Host "  gh release upload refs punk-refs.zip --repo <owner>/<repo> --clobber"
