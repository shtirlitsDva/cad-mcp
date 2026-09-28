#Requires -Version 7.0
<#
.SYNOPSIS
  Fail if CI.slnf has drifted from Acd.Mcp.sln.

.DESCRIPTION
  A solution filter lists the projects to build, so a project added to the
  .sln but not the .slnf is silently never built or tested on CI. That is a
  failure mode with no symptom — the build stays green while coverage
  quietly shrinks. (It already happened once: the batch test project fell
  out of the solution during the src/Autocad + src/Revit split and 57 tests
  stopped running without anyone noticing.)

  This script makes that drift a red build instead. Every project in the
  solution must be either in CI.slnf or in $ExcludedProjects below, with a
  stated reason. Nothing is allowed to be in neither.

  Run locally the same way CI runs it:
    pwsh ./scripts/Test-CIFilterCoverage.ps1
#>
[CmdletBinding()]
param(
    [string] $Solution = 'Acd.Mcp.sln',
    [string] $Filter   = 'CI.slnf'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $repoRoot

# Projects deliberately kept out of CI. Key = path as it appears in the .sln,
# value = why it cannot run on a GitHub runner. Adding an entry here is a
# conscious decision; forgetting to add one is what this script catches.
$ExcludedProjects = @{
    'tests\Revit\Rvt.Mcp.Tests.RequiresRevit\Rvt.Mcp.Tests.RequiresRevit.csproj' =
        'Constructs real Autodesk.Revit.DB types; RevitAPI.dll P/Invokes into native Revit DLLs that no NuGet package can supply.'
    'src\Bricscad\Bcad.Mcp.Api\Bcad.Mcp.Api.csproj' =
        'References BrxMgd/TD_Mgd from a BricsCAD install; Bricsys publishes no NuGet reference package.'
    'src\Bricscad\Bcad.Mcp\Bcad.Mcp.csproj' =
        'References BrxMgd/TD_Mgd from a BricsCAD install; Bricsys publishes no NuGet reference package.'
}

$slnProjects = [regex]::Matches(
        (Get-Content $Solution -Raw), '"([^"]*\.csproj)"') |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique

$filterProjects = (Get-Content $Filter -Raw | ConvertFrom-Json).solution.projects

$missing = $slnProjects | Where-Object {
    $_ -notin $filterProjects -and -not $ExcludedProjects.ContainsKey($_)
}
$stale = $filterProjects | Where-Object { $_ -notin $slnProjects }

$failed = $false

if ($missing) {
    Write-Host "✗ In ${Solution} but neither in ${Filter} nor excluded:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "    $_" }
    Write-Host "  Add each to $Filter, or to `$ExcludedProjects in this script with a reason." -ForegroundColor Yellow
    $failed = $true
}

if ($stale) {
    Write-Host "✗ In ${Filter} but no longer in ${Solution}:" -ForegroundColor Red
    $stale | ForEach-Object { Write-Host "    $_" }
    $failed = $true
}

if ($failed) { exit 1 }

Write-Host "✓ $Filter covers all $($slnProjects.Count) solution projects ($($ExcludedProjects.Count) deliberately excluded)." -ForegroundColor Green
foreach ($kv in $ExcludedProjects.GetEnumerator()) {
    Write-Host "    excluded: $($kv.Key)" -ForegroundColor DarkGray
    Write-Host "              $($kv.Value)"  -ForegroundColor DarkGray
}
