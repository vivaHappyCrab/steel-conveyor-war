#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [switch]$SkipFormat,
    [switch]$SkipSmoke,
    [switch]$SkipCoverage,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$solution = Join-Path $repoRoot 'SteelConveyorWar.sln'
$artifacts = Join-Path $repoRoot 'artifacts'
$testResults = Join-Path $artifacts 'test-results'
$coverageDir = Join-Path $artifacts 'coverage'

New-Item -ItemType Directory -Force -Path $artifacts, $testResults, $coverageDir | Out-Null

Write-Host '==> Restore'
dotnet restore $solution --locked-mode
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Locked-mode restore failed; generating/updating package lock files...'
    Get-ChildItem -Recurse -Filter '*.csproj' | ForEach-Object {
        dotnet restore $_.FullName --force-evaluate /p:RestorePackagesWithLockFile=true
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    dotnet restore $solution --locked-mode
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $SkipFormat) {
    Write-Host '==> Format check'
    # Use warn: some local SDK/format builds reject severity "diagnostic".
    dotnet format $solution --verify-no-changes --severity warn
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "==> Build ($Configuration)"
dotnet build $solution -c $Configuration --no-restore /p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host '==> Test'
$testArgs = @(
    'test', $solution,
    '-c', $Configuration,
    '--no-build',
    '--logger', 'trx',
    '--results-directory', $testResults
)
if (-not $SkipCoverage) {
    # Keep a single --results-directory (trx + cobertura under test-results).
    $testArgs += @('--collect', 'XPlat Code Coverage')
}

& dotnet @testArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipSmoke) {
    Write-Host '==> Headless host smoke (no display)'
    $headlessReplay = Join-Path $artifacts 'headless-smoke.scwreplay'
    dotnet run --project (Join-Path $repoRoot 'src/SteelConveyorWar.Headless') -c $Configuration --no-build -- --ticks 90 --record $headlessReplay
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host '==> Headless replay roundtrip'
    dotnet run --project (Join-Path $repoRoot 'src/SteelConveyorWar.Headless') -c $Configuration --no-build -- --replay $headlessReplay
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host '==> Client smoke test'
    dotnet run --project (Join-Path $repoRoot 'src/SteelConveyorWar.Client') -c $Configuration --no-build -- --smoke-test --no-record
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host 'Verification succeeded.'
Write-Host "Artifacts: $artifacts"
exit 0
