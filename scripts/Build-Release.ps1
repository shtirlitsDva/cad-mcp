#Requires -Version 7.0
<#
.SYNOPSIS
  Build, assemble, and (optionally) publish a release of acd-mcp, rvt-mcp, or both.

.DESCRIPTION
  This repo ships TWO independently versioned products out of one tree:

    acd-mcp  — C# REPL + batch runner inside AutoCAD / Civil 3D 2025+
    rvt-mcp  — C# script session inside Revit 2025+

  Each has its own plugin.json version and its own release tag namespace
  (acd-v<X.Y.Z> / rvt-v<X.Y.Z>), because they move at different speeds. This
  script is the release pipeline for either or both. It runs locally or in CI:
  AutoCAD, Civil 3D and Revit reference assemblies all come from NuGet
  (ExcludeAssets=runtime), so no Autodesk product needs to be installed on
  the build machine.

  What it does, per selected product:
    1. dotnet publish the bridge → plugins/<product>/bin/ (THE committed
       binaries — /plugin install launches the bridge from here via
       .mcp.json's ${CLAUDE_PLUGIN_ROOT}/bin/<Bridge>.exe, and Codex's
       codex.mcp.json's ./bin/<Bridge>.exe).
    2. dotnet build CI.slnf → the in-process plugin assemblies + deps.
    3. Assemble Deploy/<product>-plugin/ from plugins/<product>/ plus the
       host-specific payload (AutoCAD .bundle / Revit add-in folder).
    4. Zip it to Deploy/<product>-plugin-v<X.Y.Z>.zip
    5. (Optional) Create the GH Release and upload the zip.

  After running locally, REMEMBER TO commit the refreshed bridge binaries —
  without that, /plugin install pulls stale binaries from master:
    git add plugins/<product>/bin/
    git commit -m "Refresh <product> bridge binary for v<X.Y.Z>"
    git tag <acd|rvt>-v<X.Y.Z>
    git push --tags
  CI then uploads the zip to the GitHub Release (.github/workflows/ci.yml).

.PARAMETER Product
  Which product to release: acd, rvt, or both. Default: both.

.PARAMETER Configuration
  dotnet build configuration. Default: Release.

.PARAMETER Version
  Override the version. Only valid with a single -Product (the two products
  version independently, so one number cannot mean both). Defaults to the
  "version" field in that product's plugin.json.

.PARAMETER Publish
  Also create GitHub Release tag "<product>-v<Version>" and upload the zip.
  Requires an authenticated `gh` CLI.

.PARAMETER SkipBuild
  Skip dotnet build/publish — only re-assemble + zip from existing bin/.

.EXAMPLE
  pwsh ./scripts/Build-Release.ps1
  # Build + assemble + zip BOTH products. Results in Deploy/.

.EXAMPLE
  pwsh ./scripts/Build-Release.ps1 -Product rvt -Publish
  # Revit only, then `gh release create rvt-vX.Y.Z` and upload the zip.
#>
[CmdletBinding()]
param(
    [ValidateSet('acd', 'rvt', 'both')]
    [string]   $Product = 'both',
    [string]   $Configuration = 'Release',
    [string]   $Version,
    [switch]   $Publish,
    [switch]   $SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $repoRoot

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  ✓ $msg" -ForegroundColor Green }
function Fail($msg)       { Write-Host "  ✗ $msg" -ForegroundColor Red; throw $msg }

$products = if ($Product -eq 'both') { @('acd', 'rvt') } else { @($Product) }

if ($Version -and $products.Count -gt 1) {
    Fail "-Version requires a single -Product. acd-mcp and rvt-mcp version independently."
}

# ─── per-product definitions ────────────────────────────────────────────────
# Everything that differs between the two releases lives here, so the staging
# and publishing code below stays product-agnostic.
$Definitions = @{
    acd = @{
        PluginName   = 'acd-mcp'
        TagPrefix    = 'acd'
        ReleaseTitle = 'ACD-MCP'
        BridgeProj   = 'src/Autocad/Acd.Mcp.Bridge/Acd.Mcp.Bridge.csproj'
        BridgeExe    = 'Acd.Mcp.Bridge.exe'
        # In-process plugin whose build output is the host payload.
        HostProj     = 'src/Autocad/Acd.Mcp/Acd.Mcp.csproj'
        HostOutDir   = 'src/Autocad/Acd.Mcp/bin'
    }
    rvt = @{
        PluginName   = 'rvt-mcp'
        TagPrefix    = 'rvt'
        ReleaseTitle = 'RVT-MCP'
        BridgeProj   = 'src/Revit/Rvt.Mcp.Bridge/Rvt.Mcp.Bridge.csproj'
        BridgeExe    = 'Rvt.Mcp.Bridge.exe'
        # The loader pulls the engine in by ProjectReference, so one build
        # produces the complete add-in folder (loader + engine + Roslyn).
        HostProj     = 'src/Revit/Rvt.Mcp.Loader/Rvt.Mcp.Loader.csproj'
        HostOutDir   = 'src/Revit/Rvt.Mcp.Loader/bin'
    }
}

# ─── shared build (both products come out of the same filter) ───────────────

if (-not $SkipBuild) {
    Write-Step "dotnet build CI.slnf"
    dotnet build 'CI.slnf' -c $Configuration -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { Fail "Solution build failed" }
    Write-Ok "Solution built"
}

$zips = @()

foreach ($key in $products) {
    $def         = $Definitions[$key]
    $pluginName  = $def.PluginName
    $pluginRoot  = Join-Path $repoRoot "plugins/$pluginName"
    $repoBinDir  = Join-Path $pluginRoot 'bin'

    # ─── version ────────────────────────────────────────────────────────────
    $ver = $Version
    if (-not $ver) {
        $manifest = Get-Content (Join-Path $pluginRoot '.claude-plugin/plugin.json') -Raw | ConvertFrom-Json
        $ver = $manifest.version
    }
    if (-not $ver) { Fail "Could not determine version for $pluginName." }

    Write-Host ''
    Write-Step "$pluginName v$ver ($Configuration)"

    $pluginStage = Join-Path $repoRoot "Deploy/$pluginName-plugin"
    $zipPath     = Join-Path $repoRoot "Deploy/$pluginName-plugin-v$ver.zip"

    if (Test-Path $pluginStage) { Remove-Item $pluginStage -Recurse -Force }
    New-Item -ItemType Directory -Path $pluginStage -Force | Out-Null

    # ─── bridge → committed plugins/<product>/bin/ ──────────────────────────
    if (-not $SkipBuild) {
        Write-Step "dotnet publish $($def.BridgeExe) → plugins/$pluginName/bin/"
        # Wipe-then-publish so transitive deps dropped by a newer build don't
        # linger in the committed folder.
        if (Test-Path $repoBinDir) { Get-ChildItem $repoBinDir -File | Remove-Item -Force }
        dotnet publish $def.BridgeProj `
            -c $Configuration `
            -o $repoBinDir `
            --self-contained false `
            -p:PublishSingleFile=false
        if ($LASTEXITCODE -ne 0) { Fail "$pluginName bridge publish failed" }
        # Debug symbols bloat the committed bin/ without helping users. dotnet
        # has no publish flag to skip the pdb cleanly, so prune after the fact.
        Get-ChildItem $repoBinDir -File -Filter '*.pdb' | Remove-Item -Force
        Write-Ok "Bridge published to $repoBinDir"
    }

    # ─── stage plugin layout ────────────────────────────────────────────────
    Write-Step "Assembling plugin layout at $pluginStage"

    # Everything inside plugins/<product>/ IS the plugin: .claude-plugin/,
    # .codex-plugin/, .mcp.json, codex.mcp.json, skills/, install-hooks/, bin/.
    Copy-Item (Join-Path $pluginRoot '*') $pluginStage -Recurse
    Copy-Item 'README.md' $pluginStage
    $productDoc = Join-Path $repoRoot "docs/$pluginName.md"
    if (Test-Path $productDoc) { Copy-Item $productDoc $pluginStage }
    Write-Ok "Plugin metadata + bridge staged"

    # ─── host payload (the in-process half) ─────────────────────────────────
    $hostBuildOut = Join-Path $repoRoot "$($def.HostOutDir)/$Configuration"
    if (-not (Test-Path $hostBuildOut)) {
        Fail "Host build output not found at $hostBuildOut. Did dotnet build succeed?"
    }

    if ($key -eq 'acd') {
        # AutoCAD Autoloader bundle: manifest + Contents/ populated from the
        # plugin's build output (Acd.Mcp.dll + the transitive deps MSBuild
        # already arranged).
        $bundleSrc = Join-Path $repoRoot 'autocad-bundle/ACD-MCP.bundle'
        $bundleDst = Join-Path $pluginStage 'autocad-bundle/ACD-MCP.bundle'
        New-Item -ItemType Directory -Path $bundleDst -Force | Out-Null
        Copy-Item (Join-Path $bundleSrc 'PackageContents.xml') $bundleDst

        $contentsDst = Join-Path $bundleDst 'Contents'
        New-Item -ItemType Directory -Path $contentsDst -Force | Out-Null
        Get-ChildItem $hostBuildOut -File |
            Where-Object { $_.Extension -in '.dll', '.pdb', '.json' } |
            Copy-Item -Destination $contentsDst -Force
        Write-Ok "AutoCAD bundle staged ($((Get-ChildItem $contentsDst).Count) files)"
    }
    else {
        # Revit add-in payload: the loader's whole output folder. The .addin
        # manifest is written at install time by install-hooks/Install-Addin.ps1,
        # which needs the target Revit year — it cannot be baked in here.
        $addinDst = Join-Path $pluginStage 'revit-addin'
        New-Item -ItemType Directory -Path $addinDst -Force | Out-Null
        Copy-Item (Join-Path $hostBuildOut '*') $addinDst -Recurse -Force
        if (-not (Test-Path (Join-Path $addinDst 'Rvt.Mcp.Loader.dll'))) {
            Fail "Rvt.Mcp.Loader.dll missing from staged add-in payload."
        }
        Write-Ok "Revit add-in staged ($((Get-ChildItem $addinDst -File).Count) files)"
    }

    # ─── zip ────────────────────────────────────────────────────────────────
    Write-Step "Zipping to $zipPath"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $pluginStage '*') -DestinationPath $zipPath -Force
    Write-Ok "Wrote $zipPath ($([math]::Round(((Get-Item $zipPath).Length / 1MB), 2)) MB)"

    $zips += [pscustomobject]@{
        Product = $pluginName
        Version = $ver
        Tag     = "$($def.TagPrefix)-v$ver"
        Title   = "$($def.ReleaseTitle) v$ver"
        Path    = $zipPath
        BinDir  = "plugins/$pluginName/bin"
    }
}

# ─── publish ────────────────────────────────────────────────────────────────

if ($Publish) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Fail "gh CLI not found. Install from https://cli.github.com or drop -Publish."
    }
    foreach ($z in $zips) {
        Write-Step "gh release create $($z.Tag)"
        gh release view $z.Tag 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Release $($z.Tag) already exists — uploading asset only" -ForegroundColor Yellow
            gh release upload $z.Tag $z.Path --clobber
        } else {
            gh release create $z.Tag $z.Path --title $z.Title --generate-notes
        }
        Write-Ok "Published $($z.Tag)"
    }
}

