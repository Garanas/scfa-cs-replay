#!/usr/bin/env pwsh
# Generates the unit data of a range of FAF releases, from scratch, into
# src/FAForever.Vault.Viewer/wwwroot/data/units/ (see tools/generate-unit-data.cs). Use it to add old
# releases, or after a change to UnitSummary, which changes every data file. It reads each release
# tag from a clone of github.com/FAForever/fa with git archive (only units/, schook/units/ and
# mod_info.lua), so the clone itself is not touched; it needs the tags (git fetch --tags). The units
# the game took from the base game come from a Forged Alliance installation: -BaseGame, or the
# environment variable FA_BASE_GAME. Without -From it starts at the oldest release tag, which is
# also UnitDatabase.FirstVersion in the Viewer: change that when the oldest version changes.
#
#   pwsh tools/backfill-unit-data.ps1 -Source D:\faf-development\fa -BaseGame "D:\SteamLibrary\steamapps\common\Supreme Commander Forged Alliance"
param(
    [Parameter(Mandatory)] [string]$Source,
    [string]$BaseGame = $env:FA_BASE_GAME,
    [int]$From = 0,
    [int]$To = 0,
    [string]$Output = (Join-Path $PSScriptRoot "..\src\FAForever.Vault.Viewer\wwwroot\data\units")
)

$ErrorActionPreference = "Stop"

if (-not $BaseGame) { throw "Where is the base game? Pass -BaseGame <Forged Alliance folder> or set FA_BASE_GAME." }
$generator = Join-Path $PSScriptRoot "generate-unit-data.cs"
$work = Join-Path ([System.IO.Path]::GetTempPath()) "fa-unit-data-$([guid]::NewGuid().ToString('N'))"

# Release tags are plain numbers. The FA repository also has branches named like versions, so
# always use refs/tags/.
$tags = git -C $Source tag --list | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ } |
    Where-Object { $_ -ge $From -and ($To -eq 0 -or $_ -le $To) } | Sort-Object
if (-not $tags) { throw "No release tags between $From and $To in $Source (git fetch --tags?)" }

# From scratch: oldest first, so each data file is named after the first version that has its units.
if (Test-Path $Output) { Remove-Item -Recurse -Force $Output }
New-Item -ItemType Directory -Force $Output | Out-Null

try {
    $rebuild = @("--no-cache")  # build the generator fresh once, then reuse that build
    foreach ($tag in $tags) {
        $dir = Join-Path $work $tag
        New-Item -ItemType Directory -Force $dir | Out-Null
        $archive = Join-Path $work "$tag.tar"
        # The oldest releases keep their units in schook/units/ and have no mod_info.lua; git archive
        # fails on a path the tag does not have.
        $paths = @(@("units", "schook/units", "mod_info.lua") | Where-Object { git -C $Source ls-tree --name-only "refs/tags/$tag" -- $_ })
        if (-not $paths) { throw "Release $tag has no unit blueprints" }
        git -C $Source archive --format=tar -o $archive "refs/tags/$tag" $paths
        if ($LASTEXITCODE -ne 0) { throw "git archive failed for $tag" }
        # .NET's reader, not tar on the path: from Git Bash that is GNU tar, which takes C:\ for a host
        [System.Formats.Tar.TarFile]::ExtractToDirectory($archive, $dir, $true)

        # where the data comes from: the commit of the tag and its date
        $commit = git -C $Source rev-parse "refs/tags/$tag^{commit}"
        $released = git -C $Source log -1 --format=%cs "refs/tags/$tag"

        # the tag is the release: its mod_info.lua does not always say so (3805 says 3804)
        dotnet run @rebuild $generator -- $dir $Output --base-game $BaseGame --version $tag --commit $commit --released $released
        if ($LASTEXITCODE -ne 0) { throw "generate-unit-data failed for $tag" }
        $rebuild = @()
        Remove-Item -Recurse -Force $dir, $archive
    }
}
finally {
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
}

Write-Host "Unit data of $($tags.Count) releases in $Output"
