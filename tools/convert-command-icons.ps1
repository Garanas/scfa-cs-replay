#!/usr/bin/env pwsh
# Converts the game's waypoint button textures (DDS) into the command-category marker icons
# of the Playthrough tab. Requires ImageMagick 7 (`magick`). One PNG per CommandCategory slug;
# keep the mapping below and CommandIcons.Available (Services/Commands/CommandIcons.cs) in sync.
# The Special category deliberately has no icon and keeps its SVG glyph.
param(
    [string]$Source = "D:/SteamLibrary/steamapps/common/Supreme Commander Forged Alliance/gamedata/textures/textures/ui/common/game/waypoints",
    [string]$Output = (Join-Path $PSScriptRoot "../src/FAForever.Vault.Viewer/wwwroot/images/commands"),
    [int]$Size = 64
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command magick -ErrorAction SilentlyContinue)) {
    throw "ImageMagick 7 ('magick') is not on PATH."
}

# CommandCategory slug -> waypoint texture base name.
$mapping = [ordered]@{
    move       = "move_btn_up"
    attack     = "attack_btn_up"
    aggressive = "attack_move_btn_up"
    patrol     = "patrol_btn_up"
    build      = "production_btn_up"
    launch     = "nuke_btn_up"
    reclaim    = "reclaim_btn_up"
    repair     = "repair_btn_up"
    capture    = "convert_btn_up"
    guard      = "guard_btn_up"
    transport  = "load_btn_up"
    teleport   = "teleport_btn_up"
    stop       = "stop_btn_up"
}

New-Item -ItemType Directory -Force $Output | Out-Null

# Strip timestamps so re-running the script yields byte-identical files (clean git diffs).
$pngOptions = @("-define", "png:exclude-chunks=date,time")

foreach ($entry in $mapping.GetEnumerator()) {
    $dds = Join-Path $Source "$($entry.Value).dds"
    if (-not (Test-Path $dds)) {
        throw "Missing texture: $dds"
    }
    magick $dds -resize "${Size}x${Size}" @pngOptions (Join-Path $Output "$($entry.Key).png")
    Write-Host "  $($entry.Key).png <- $($entry.Value).dds"
}

Write-Host "Wrote $($mapping.Count) command icons to $Output"
