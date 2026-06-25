#requires -Version 5.1
<#
.SYNOPSIS
    Builds a versioned NuGet release of BlazorDX and keeps a copy of every version.

.DESCRIPTION
    For a given version this script produces, under <OutputRoot>\<Version>\:

        packages\                    every *.nupkg + *.snupkg (symbol) package
        BlazorDX-<Version>-src.zip   a source snapshot (build outputs excluded)
        manifest.json                version, UTC build time, git commit, SHA-256 of each file

    Each run writes to its own <Version> folder, so previously built releases are
    retained side by side. Re-running an existing version fails unless -Force is given,
    which protects an already-archived release from being clobbered by accident.

    The package version is passed to `dotnet pack` via -p:Version, so Directory.Build.props
    is never modified and the working tree stays clean.

.PARAMETER Version
    Package/assembly version, e.g. 0.2.0 or 1.0.0-rc.1. Defaults to the <Version>
    in Directory.Build.props.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER OutputRoot
    Root folder that accumulates versioned releases.
    Default: <repo>\artifacts\releases  (artifacts\ is git-ignored).

.PARAMETER Test
    Run `dotnet test` before packing; abort the release if any test fails.

.PARAMETER Clean
    Run `dotnet clean` and delete bin/obj before packing (a from-scratch build).

.PARAMETER Force
    Overwrite an existing <OutputRoot>\<Version> folder instead of failing.

.EXAMPLE
    .\Build-Release.ps1 -Version 0.2.0

.EXAMPLE
    .\Build-Release.ps1 -Version 1.0.0-rc.1 -Test -Clean

.EXAMPLE
    .\Build-Release.ps1 -Version 0.3.0 -OutputRoot \\fileserver\nuget\BlazorDX
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = 'Release',
    [string]$OutputRoot,
    [switch]$Test,
    [switch]$Clean,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# --- Locate the repository (this script lives at the repo root) ------------------
$RepoRoot  = $PSScriptRoot
$Solution  = Join-Path $RepoRoot 'BlazorDX.slnx'
$PropsFile = Join-Path $RepoRoot 'Directory.Build.props'

if (-not (Test-Path $Solution)) {
    throw "BlazorDX.slnx not found beside this script ($RepoRoot). Run it from the repo root."
}

# --- Resolve the version (parameter overrides Directory.Build.props) -------------
if ([string]::IsNullOrWhiteSpace($Version)) {
    $propsText = Get-Content -Path $PropsFile -Raw
    if ($propsText -match '<Version>\s*([^<]+?)\s*</Version>') {
        $Version = $Matches[1].Trim()
        Write-Host "Using version from Directory.Build.props: $Version"
    }
    else {
        throw "No -Version supplied and no <Version> found in $PropsFile."
    }
}

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$') {
    throw "Version '$Version' is not valid SemVer (e.g. 1.2.3 or 1.2.3-beta.1)."
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $RepoRoot 'artifacts\releases'
}

$ReleaseDir  = Join-Path $OutputRoot $Version
$PackagesDir = Join-Path $ReleaseDir 'packages'

