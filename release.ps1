# release.ps1 - KillerNotes release workflow
# Builds, packages, tags, and publishes a GitHub release.
# Compatible with Windows PowerShell 5.1 and PowerShell 7.
#
# Usage:
#   .\release.ps1              # full release for the version in the csproj
#   .\release.ps1 -DryRun      # everything except tag push and gh release

[CmdletBinding()]
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

function Fail([string]$Message) {
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

function Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

# --- 1. Read version from the csproj (single source of truth) ---
Step "Reading version from KillerNotes.csproj"
$csproj = Get-Content -Path 'KillerNotes.csproj' -Raw
if ($csproj -notmatch '<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>') {
    Fail 'No <Version>x.y.z</Version> found in KillerNotes.csproj'
}
$Version = $Matches[1]
$Tag = "v$Version"
Write-Host "Version: $Version (tag $Tag)"

# --- 2. Preflight: clean tree, on main, up to date, tag free ---
Step "Preflight checks"
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main') { Fail "On branch '$branch', expected main" }

$dirty = git status --porcelain
if ($dirty) { Fail "Working tree is not clean. Commit or stash first:`n$($dirty -join "`n")" }

git fetch origin main --quiet
$local = (git rev-parse HEAD).Trim()
$remote = (git rev-parse origin/main).Trim()
if ($local -ne $remote) { Fail 'Local main and origin/main differ. Push or pull first.' }

$existing = git tag --list $Tag
if ($existing) { Fail "Tag $Tag already exists" }

$remoteTag = git ls-remote --tags origin $Tag
if ($remoteTag) { Fail "Tag $Tag already exists on origin" }

# CHANGELOG must have a dated section for this version
$changelog = Get-Content -Path 'CHANGELOG.md' -Raw
if ($changelog -match [regex]::Escape("## [$Version] - Unreleased")) {
    Fail "CHANGELOG.md section [$Version] is still marked Unreleased"
}
if ($changelog -notmatch [regex]::Escape("## [$Version]")) {
    Fail "CHANGELOG.md has no [$Version] section"
}
Write-Host 'Preflight OK'

# --- 3. Vulnerable package scan (required at every release, see csproj) ---
Step "Scanning for vulnerable packages"
dotnet restore | Out-Null
$scan = dotnet list package --vulnerable --include-transitive 2>&1 | Out-String
Write-Host $scan
if ($scan -match 'has the following vulnerable packages') {
    Fail 'Vulnerable packages found. Resolve before releasing.'
}

# --- 4. Clean Release build ---
Step "Building Release"
if (Test-Path 'bin\Release') { Remove-Item 'bin\Release' -Recurse -Force }
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { Fail 'Build failed' }

$outDir = 'bin\Release\net48'
$exe = Join-Path $outDir 'KillerNotes.exe'
if (-not (Test-Path $exe)) { Fail "Expected output not found: $exe" }

# Sanity check: built file version matches the csproj version
$fileVersion = (Get-Item $exe).VersionInfo.FileVersion
Write-Host "Built KillerNotes.exe FileVersion $fileVersion"
if ($fileVersion -notlike "$Version*") {
    Fail "Built FileVersion $fileVersion does not match csproj version $Version"
}

# --- 5. Single-exe check ---
# Costura embeds every managed dependency and SqlCipherBootstrap carries the native, so the
# exe alone is the release asset (the site links to releases/latest/download/KillerNotes.exe).
Step "Verifying single-exe packaging"
$exeSize = (Get-Item $exe).Length
$exeMB = '{0:N1} MB' -f ($exeSize / 1MB)
if ($exeSize -lt 3MB) {
    Fail "KillerNotes.exe is only $exeMB - Costura does not appear to have embedded the dependencies. Check Fody/FodyWeavers.xml."
}
Write-Host "KillerNotes.exe is $exeMB"

# --- 6. Release notes from CHANGELOG section ---
Step "Extracting release notes from CHANGELOG.md"
$lines = Get-Content -Path 'CHANGELOG.md'
$notes = New-Object System.Collections.Generic.List[string]
$inSection = $false
foreach ($line in $lines) {
    if ($line -match "^## \[$([regex]::Escape($Version))\]") { $inSection = $true; continue }
    if ($inSection -and $line -match '^## \[') { break }
    if ($inSection) { $notes.Add($line) }
}
if ($notes.Count -eq 0) { Fail "Could not extract [$Version] notes from CHANGELOG.md" }
$notesFile = Join-Path $env:TEMP "KillerNotes-$Version-notes.md"
$notes -join "`r`n" | Set-Content -Path $notesFile -Encoding UTF8
Write-Host "Notes written to $notesFile ($($notes.Count) lines)"

if ($DryRun) {
    Step "DryRun: stopping before tag and release"
    Write-Host "Would create tag $Tag, push it, and publish release with KillerNotes.exe ($exeMB)"
    exit 0
}

# --- 7. Tag and push ---
Step "Tagging $Tag"
git tag -a $Tag -m "KillerNotes $Tag"
git push origin $Tag
if ($LASTEXITCODE -ne 0) { Fail 'Tag push failed' }

# --- 8. GitHub release ---
Step "Creating GitHub release"
gh release create $Tag $exe --title "KillerNotes $Tag" --notes-file $notesFile --verify-tag
if ($LASTEXITCODE -ne 0) { Fail 'gh release create failed' }

Step "Done"
Write-Host "Release $Tag published:"
gh release view $Tag --json url --jq '.url'
