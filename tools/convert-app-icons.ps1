#!/usr/bin/env pwsh
# Renders the app icons (favicon, PWA manifest icons, Apple touch icon) from
# src/FAForever.Vault.Viewer/wwwroot/icons/icon.svg. Requires ImageMagick 7 (`magick`).
# The SVG is full bleed with its artwork inside the 80% safe zone, so it is used as-is for the
# maskable icon; the regular icons get rounded corners.
param(
    [string]$Output = (Join-Path $PSScriptRoot "../src/FAForever.Vault.Viewer/wwwroot")
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command magick -ErrorAction SilentlyContinue)) {
    throw "ImageMagick 7 ('magick') is not on PATH."
}

$svg = Join-Path $Output "icons/icon.svg"
# Strip timestamps so re-running the script yields byte-identical files (clean git diffs).
$pngOptions = @("-depth", "8", "-define", "png:exclude-chunks=date,time")

function Render([int]$size, [string]$file, [bool]$rounded) {
    $target = Join-Path $Output $file
    if ($rounded) {
        $radius = [int]($size * 0.1875)
        magick -background none -density 384 $svg -resize "${size}x${size}" `
            "(" -size "${size}x${size}" xc:none -fill white -draw "roundrectangle 0,0,$($size - 1),$($size - 1),$radius,$radius" ")" `
            -compose DstIn -composite @pngOptions $target
    } else {
        magick -background none -density 384 $svg -resize "${size}x${size}" @pngOptions $target
    }
    Write-Host "  $file"
}

Render 32 "favicon.png" $true
Render 192 "icons/icon-192.png" $true
Render 512 "icons/icon-512.png" $true
Render 512 "icons/icon-maskable-512.png" $false
Render 180 "icons/apple-touch-icon.png" $false

Write-Host "Wrote the app icons to $Output"
