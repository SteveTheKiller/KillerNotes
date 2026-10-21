<#
.SYNOPSIS
    Runs the KillerNotes performance benchmarks (KN-P07) on this machine.

.DESCRIPTION
    Builds the test project, then runs the opt-in benchmark tests against synthetic notebooks of
    1,000, 10,000 and 50,000 notes. Each run writes its results to a new timestamped folder under
    C:\Users\steve\scratch\kn-perf (or -OutRoot). Only the newest -Keep runs are kept; older run
    folders are deleted every time this script runs, so the scratch folder never grows.
    Nothing here ships in the app. Works in Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
    .\tests\Performance\Run.ps1
    .\tests\Performance\Run.ps1 -Sizes 1k,10k
#>
[CmdletBinding()]
param(
    [ValidateSet('1k', '10k', '50k')]
    [string[]]$Sizes = @('1k', '10k', '50k'),
    [string]$OutRoot = 'C:\Users\steve\scratch\kn-perf',
    [ValidateRange(1, 50)]
    [int]$Keep = 3
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$tests = Join-Path $repo 'KillerNotes.Tests\KillerNotes.Tests.csproj'

New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
$run = Join-Path $OutRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Force -Path $run | Out-Null

$names = @{ '1k' = 'Notes1k'; '10k' = 'Notes10k'; '50k' = 'Notes50k' }
$filter = ($Sizes | ForEach-Object { "FullyQualifiedName~PerfBenchmarks.$($names[$_])" }) -join '|'

$env:KN_PERF = '1'
$env:KN_PERF_OUT = $run
try {
    & dotnet test $tests -c Release --filter $filter --logger "console;verbosity=normal"
    $exit = $LASTEXITCODE
}
finally {
    Remove-Item Env:KN_PERF -ErrorAction SilentlyContinue
    Remove-Item Env:KN_PERF_OUT -ErrorAction SilentlyContinue
}

# Keep the newest runs only. Each run folder holds just its results files; the generated
# notebooks are deleted by the tests themselves as soon as each size is measured.
Get-ChildItem -Path $OutRoot -Directory |
    Where-Object { $_.Name -match '^\d{8}-\d{6}$' } |
    Sort-Object Name -Descending |
    Select-Object -Skip $Keep |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }

Write-Host "Results: $run"
Get-ChildItem -Path $run -Filter 'results-*.json' | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }
exit $exit
