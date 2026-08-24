#Requires -Version 7.0
<#
.SYNOPSIS
  Install the Rvt.Mcp Revit add-in from an extracted rvt-mcp release zip.

.DESCRIPTION
  The Revit half of rvt-mcp. Copies the pre-built add-in payload shipped in
  the zip (revit-addin/) to the standard per-user add-in location and writes
  the .addin manifest:

    %APPDATA%\Autodesk\Revit\Addins\<year>\Rvt.Mcp\        <- binaries
    %APPDATA%\Autodesk\Revit\Addins\<year>\Rvt.Mcp.addin   <- manifest

  Idempotent: re-running replaces both.

  This installs the IN-PROCESS half only. The LLM half (the MCP bridge that
  exposes revit_script_execute) installs through your AI client:
    Claude Code — /plugin install rvt-mcp@cad-mcp
    others      — point the client at bin\Rvt.Mcp.Bridge.exe next to this script
  The two halves meet on the rvt-mcp-{pid} named pipe.

  Developers working from a clone should use scripts\Deploy-RevitAddin.ps1
  instead — it builds from source and installs in one step.

.PARAMETER RevitYear
  Target Revit major version. Default: 2025. Revit 2024 and earlier run on
  .NET Framework and are not supported.

.PARAMETER PayloadPath
  Folder holding the built add-in. Defaults to ..\revit-addin relative to
  this script (the layout produced by Build-Release.ps1).

.EXAMPLE
  pwsh .\Install-Addin.ps1
  # Install for Revit 2025 from the zip's revit-addin\ folder.

.EXAMPLE
  pwsh .\Install-Addin.ps1 -RevitYear 2026
  # Install for Revit 2026.

.NOTES
  Windows-only (Revit is Windows-only). Run from pwsh 7+.
  Companion: Uninstall-Addin.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [int]    $RevitYear = 2025,
    [string] $PayloadPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step($msg)  { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Warn2($msg) { Write-Host "    !   $msg" -ForegroundColor Yellow }
function Fail($msg)        { Write-Host "    X   $msg" -ForegroundColor Red; throw $msg }

if ($RevitYear -lt 2025) {
    Fail "Rvt.Mcp targets Revit 2025+ (net8). Revit $RevitYear runs on .NET Framework — not supported."
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$pluginRoot = Split-Path -Parent $scriptRoot

if (-not $PayloadPath) { $PayloadPath = Join-Path $pluginRoot 'revit-addin' }
$PayloadPath = [System.IO.Path]::GetFullPath($PayloadPath)

Write-Host ''
Write-Host "RVT-MCP Revit add-in installer" -ForegroundColor White
Write-Host "  Revit year: $RevitYear"
Write-Host "  Payload:    $PayloadPath"
Write-Host ''

if (-not (Test-Path -LiteralPath (Join-Path $PayloadPath 'Rvt.Mcp.Loader.dll'))) {
    Fail "Rvt.Mcp.Loader.dll not found in $PayloadPath. Extract the full release zip, or pass -PayloadPath."
}

$addinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear"
$targetDir = Join-Path $addinsDir 'Rvt.Mcp'
$manifest  = Join-Path $addinsDir 'Rvt.Mcp.addin'

Write-Step "Installing to $targetDir"
if ($PSCmdlet.ShouldProcess($targetDir, 'Replace add-in binaries')) {
    if (Test-Path -LiteralPath $targetDir) { Remove-Item -LiteralPath $targetDir -Recurse -Force }
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Copy-Item (Join-Path $PayloadPath '*') $targetDir -Recurse -Force
    Write-Ok "Copied $((Get-ChildItem $targetDir -File -Recurse).Count) files"
}

# The <Assembly> path is RELATIVE to the Addins\<year> folder, so the whole
# tree stays movable and matches what Deploy-RevitAddin.ps1 writes. The
# AddInId must stay stable across versions — Revit keys enable/disable state
# on it, so changing it silently re-enables an add-in the user turned off.
$manifestXml = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
    <AddIn Type="Application">
        <Name>Rvt.Mcp</Name>
        <Assembly>Rvt.Mcp/Rvt.Mcp.Loader.dll</Assembly>
        <FullClassName>Rvt.Mcp.Loader.LoaderApp</FullClassName>
        <AddInId>f4ac6a14-27e4-442f-a254-300c83e2b55a</AddInId>
        <VendorId>DVRL</VendorId>
        <VendorDescription>Norsyn, https://github.com/shtirlitsDva/cad-mcp</VendorDescription>
    </AddIn>
</RevitAddIns>
"@

Write-Step "Writing manifest $manifest"
if ($PSCmdlet.ShouldProcess($manifest, 'Write .addin manifest')) {
    Set-Content -LiteralPath $manifest -Value $manifestXml -Encoding utf8
    Write-Ok "Manifest written"
}

$bridge = Join-Path $pluginRoot 'bin\Rvt.Mcp.Bridge.exe'
if (-not (Test-Path -LiteralPath $bridge)) {
    Write-Warn2 "Rvt.Mcp.Bridge.exe not found at $bridge — the LLM half will not start."
}

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
Write-Host 'Next:' -ForegroundColor White
Write-Host "  1. Restart Revit $RevitYear so it loads the add-in."
Write-Host '  2. Register the MCP bridge with your AI client:'
Write-Host '       Claude Code: /plugin install rvt-mcp@cad-mcp'
Write-Host "       others:      point the client at $bridge"
Write-Host ''
