#!/usr/bin/env pwsh
# Converts the game's unit view icons (DDS) into the small icons of the Statistics tab: mass,
# energy, kills, built and so on. Requires ImageMagick 7 (`magick`). The icons keep their native
# size (about 20 px); the Viewer shows them at that size.
param(
    [string]$Source = "D:/SteamLibrary/steamapps/common/Supreme Commander Forged Alliance/gamedata/textures/textures/ui/common/game/unit_view_icons",
    [string]$Output = (Join-Path $PSScriptRoot "../src/FAForever.Vault.Viewer/wwwroot/images/stats")
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command magick -ErrorAction SilentlyContinue)) {
    throw "ImageMagick 7 ('magick') is not on PATH."
}

$names = @("mass", "energy", "kills", "build", "redcross")

New-Item -ItemType Directory -Force $Output | Out-Null

# Strip timestamps so re-running the script yields byte-identical files (clean git diffs).
$pngOptions = @("-define", "png:exclude-chunks=date,time")

foreach ($name in $names) {
    $dds = Join-Path $Source "$name.dds"
    if (-not (Test-Path $dds)) {
        throw "Missing texture: $dds"
    }
    magick $dds @pngOptions (Join-Path $Output "$name.png")
    Write-Host "  $name.png <- $name.dds"
}

Write-Host "Wrote $($names.Count) stat icons to $Output"
