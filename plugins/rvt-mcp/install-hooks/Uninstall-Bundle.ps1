#Requires -Version 7.0
<#
.SYNOPSIS
  Remove the RVT-MCP Revit plugin bundle installed by Install-Bundle.ps1.

.DESCRIPTION
  Deletes %APPDATA%\Autodesk\ApplicationPlugins\RVT-MCP.bundle.

  Idempotent: reports and skips whatever is already gone.

  This removes the IN-PROCESS half only. Unregister the MCP bridge from your
  AI client separately (Claude Code: /plugin uninstall rvt-mcp@cad-mcp).

.PARAMETER Purge
  Also delete rvt-mcp's user data under %LOCALAPPDATA%\Rvt.Mcp\ (log file and
  any session state). Off by default — an uninstall should not destroy data a
  reinstall would want.

.EXAMPLE
  pwsh .\Uninstall-Bundle.ps1

.NOTES
  Windows-only. Run from pwsh 7+. Companion: Install-Bundle.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [switch] $Purge
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step($msg)  { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "    OK  $msg" -ForegroundColor Green }
function Write-Skip2($msg) { Write-Host "    --  $msg" -ForegroundColor DarkGray }
function Write-Warn2($msg) { Write-Host "    !   $msg" -ForegroundColor Yellow }

$bundleTarget = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\RVT-MCP.bundle'
$userData     = Join-Path $env:LOCALAPPDATA 'Rvt.Mcp'

Write-Host ""
Write-Host "RVT-MCP bundle uninstaller" -ForegroundColor White
Write-Host ""

Write-Step "Revit bundle"
if (Test-Path -LiteralPath $bundleTarget) {
    if ($PSCmdlet.ShouldProcess($bundleTarget, 'Remove recursively')) {
        Remove-Item -LiteralPath $bundleTarget -Recurse -Force
        Write-Ok "Removed $bundleTarget"
    }
} else {
    Write-Skip2 "Not present: $bundleTarget"
}

# The developer install path (scripts\Deploy-RevitAddin.ps1) registers into the
# per-user Addins folder instead. Point at it rather than deleting it silently —
# a developer's working install is not this script's to remove.
$legacy = Get-ChildItem (Join-Path $env:APPDATA 'Autodesk\Revit\Addins') -Filter 'Rvt.Mcp.addin' -Recurse -ErrorAction SilentlyContinue
if ($legacy) {
    Write-Warn2 "A per-user add-in registration still exists and will keep loading Rvt.Mcp:"
    $legacy | ForEach-Object { Write-Warn2 "  $($_.FullName)" }
    Write-Warn2 "Remove it with: pwsh scripts\Deploy-RevitAddin.ps1 -Remove"
}

Write-Step "User data"
if ($Purge) {
    if (Test-Path -LiteralPath $userData) {
        if ($PSCmdlet.ShouldProcess($userData, 'Remove recursively')) {
            Remove-Item -LiteralPath $userData -Recurse -Force
            Write-Ok "Purged $userData"
        }
    } else {
        Write-Skip2 "Not present: $userData"
    }
} elseif (Test-Path -LiteralPath $userData) {
    Write-Skip2 "Kept $userData (-Purge to delete)"
} else {
    Write-Skip2 "No user data"
}

Write-Host ""
Write-Host "Done. Restart Revit to complete removal." -ForegroundColor Green
Write-Host ""
