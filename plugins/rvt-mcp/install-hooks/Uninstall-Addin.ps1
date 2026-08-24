#Requires -Version 7.0
<#
.SYNOPSIS
  Remove the Rvt.Mcp Revit add-in installed by Install-Addin.ps1.

.DESCRIPTION
  Deletes both halves of the per-user install:
    %APPDATA%\Autodesk\Revit\Addins\<year>\Rvt.Mcp\
    %APPDATA%\Autodesk\Revit\Addins\<year>\Rvt.Mcp.addin

  Idempotent: reports and skips whatever is already gone.

  This removes the IN-PROCESS half only. Unregister the MCP bridge from your
  AI client separately (Claude Code: /plugin uninstall rvt-mcp).

.PARAMETER RevitYear
  Target Revit major version. Default: 2025.

.EXAMPLE
  pwsh .\Uninstall-Addin.ps1

.NOTES
  Windows-only. Run from pwsh 7+. Companion: Install-Addin.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [int] $RevitYear = 2025
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step($msg)  { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Skip2($msg) { Write-Host "    --  $msg" -ForegroundColor DarkGray }

$addinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitYear"
$targetDir = Join-Path $addinsDir 'Rvt.Mcp'
$manifest  = Join-Path $addinsDir 'Rvt.Mcp.addin'

Write-Host ''
Write-Host "RVT-MCP Revit add-in uninstaller (Revit $RevitYear)" -ForegroundColor White
Write-Host ''

Write-Step 'Removing manifest'
if (Test-Path -LiteralPath $manifest) {
    if ($PSCmdlet.ShouldProcess($manifest, 'Remove')) {
        Remove-Item -LiteralPath $manifest -Force
        Write-Ok "Removed $manifest"
    }
} else {
    Write-Skip2 "Not present: $manifest"
}

Write-Step 'Removing binaries'
if (Test-Path -LiteralPath $targetDir) {
    if ($PSCmdlet.ShouldProcess($targetDir, 'Remove recursively')) {
        Remove-Item -LiteralPath $targetDir -Recurse -Force
        Write-Ok "Removed $targetDir"
    }
} else {
    Write-Skip2 "Not present: $targetDir"
}

Write-Host ''
Write-Host 'Done. Restart Revit to complete removal.' -ForegroundColor Green
Write-Host ''