# --- Guard against overwriting an archived version -------------------------------
if (Test-Path $ReleaseDir) {
    if (-not $Force) {
        throw "Release folder already exists: $ReleaseDir`n" +
              "Pick a new -Version, or pass -Force to rebuild and overwrite it."
    }
    Write-Warning "Overwriting existing release folder: $ReleaseDir"
    Remove-Item -Path $ReleaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $PackagesDir -Force | Out-Null

Write-Host ''
Write-Host '=== BlazorDX release build ===' -ForegroundColor Cyan
Write-Host "  Version       : $Version"
Write-Host "  Configuration : $Configuration"
Write-Host "  Output        : $ReleaseDir"
Write-Host ''

# --- Optional clean (from-scratch build) -----------------------------------------
if ($Clean) {
    Write-Host 'Cleaning bin/obj...' -ForegroundColor Yellow
    & dotnet clean $Solution -c $Configuration --nologo | Out-Null
    Get-ChildItem -Path $RepoRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { ($_.Name -eq 'bin' -or $_.Name -eq 'obj') -and $_.FullName -notlike '*\artifacts\*' } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

# --- Optional test gate ----------------------------------------------------------
if ($Test) {
    Write-Host 'Running tests...' -ForegroundColor Yellow
    & dotnet test $Solution --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed (exit $LASTEXITCODE). Release aborted."
    }
}

# --- Pack every packable library to the version's packages folder ----------------
# -p:Version overrides Directory.Build.props without editing it. Projects with
# IsPackable=false (the demo, tests, Rust/TS build-only tiers) are skipped.
Write-Host 'Packing NuGet packages...' -ForegroundColor Yellow
& dotnet pack $Solution `
    --configuration $Configuration `
    --output $PackagesDir `
    -p:Version=$Version `
    -p:ContinuousIntegrationBuild=true `
    --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed (exit $LASTEXITCODE). No release written."
}

$packages = @(Get-ChildItem -Path $PackagesDir -Filter '*.nupkg' | Sort-Object Name)
if ($packages.Count -eq 0) {
    throw "No .nupkg files were produced in $PackagesDir."
}
Write-Host "  Packed $($packages.Count) package(s)." -ForegroundColor Green

# --- Archive a source snapshot (build outputs and secrets excluded) --------------
Write-Host 'Archiving source...' -ForegroundColor Yellow
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('blazordx-src-' + [guid]::NewGuid().ToString('N'))

# Directory names excluded anywhere in the tree, plus the absolute output root so a
# release never zips itself. .env / *.env keep deploy secrets out of the archive.
$excludeDirs = @('bin', 'obj', '.git', '.vs', '.idea', 'node_modules', 'target',
                 'artifacts', 'TestResults', '.claude', 'Backup', $OutputRoot)
$excludeFiles = @('*.user', '*.tsbuildinfo', '.env', '*.env')

& robocopy $RepoRoot $staging /MIR /XD @excludeDirs /XF @excludeFiles /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "robocopy failed to stage the source (exit $LASTEXITCODE)."
}
$global:LASTEXITCODE = 0   # robocopy uses 0-7 for success; reset so later checks are clean

$srcZip = Join-Path $ReleaseDir "BlazorDX-$Version-src.zip"
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $srcZip -CompressionLevel Optimal
Remove-Item -Path $staging -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "  Source zipped: $([System.IO.Path]::GetFileName($srcZip))" -ForegroundColor Green

# --- Manifest: version, build time, git commit, and SHA-256 of every artifact ----
$commit = ''; $branch = ''
try { $commit = "$(& git -C $RepoRoot rev-parse HEAD 2>$null)".Trim() } catch { }
try { $branch = "$(& git -C $RepoRoot rev-parse --abbrev-ref HEAD 2>$null)".Trim() } catch { }

$manifest = [ordered]@{
    product       = 'BlazorDX'
    version       = $Version
    configuration = $Configuration
    builtUtc      = (Get-Date).ToUniversalTime().ToString('o')
    gitCommit     = $commit
    gitBranch     = $branch
    packages      = @(
        Get-ChildItem -Path $PackagesDir | Sort-Object Name | ForEach-Object {
            [ordered]@{
                name   = $_.Name
                bytes  = $_.Length
                sha256 = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash
            }
        }
    )
    sourceArchive = [ordered]@{
        name   = [System.IO.Path]::GetFileName($srcZip)
        bytes  = (Get-Item $srcZip).Length
        sha256 = (Get-FileHash -Path $srcZip -Algorithm SHA256).Hash
    }
}
$manifestPath = Join-Path $ReleaseDir 'manifest.json'
[System.IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 5),
    (New-Object System.Text.UTF8Encoding $false))   # UTF-8, no BOM

# --- Summary ---------------------------------------------------------------------
Write-Host ''
Write-Host "=== Release $Version complete ===" -ForegroundColor Cyan
Write-Host "  $ReleaseDir"
Get-ChildItem -Path $PackagesDir -Filter '*.nupkg' |
    Format-Table Name, @{ N = 'Size'; E = { '{0:N0} KB' -f ($_.Length / 1KB) } } -AutoSize |
    Out-String | Write-Host
Write-Host "  + symbols (*.snupkg), $([System.IO.Path]::GetFileName($srcZip)), manifest.json"
Write-Host ''
Write-Host 'To publish these packages to a feed later, e.g.:' -ForegroundColor DarkGray
Write-Host "  dotnet nuget push `"$PackagesDir\*.nupkg`" --source <feed> --api-key <key>" -ForegroundColor DarkGray