# ─── summary ────────────────────────────────────────────────────────────────

Write-Host ''
foreach ($z in $zips) {
    Write-Host "Release artifact: $($z.Path)" -ForegroundColor Green
}
Write-Host ''

# Nudge the maintainer if any committed bin/ drifted from git. Without a
# refresh-and-commit, /plugin install pulls stale bridges from master.
if (Get-Command git -ErrorAction SilentlyContinue) {
    foreach ($z in $zips) {
        $dirtyBin = git status --porcelain -- $z.BinDir 2>$null
        if ($dirtyBin) {
            Write-Host "  ! $($z.BinDir)/ has uncommitted changes — commit so /plugin install picks up the refresh:" -ForegroundColor Yellow
            Write-Host "    git add $($z.BinDir)/"
            Write-Host "    git commit -m `"Refresh $($z.Product) bridge binary for v$($z.Version)`""
            Write-Host "    git tag $($z.Tag) && git push --tags"
            Write-Host ''
        }
    }
}

Write-Host "Users install with one of:" -ForegroundColor White
Write-Host "  Claude Code: /plugin marketplace add https://github.com/shtirlitsDva/cad-mcp"
Write-Host "               /plugin install acd-mcp@cad-mcp      # AutoCAD / Civil 3D"
Write-Host "               /plugin install rvt-mcp@cad-mcp      # Revit"
Write-Host "  Others:      Download the product zip, extract, then:"
Write-Host "                 acd-mcp: pwsh install-hooks\Install-Bundle.ps1   # AutoCAD bundle"
Write-Host "                          pwsh install-hooks\Install-Mcp.ps1      # Codex/Copilot/Claude Desktop"
Write-Host "                 rvt-mcp: pwsh install-hooks\Install-Addin.ps1    # Revit add-in"
Write-Host "                          (non-Claude clients: point them at bin\Rvt.Mcp.Bridge.exe)"
Write-Host ''
