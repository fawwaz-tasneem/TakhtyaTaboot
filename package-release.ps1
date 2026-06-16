<#
.SYNOPSIS
    Packages a clean, runtime-only release of The Hindostan Mod.

.DESCRIPTION
    Stages ONLY the files needed to run the mod (no source, no wiki, no docs,
    no data-tooling scripts) into a folder named "TheHindostanMod" and zips it
    for distribution via GitHub Releases.

    Users extract the zip into:  Mount & Blade II Bannerlord\Modules\

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\package-release.ps1
    powershell -ExecutionPolicy Bypass -File .\package-release.ps1 -Version v1.0.0
#>
param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root  = $PSScriptRoot
$stage = Join-Path $root "release\TheHindostanMod"

# Derive version from SubModule.xml if not supplied.
if (-not $Version) {
    [xml]$sm = Get-Content (Join-Path $root "SubModule.xml")
    $Version = $sm.Module.Version.value
}
Write-Host "Packaging The Hindostan Mod $Version" -ForegroundColor Cyan

# Clean previous staging.
if (Test-Path (Join-Path $root "release")) {
    Remove-Item (Join-Path $root "release") -Recurse -Force
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null

# Helper: copy a whole folder into the staging dir, excluding patterns.
function Copy-Tree($name, [string[]]$excludeDirs, [string[]]$excludeExt) {
    $srcDir = Join-Path $root $name
    if (-not (Test-Path $srcDir)) { Write-Host "  (skip $name - not present)"; return }
    Get-ChildItem $srcDir -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($root.Length).TrimStart('\')
        $skip = $false
        foreach ($d in $excludeDirs) { if ($rel -match [regex]::Escape($d)) { $skip = $true; break } }
        if (-not $skip) {
            foreach ($e in $excludeExt) { if ($_.Extension -ieq $e) { $skip = $true; break } }
        }
        if (-not $skip) {
            $dest = Join-Path $stage $rel
            New-Item -ItemType Directory -Path (Split-Path $dest) -Force | Out-Null
            Copy-Item $_.FullName $dest -Force
        }
    }
    Write-Host "  + $name" -ForegroundColor DarkGray
}

# 1. Module manifest.
Copy-Item (Join-Path $root "SubModule.xml") (Join-Path $stage "SubModule.xml") -Force
Write-Host "  + SubModule.xml" -ForegroundColor DarkGray

# 2. Compiled assembly (DLL only - no .pdb debug symbols).
$dll = Join-Path $root "bin\Win64_Shipping_Client\TheHindostanMod.dll"
if (-not (Test-Path $dll)) { throw "Built DLL not found - build the project in Release first: $dll" }
$dllDest = Join-Path $stage "bin\Win64_Shipping_Client"
New-Item -ItemType Directory -Path $dllDest -Force | Out-Null
Copy-Item $dll $dllDest -Force
Write-Host "  + bin\Win64_Shipping_Client\TheHindostanMod.dll" -ForegroundColor DarkGray

# 3. ModuleData - runtime XML + localization only. Drop the data-authoring
#    tooling (.py/.xslt/.csv/.xlsx), the project file, and dev readmes/working data.
Copy-Tree "ModuleData" -excludeDirs @("Mod data") -excludeExt @(".py", ".xslt", ".csv", ".xlsx", ".mbproj", ".txt")

# 4. GUI - prefabs, sprite data, brushes.
Copy-Tree "GUI" -excludeDirs @() -excludeExt @()

# 5. Map / scene / compiled-asset data (needed to run; gitignored, lives only on disk).
#    Exclude editor scratch (Backups, *_HOLD) to keep the release lean.
Copy-Tree "Assets"           -excludeDirs @("Backups", "_HOLD") -excludeExt @()
Copy-Tree "AssetPackages"    -excludeDirs @("Backups", "_HOLD") -excludeExt @()
Copy-Tree "SceneObj"         -excludeDirs @("Backups", "_HOLD") -excludeExt @()
Copy-Tree "RuntimeDataCache" -excludeDirs @("Backups", "_HOLD") -excludeExt @()

# Zip it.
$zip = Join-Path $root "TheHindostanMod-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ""
Write-Host "Release built: $zip ($mb MB)" -ForegroundColor Green
Write-Host "Top-level folder inside the zip is 'TheHindostanMod' - users extract it straight into Modules\." -ForegroundColor Green
