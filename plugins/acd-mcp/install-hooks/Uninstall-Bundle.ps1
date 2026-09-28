#Requires -Version 7.0
<#
.SYNOPSIS
  Remove the ACD-MCP plugin bundles (AutoCAD and BricsCAD), and optionally
  purge user data.

.DESCRIPTION
  Inverse of Install-Bundle.ps1. Per host, refuses while that host is
  running unless -Force.

  -Purge also wipes user-authored content: %APPDATA%\<folder>\ and
  %LOCALAPPDATA%\<folder>\ for each host (Acd.Mcp for AutoCAD, Bcad.Mcp for
  BricsCAD: dto-user, saved scripts, batch-run history, diagnostic log).
  Lives on this side of the split because the bundle is what creates that
  data — removing the bundle is the "I'm done with ACD-MCP" signal.
  Uninstall-Mcp.ps1 leaves user data alone.

.PARAMETER Force
  Skip the host-running check when removing a bundle.

.PARAMETER Purge
  Also delete dto-user/, scripts/, batch-runs/, log.txt.

.EXAMPLE
  pwsh .\Uninstall-Bundle.ps1
  # Remove the bundles; leave user content intact.

.EXAMPLE
  pwsh .\Uninstall-Bundle.ps1 -Purge
  # Remove the bundles AND wipe all user content.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch] $Force,
    [switch] $Purge
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step($msg)  { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Skip2($msg) { Write-Host "    --  $msg" -ForegroundColor DarkGray }
function Fail($msg)        { Write-Host "    X   $msg" -ForegroundColor Red; throw $msg }

# Same host table as Install-Bundle.ps1, plus each host's storage folder
# (HostStorage.AppFolder in the plugin).
$Hosts = @(
    [pscustomobject]@{
        Name       = 'AutoCAD'
        TargetRoot = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
        Processes  = @('acad', 'acadlt', 'accoreconsole')
        DataFolder = 'Acd.Mcp'
    }
    [pscustomobject]@{
        Name       = 'BricsCAD'
        TargetRoot = Join-Path $env:APPDATA 'Bricsys\ApplicationPlugins'
        Processes  = @('bricscad')
        DataFolder = 'Bcad.Mcp'
    }
)

function Get-RunningHost([string[]] $processNames) {
    $processNames |
        ForEach-Object { Get-Process -Name $_ -ErrorAction SilentlyContinue } |
        Where-Object { $_ } | Select-Object -First 1
}

# ─── bundles ────────────────────────────────────────────────────────────────

foreach ($hostRow in $Hosts) {
    Write-Step "$($hostRow.Name) bundle"
    $bundleTarget = Join-Path $hostRow.TargetRoot 'ACD-MCP.bundle'
    if (-not (Test-Path -LiteralPath $bundleTarget)) {
        Write-Skip2 "Bundle not installed"
        continue
    }
    $running = Get-RunningHost $hostRow.Processes
    if ($running -and -not $Force) {
        Fail "$($hostRow.Name) is running (PID $($running.Id)). Close it and re-run, or pass -Force."
    }
    if ($PSCmdlet.ShouldProcess($bundleTarget, "Remove bundle")) {
        Remove-Item -LiteralPath $bundleTarget -Recurse -Force
        Write-Ok "Removed $bundleTarget"
    }
}

# ─── purge user data ────────────────────────────────────────────────────────

if ($Purge) {
    Write-Step "Purge user content"
    foreach ($hostRow in $Hosts) {
        foreach ($dir in @(
            Join-Path $env:APPDATA       $hostRow.DataFolder
            Join-Path $env:LOCALAPPDATA  $hostRow.DataFolder
        )) {
            if (Test-Path -LiteralPath $dir) {
                if ($PSCmdlet.ShouldProcess($dir, "Recursively remove")) {
                    Remove-Item -LiteralPath $dir -Recurse -Force
                    Write-Ok "Removed $dir"
                }
            } else { Write-Skip2 "Not present: $dir" }
        }
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
