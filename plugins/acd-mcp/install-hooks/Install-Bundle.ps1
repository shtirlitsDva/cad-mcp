#Requires -Version 7.0
<#
.SYNOPSIS
  Deploy the ACD-MCP plugin bundles: AutoCAD's to %APPDATA%\Autodesk\
  ApplicationPlugins\, BricsCAD's to %APPDATA%\Bricsys\ApplicationPlugins\.

.DESCRIPTION
  Pure bundle-deploy. Run this once per machine (and again on every
  upgrade) so AutoCAD and BricsCAD autoload the plugin DLLs at startup.
  Each host has its own bundle (its own build of Acd.Mcp.dll). Both are
  deployed; a host that is not installed simply never reads its folder.

  Idempotent. Per host, refuses to overwrite if that host is running, or if
  the on-disk bundle is newer than the source — unless -Force.

  Companion script: Install-Mcp.ps1 (registers acd-mcp with non-Claude-Code
  MCP clients). Claude Code users do that side via /plugin install.

.PARAMETER Force
  Overwrite a bundle even if on-disk is newer, and skip the host-running
  check.

.EXAMPLE
  pwsh .\Install-Bundle.ps1
  # Deploy both bundles from the sibling autocad-bundle/ and bricscad-bundle/.

.EXAMPLE
  pwsh .\Install-Bundle.ps1 -Force
  # Overwrite even if a host is running. Bundle copy may fail if the host
  # has the DLLs open — close it first whenever possible.

.NOTES
  Windows-only. Run from pwsh 7+.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ─── pretty printing ────────────────────────────────────────────────────────

function Write-Step($msg)  { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Skip2($msg) { Write-Host "    --  $msg" -ForegroundColor DarkGray }
function Write-Warn2($msg) { Write-Host "    !   $msg" -ForegroundColor Yellow }
function Fail($msg)        { Write-Host "    X   $msg" -ForegroundColor Red; throw $msg }

# ─── hosts ──────────────────────────────────────────────────────────────────

$scriptRoot = Split-Path -Parent $PSCommandPath
$pluginRoot = Split-Path -Parent $scriptRoot

# One row per host; everything host-specific lives here so the deploy below
# stays host-agnostic. Uninstall-Bundle.ps1 carries the same table.
$Hosts = @(
    [pscustomobject]@{
        Name       = 'AutoCAD'
        Source     = Join-Path $pluginRoot 'autocad-bundle\ACD-MCP.bundle'
        TargetRoot = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
        Processes  = @('acad', 'acadlt', 'accoreconsole')
    }
    [pscustomobject]@{
        Name       = 'BricsCAD'
        Source     = Join-Path $pluginRoot 'bricscad-bundle\ACD-MCP.bundle'
        TargetRoot = Join-Path $env:APPDATA 'Bricsys\ApplicationPlugins'
        Processes  = @('bricscad')
    }
)

# ─── helpers ────────────────────────────────────────────────────────────────

function Get-RunningHost([string[]] $processNames) {
    $processNames |
        ForEach-Object { Get-Process -Name $_ -ErrorAction SilentlyContinue } |
        Where-Object { $_ } | Select-Object -First 1
}

function Get-BundleVersion([string] $bundleDir) {
    $pkg = Join-Path $bundleDir 'PackageContents.xml'
    if (-not (Test-Path -LiteralPath $pkg)) { return $null }
    try {
        $xml = [xml](Get-Content -LiteralPath $pkg -Raw)
        return [version]$xml.ApplicationPackage.AppVersion
    } catch { return $null }
}

# ─── main flow ──────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "ACD-MCP bundle installer" -ForegroundColor White
Write-Host ""

foreach ($hostRow in $Hosts) {
    $source = [System.IO.Path]::GetFullPath($hostRow.Source)
    $target = Join-Path $hostRow.TargetRoot 'ACD-MCP.bundle'

    Write-Step "$($hostRow.Name) bundle"
    Write-Host "    Source: $source"
    Write-Host "    Target: $target"

    if (-not (Test-Path -LiteralPath $source)) {
        Fail "Bundle source not found: $source"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $source 'PackageContents.xml'))) {
        Fail "Bundle source has no PackageContents.xml: $source"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $source 'Contents'))) {
        Fail "Bundle source has no Contents/ folder: $source"
    }
    # Require at least one .dll — the in-repo Contents/ only has .gitkeep until
    # scripts\Build-Release.ps1 populates it. Without this guard the installer
    # happily deploys a non-functional bundle.
    $dllCount = @(Get-ChildItem (Join-Path $source 'Contents') -File -Filter '*.dll' -ErrorAction SilentlyContinue).Count
    if ($dllCount -eq 0) {
        Fail "Bundle Contents/ has no .dll files. Build first: pwsh scripts\Build-Release.ps1"
    }

    $sourceVer   = Get-BundleVersion $source
    $existingVer = if (Test-Path -LiteralPath $target) { Get-BundleVersion $target } else { $null }

    if ($existingVer -and -not $Force -and $sourceVer -le $existingVer) {
        Write-Skip2 "Installed bundle ($existingVer) is newer or equal to source ($sourceVer). -Force to overwrite."
        continue
    }

    $running = Get-RunningHost $hostRow.Processes
    if ($running -and -not $Force) {
        Fail "$($hostRow.Name) is running (PID $($running.Id)). Close it and re-run, or pass -Force."
    }
    if ($running -and $Force) {
        Write-Warn2 "$($hostRow.Name) is running but -Force was passed. Bundle copy may fail with locked-file errors."
    }

    if ($PSCmdlet.ShouldProcess($target, "Deploy ACD-MCP.bundle v$sourceVer")) {
        if (Test-Path -LiteralPath $target) {
            Remove-Item -LiteralPath $target -Recurse -Force
        }
        New-Item -ItemType Directory -Path $hostRow.TargetRoot -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $hostRow.TargetRoot -Recurse -Force
        Write-Ok "Deployed bundle v$sourceVer to $target"
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "Next:" -ForegroundColor White
Write-Host "  1. Launch AutoCAD 2025+ or BricsCAD V26. The bundle autoloads (look for 'ACDMCP' commands)."
Write-Host "  2. Run ACDMCP_START to open the named pipe."
Write-Host "  3. Connect with your AI client. Claude Code: /plugin install acd-mcp@cad-mcp."
Write-Host "                                  Others: pwsh .\Install-Mcp.ps1"
Write-Host ""
