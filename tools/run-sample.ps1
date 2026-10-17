<#
.SYNOPSIS
    Builds the Server and runs it on the sample Players data (no token, no login).

.DESCRIPTION
    The app is signed in as the sample player 2049311 "Ashgrove" and the Players pages read the embedded
    fixtures (SamplePlayersApi). Run it from any folder of the worktree; it builds first, then stays in the
    foreground until Ctrl+C (an agent starts it as a background command and stops that task when done).
    With -Development the Server runs in the Development environment, which also serves dev/charts.

.PARAMETER Port
    The port on 127.0.0.1. 5080 is the owner's dev server: never use it for this.

.PARAMETER Artifacts
    The --artifacts-path of the build (keep it short: D:\wt\art-<id>); default D:\wt\art-<worktree folder>.

.PARAMETER Development
    Run as ASPNETCORE_ENVIRONMENT=Development instead of Sample, for dev/charts. The Server answers
    appsettings.Sample.json only in the Sample environment, so the Players pages need a real session here.

.PARAMETER NoBuild
    Skip the build (the Server and the Viewer must be built into -Artifacts already).

.EXAMPLE
    pwsh tools/run-sample.ps1 -Port 5398 -Artifacts D:\wt\art-34a
    pwsh tools/run-sample.ps1 -Port 5399 -Artifacts D:\wt\art-34a -Development   # dev/charts
#>
param(
    [int]$Port = 5398,
    [string]$Artifacts,
    [switch]$Development,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Artifacts) {
    $Artifacts = Join-Path 'D:\wt' ('art-' + (Split-Path -Leaf $root))
}
if ($Port -eq 5080) {
    throw 'Port 5080 is the owner''s dev server; pick another port.'
}
if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) {
    throw "Port $Port is already in use."
}

Set-Location $root
if (-not $NoBuild) {
    dotnet build src/FAForever.Vault.Server --artifacts-path $Artifacts
    if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }
}

$env:ASPNETCORE_ENVIRONMENT = if ($Development) { 'Development' } else { 'Sample' }
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
Write-Host "Server on http://127.0.0.1:$Port ($($env:ASPNETCORE_ENVIRONMENT)); sample player: /players/2049311"
dotnet run --project src/FAForever.Vault.Server --artifacts-path $Artifacts --no-launch-profile --no-build
