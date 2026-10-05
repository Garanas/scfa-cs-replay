#!/usr/bin/env pwsh
# Generates the unit data of a range of FAF releases, from scratch, into
# src/FAForever.Vault.Viewer/wwwroot/data/units/ (see tools/generate-unit-data.cs). Use it to add old
# releases, or after a change to UnitSummary, which changes every data file. It reads each release
# tag from a clone of github.com/FAForever/fa with git archive (only units/ and mod_info.lua), so the
# clone itself is not touched; it needs the tags (git fetch --tags).
#
#   pwsh tools/backfill-unit-data.ps1 -Source D:\faf-development\fa -From 3801 -To 3839
param(
    [Parameter(Mandatory)] [string]$Source,
    [int]$From = 3801,
    [int]$To = 0
)

$ErrorActionPreference = "Stop"

$output = Join-Path $PSScriptRoot "..\src\FAForever.Vault.Viewer\wwwroot\data\units"
$generator = Join-Path $PSScriptRoot "generate-unit-data.cs"
$work = Join-Path ([System.IO.Path]::GetTempPath()) "fa-unit-data-$([guid]::NewGuid().ToString('N'))"

# Release tags are plain numbers. The FA repository also has branches named like versions, so
# always use refs/tags/.
$tags = git -C $Source tag --list | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ } |
    Where-Object { $_ -ge $From -and ($To -eq 0 -or $_ -le $To) } | Sort-Object
if (-not $tags) { throw "No release tags between $From and $To in $Source (git fetch --tags?)" }

# From scratch: oldest first, so each data file is named after the first version that has its units.
if (Test-Path $output) { Remove-Item -Recurse -Force $output }
New-Item -ItemType Directory -Force $output | Out-Null

try {
    $rebuild = @("--no-cache")  # build the generator fresh once, then reuse that build
    foreach ($tag in $tags) {
        $dir = Join-Path $work $tag
        New-Item -ItemType Directory -Force $dir | Out-Null
        $archive = Join-Path $work "$tag.tar"
        git -C $Source archive --format=tar -o $archive "refs/tags/$tag" units mod_info.lua
        if ($LASTEXITCODE -ne 0) { throw "git archive failed for $tag" }
        # .NET's reader, not tar on the path: from Git Bash that is GNU tar, which takes C:\ for a host
        [System.Formats.Tar.TarFile]::ExtractToDirectory($archive, $dir, $true)

        # where the data comes from: the commit of the tag and its date
        $commit = git -C $Source rev-parse "refs/tags/$tag^{commit}"
        $released = git -C $Source log -1 --format=%cs "refs/tags/$tag"

        # the tag is the release: its mod_info.lua does not always say so (3805 says 3804)
        dotnet run @rebuild $generator -- $dir $output --version $tag --commit $commit --released $released
        if ($LASTEXITCODE -ne 0) { throw "generate-unit-data failed for $tag" }
        $rebuild = @()
        Remove-Item -Recurse -Force $dir, $archive
    }
}
finally {
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
}

Write-Host "Unit data of $($tags.Count) releases in $output"
