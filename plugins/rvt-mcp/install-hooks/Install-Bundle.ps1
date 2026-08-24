#Requires -Version 7.0
<#
.SYNOPSIS
  Deploy the RVT-MCP Revit plugin bundle to %APPDATA%\Autodesk\
  ApplicationPlugins\.

.DESCRIPTION
  Pure bundle-deploy. Run this once per machine (and again on every upgrade)
  so Revit autoloads the plugin at startup.

  Revit reads the same %APPDATA%\Autodesk\ApplicationPlugins\ bundle location
  as AutoCAD. The bundle's PackageContents.xml points at Contents\Rvt.Mcp.addin,
  and Revit loads the add-in from there — so there is nothing to write into
  %APPDATA%\Autodesk\Revit\Addins\<year>\, and no -RevitYear to choose. Which
  Revit versions are accepted is declared by the bundle's RuntimeRequirements
  (currently R2025-R2026).

  Idempotent. Refuses to overwrite if Revit is running, or if the on-disk
  bundle is newer than the source — unless -Force.

  Companion: Uninstall-Bundle.ps1. This installs the IN-PROCESS half only;
  the LLM half (the MCP bridge exposing revit_script_execute) installs
  through your AI client — Claude Code: /plugin install rvt-mcp@cad-mcp;
  others: point the client at bin\Rvt.Mcp.Bridge.exe next to this script.
  The two halves meet on the rvt-mcp-{pid} named pipe.

.PARAMETER BundleSource
  Path to the RVT-MCP.bundle source folder. Defaults to
  ..\revit-bundle\RVT-MCP.bundle relative to this script.

.PARAMETER Force
  Overwrite even if on-disk is newer, and skip the Revit-running check.

.EXAMPLE
  pwsh .\Install-Bundle.ps1

.NOTES
  Windows-only. Run from pwsh 7+.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $BundleSource,
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

# ─── paths ──────────────────────────────────────────────────────────────────

$scriptRoot = Split-Path -Parent $PSCommandPath
$pluginRoot = Split-Path -Parent $scriptRoot

if (-not $BundleSource) { $BundleSource = Join-Path $pluginRoot 'revit-bundle\RVT-MCP.bundle' }
$BundleSource = [System.IO.Path]::GetFullPath($BundleSource)

$bundleTargetRoot = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
$bundleTarget     = Join-Path $bundleTargetRoot 'RVT-MCP.bundle'

# ─── helpers ────────────────────────────────────────────────────────────────

function Test-RevitRunning {
    Get-Process -Name 'Revit' -ErrorAction SilentlyContinue | Select-Object -First 1
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
Write-Host "RVT-MCP bundle installer" -ForegroundColor White
Write-Host "  Source: $BundleSource"
Write-Host "  Target: $bundleTarget"
Write-Host ""

Write-Step "Revit bundle"

if (-not (Test-Path -LiteralPath $BundleSource)) {
    Fail "Bundle source not found: $BundleSource"
}
if (-not (Test-Path -LiteralPath (Join-Path $BundleSource 'PackageContents.xml'))) {
    Fail "Bundle source has no PackageContents.xml: $BundleSource"
}
$contents = Join-Path $BundleSource 'Contents'
if (-not (Test-Path -LiteralPath $contents)) {
    Fail "Bundle source has no Contents/ folder: $BundleSource"
}
# The .addin is what PackageContents.xml's ComponentEntry actually points at —
# without it Revit silently loads nothing.
if (-not (Test-Path -LiteralPath (Join-Path $contents 'Rvt.Mcp.addin'))) {
    Fail "Bundle Contents/ has no Rvt.Mcp.addin. Build first: pwsh scripts\Build-Release.ps1 -Product rvt"
}
# In-repo Contents/ holds only the .addin until Build-Release.ps1 populates it.
# Without this guard the installer happily deploys a non-functional bundle.
if (-not (Test-Path -LiteralPath (Join-Path $contents 'Rvt.Mcp.Loader.dll'))) {
    Fail "Bundle Contents/ has no Rvt.Mcp.Loader.dll. Build first: pwsh scripts\Build-Release.ps1 -Product rvt"
}

$sourceVer   = Get-BundleVersion $BundleSource
$existingVer = if (Test-Path -LiteralPath $bundleTarget) { Get-BundleVersion $bundleTarget } else { $null }

if ($existingVer -and -not $Force) {
    if ($sourceVer -le $existingVer) {
        Write-Skip2 "Installed bundle ($existingVer) is newer or equal to source ($sourceVer). -Force to overwrite."
        Write-Host ""
        Write-Host "Done." -ForegroundColor Green
        return
    }
}

$running = Test-RevitRunning
if ($running -and -not $Force) {
    Fail "Revit is running (PID $($running.Id)). Close it and re-run, or pass -Force."
}
if ($running -and $Force) {
    Write-Warn2 "Revit is running but -Force was passed. Bundle copy may fail with locked-file errors."
}

if ($PSCmdlet.ShouldProcess($bundleTarget, "Deploy RVT-MCP.bundle v$sourceVer")) {
    if (Test-Path -LiteralPath $bundleTarget) {
        Remove-Item -LiteralPath $bundleTarget -Recurse -Force
    }
    New-Item -ItemType Directory -Path $bundleTargetRoot -Force | Out-Null
    Copy-Item -LiteralPath $BundleSource -Destination $bundleTargetRoot -Recurse -Force
    Write-Ok "Deployed bundle v$sourceVer to $bundleTarget"
}

# A leftover per-user add-in from scripts\Deploy-RevitAddin.ps1 (the developer
# install path) would load a SECOND copy of the engine alongside this one.
$legacy = Get-ChildItem (Join-Path $env:APPDATA 'Autodesk\Revit\Addins') -Filter 'Rvt.Mcp.addin' -Recurse -ErrorAction SilentlyContinue
if ($legacy) {
    Write-Warn2 "A per-user add-in registration also exists and will load a second copy:"
    $legacy | ForEach-Object { Write-Warn2 "  $($_.FullName)" }
    Write-Warn2 "Remove it with: pwsh scripts\Deploy-RevitAddin.ps1 -Remove"
}

$bridge = Join-Path $pluginRoot 'bin\Rvt.Mcp.Bridge.exe'
if (-not (Test-Path -LiteralPath $bridge)) {
    Write-Warn2 "Rvt.Mcp.Bridge.exe not found at $bridge — the LLM half will not start."
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "Next:" -ForegroundColor White
Write-Host "  1. Launch Revit 2025+. The bundle autoloads."
Write-Host "  2. Connect with your AI client. Claude Code: /plugin install rvt-mcp@cad-mcp."
Write-Host "                                  Others: point it at $bridge"
Write-Host ""
