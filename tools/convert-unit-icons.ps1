#!/usr/bin/env pwsh
# Converts the unit icons and their layer backgrounds from the FA game repository (DDS) into PNGs
# for the Viewer, plus a sprite atlas per group with a JSON index. Requires ImageMagick 7 (`magick`).
#
#   <Output>/units/<blueprint id>.png        individual unit icons, lowercase id (e.g. uel0101.png)
#   <Output>/backgrounds/<name>.png          layer backgrounds (land_up, air_over, sea_down, ...)
#   <Output>/units-atlas.png + .json         all unit icons on a fixed grid of $CellSize cells
#   <Output>/backgrounds-atlas.png + .json   idem for the backgrounds
#
# The JSON index maps each name to its cell: { "cellSize": 64, "columns": 25, "icons": { "uel0101": { "x": 0, "y": 0 } } }.
param(
    [string]$Source = "D:/faf-development/fa/textures/ui/common/icons/units",
    [string]$Output = (Join-Path $PSScriptRoot "../FAForever.Replay.Viewer/wwwroot/images/units"),
    [int]$CellSize = 64,
    [int]$Columns = 25
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command magick -ErrorAction SilentlyContinue)) {
    throw "ImageMagick 7 ('magick') is not on PATH."
}

$unitsDir = Join-Path $Output "units"
$backgroundsDir = Join-Path $Output "backgrounds"
New-Item -ItemType Directory -Force $unitsDir, $backgroundsDir | Out-Null

# Strip timestamps so re-running the script yields byte-identical files (clean git diffs).
$pngOptions = @("-define", "png:exclude-chunks=date,time")

$units = [System.Collections.Generic.List[string]]::new()
$backgrounds = [System.Collections.Generic.List[string]]::new()

foreach ($file in Get-ChildItem $Source -Filter *.dds | Sort-Object { $_.BaseName.ToLowerInvariant() }) {
    $name = $file.BaseName.ToLowerInvariant()
    if ($name.EndsWith("_icon")) {
        $name = $name.Substring(0, $name.Length - "_icon".Length)
        magick $file.FullName @pngOptions (Join-Path $unitsDir "$name.png")
        $units.Add($name)
    } else {
        magick $file.FullName @pngOptions (Join-Path $backgroundsDir "$name.png")
        $backgrounds.Add($name)
    }
}

function New-Atlas([string]$Directory, [System.Collections.Generic.List[string]]$Names, [string]$AtlasName) {
    $cols = [Math]::Min($Columns, $Names.Count)
    $rows = [int][Math]::Ceiling($Names.Count / $cols)
    # Hundreds of paths overflow the Windows command line, so hand them to ImageMagick as an @list file.
    $list = New-TemporaryFile
    $Names | ForEach-Object { (Join-Path $Directory "$_.png").Replace('\', '/') } | Set-Content $list

    # A few icons are 32, 48 or 72 px: fit them into the cell, centred, on transparency.
    magick montage "@$list" -background none -geometry "${CellSize}x${CellSize}>+0+0" `
        -gravity center -tile "${cols}x${rows}" -depth 8 @pngOptions (Join-Path $Output "$AtlasName.png")
    Remove-Item $list

    $icons = [ordered]@{}
    for ($i = 0; $i -lt $Names.Count; $i++) {
        $icons[$Names[$i]] = [ordered]@{ x = ($i % $cols) * $CellSize; y = [int][Math]::Floor($i / $cols) * $CellSize }
    }
    [ordered]@{ cellSize = $CellSize; columns = $cols; rows = $rows; icons = $icons } |
        ConvertTo-Json -Depth 4 -Compress | Set-Content -NoNewline (Join-Path $Output "$AtlasName.json")
}

New-Atlas $unitsDir $units "units-atlas"
New-Atlas $backgroundsDir $backgrounds "backgrounds-atlas"

Write-Host "Converted $($units.Count) unit icons and $($backgrounds.Count) backgrounds into $Output"
