#!/usr/bin/env pwsh
# Downloads the Tailwind CSS standalone CLI (no Node.js required) into tools/.
# The Viewer's MSBuild target picks it up automatically on the next build.
param(
    [string]$Version = "v4.1.14"
)

$ErrorActionPreference = "Stop"

$platform = if ($IsLinux) { "linux-x64" } elseif ($IsMacOS) { "macos-arm64" } else { "windows-x64.exe" }
$fileName = if ($platform -eq "windows-x64.exe") { "tailwindcss.exe" } else { "tailwindcss" }
$url = "https://github.com/tailwindlabs/tailwindcss/releases/download/$Version/tailwindcss-$platform"
$target = Join-Path $PSScriptRoot $fileName

Write-Host "Downloading Tailwind CSS standalone CLI $Version..."
Invoke-WebRequest -Uri $url -OutFile $target

if ($platform -ne "windows-x64.exe") {
    chmod +x $target
}

& $target --help | Select-Object -First 1
Write-Host "Installed at $target"
