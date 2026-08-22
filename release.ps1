# release.ps1 - KillerNotes release workflow
# Builds, signs (Certum via SimplySign, family convention), tags, and publishes a GitHub release.
# Compatible with Windows PowerShell 5.1 and PowerShell 7.
#
# Usage:
#   .\release.ps1              # full release for the version in the csproj
#   .\release.ps1 -DryRun      # everything except tag push and gh release
#   .\release.ps1 -SkipSign    # local test build only - never release unsigned
#
# winget is NOT submitted from here. .github/workflows/winget-release.yml fires on
# "release: published" and runs komac itself, so doing it here too would double-submit.

[CmdletBinding()]
param(
    [switch]$DryRun,
    # SHA1 thumbprint of the code-signing cert (40 hex chars). Preferred over CertName.
    [string]$CertThumbprint = "",
    # Fallback: CN match in the Windows cert store, as in the other Killer release scripts.
    [string]$CertName = "Open Source Developer Stephen Riley",
    [switch]$SkipSign
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

# Landing-page find/replace that REFUSES to silently do nothing. A plain -replace whose
# pattern no longer matches the markup leaves the text untouched, the "did anything change"
# check then reports the page as already current, and the release ships with a stale fact on
# the site. That is exactly how the exe size on technical.html drifted to 4.5 MB while
# index.html said 4.6. Every site edit goes through here so changed markup fails the release
# instead of quietly skipping.
function Edit-SiteFact {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)][string]$Pattern,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Replacement,
        [Parameter(Mandatory)][string]$What
    )
    if ($Text -notmatch $Pattern) {
        Fail "Landing page: could not find $What. The markup changed - update the pattern in release.ps1 (step 7b)."
    }
    return ($Text -replace $Pattern, $Replacement)
}

# Resolve the repo's default branch instead of hardcoding it, so the same script works across
# the Killer family. origin/HEAD is the best hint but it can go stale - it keeps naming a
# branch that was renamed away - so a candidate is only accepted if it still exists on the
# remote. Order: whatever origin/HEAD claims, then main, then master.
function Get-DefaultBranch {
    $remoteHeads = @(git ls-remote --heads origin 2>$null) |
        ForEach-Object { ($_ -split '\s+')[-1] -replace '^refs/heads/', '' }
    if (-not $remoteHeads) { return $null }

    $candidates = @()
    $originHead = git symbolic-ref --quiet refs/remotes/origin/HEAD 2>$null
    if ($originHead) { $candidates += (($originHead -replace '^refs/remotes/origin/', '').Trim()) }
    foreach ($c in @('main', 'master')) { if ($candidates -notcontains $c) { $candidates += $c } }

    foreach ($c in $candidates) {
        if ($c -and $remoteHeads -contains $c) { return $c }
    }
    return $null
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

# --- 2. Preflight: clean tree, on the default branch, up to date, tag free ---
Step "Preflight checks"
$defaultBranch = Get-DefaultBranch
if (-not $defaultBranch) { Fail 'Could not determine the default branch from origin' }
Write-Host "Default branch: $defaultBranch"

$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne $defaultBranch) { Fail "On branch '$branch', expected $defaultBranch" }

$dirty = git status --porcelain
if ($dirty) { Fail "Working tree is not clean. Commit or stash first:`n$($dirty -join "`n")" }

git fetch origin $defaultBranch --quiet
$local = (git rev-parse HEAD).Trim()
$remote = (git rev-parse "origin/$defaultBranch").Trim()
if ($local -ne $remote) { Fail "Local $defaultBranch and origin/$defaultBranch differ. Push or pull first." }

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

# The About card shows <ReleaseDate> beside the version so users can tell how old their
# build is. It is a hand-edited csproj field, so it silently goes stale unless something
# checks it - that something is here. It must equal the date on this version's CHANGELOG
# section, which is the date the release actually goes out.
if ($csproj -notmatch '<ReleaseDate>([0-9]{4}-[0-9]{2}-[0-9]{2})</ReleaseDate>') {
    Fail 'No <ReleaseDate>yyyy-MM-dd</ReleaseDate> found in KillerNotes.csproj'
}
$releaseDate = $Matches[1]
if ($changelog -notmatch ('## \[' + [regex]::Escape($Version) + '\] - ([0-9]{4}-[0-9]{2}-[0-9]{2})')) {
    Fail "CHANGELOG.md section [$Version] has no yyyy-MM-dd date"
}
$changelogDate = $Matches[1]
if ($releaseDate -ne $changelogDate) {
    Fail "csproj <ReleaseDate> is $releaseDate but CHANGELOG [$Version] is dated $changelogDate. Bump the csproj."
}
Write-Host "Release date: $releaseDate"

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
# NOTE: this is the PRE-signature size, used only for the Costura sanity check. The figure
# published on the landing page is recomputed after signing (step 7b), because Authenticode
# adds ~10KB and the site would otherwise advertise a size the downloaded file does not have.
Step "Verifying single-exe packaging"
$exeSize = (Get-Item $exe).Length
$unsignedMB = '{0:N1} MB' -f ($exeSize / 1MB)
if ($exeSize -lt 3MB) {
    Fail "KillerNotes.exe is only $unsignedMB - Costura does not appear to have embedded the dependencies. Check Fody/FodyWeavers.xml."
}
Write-Host "KillerNotes.exe is $unsignedMB (unsigned)"

# --- 6. Sign (Certum via SimplySign, same flow as the other Killer release scripts) ---
if ($SkipSign) {
    Write-Host ""
    Write-Host 'SkipSign: KillerNotes.exe will be UNSIGNED - do not release this build' -ForegroundColor Red
} else {
    Step "Signing KillerNotes.exe"
    $ssProc = Get-Process -Name 'SimplySignDesktop' -ErrorAction SilentlyContinue
    if (-not $ssProc) {
        Write-Warning 'SimplySign Desktop does not appear to be running.'
        Write-Host 'Start it and wait for Connected, then press Enter to continue (Ctrl+C aborts).'
        $null = Read-Host
    }

    # PATH first (covers shells where ProgramFiles(x86) is not in the environment), then the SDK kit dir.
    $signtool = (Get-Command signtool -ErrorAction SilentlyContinue).Source
    if (-not $signtool) {
        $kitBase = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
        if (-not (Test-Path $kitBase)) { $kitBase = 'C:\Program Files (x86)\Windows Kits\10\bin' }
        if (Test-Path $kitBase) {
            $signtool = Get-ChildItem "$kitBase\*\x64\signtool.exe" -Recurse -ErrorAction SilentlyContinue |
                        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
        }
    }
    if (-not $signtool) { Fail 'signtool.exe not found. Install the Windows SDK.' }
    Write-Host "signtool: $signtool"

    $certArgs = if ($CertThumbprint) { @('/sha1', $CertThumbprint) } else { @('/n', $CertName) }

    # TSA endpoints - tried in order; first success wins.
    $tsaList = @(
        'http://timestamp.digicert.com',
        'http://timestamp.sectigo.com',
        'http://ts.ssl.com'
    )
    $signedOk = $false
    foreach ($tsa in $tsaList) {
        Write-Host "Trying TSA: $tsa"
        & $signtool sign /fd sha256 /tr $tsa /td sha256 @certArgs /d 'KillerNotes' /du 'https://killernotes.net' /v $exe
        if ($LASTEXITCODE -eq 0) { $signedOk = $true; break }
        Write-Warning "TSA $tsa failed (exit $LASTEXITCODE). Trying next..."
        Start-Sleep -Seconds 3
    }
    if (-not $signedOk) { Fail 'Signing failed on all TSA endpoints. Is SimplySign Desktop connected?' }

    # Post-sign gate: abort if the chain does not validate to a trusted root.
    & $signtool verify /pa /v $exe
    if ($LASTEXITCODE -ne 0) { Fail 'signtool verify FAILED - the signed exe does not pass trust validation. DO NOT RELEASE.' }
    Write-Host 'Signed, timestamped, and chain-verified' -ForegroundColor Green
}

# --- 7. Source bundle (GPL3 family convention, same as the other Killer apps) ---
# Preflight guarantees a clean tree in sync with origin, so tracked files == the tagged source.
Step "Bundling source"
$srcZip = Join-Path $outDir "KillerNotes-$Version-src.zip"
if (Test-Path $srcZip) { Remove-Item $srcZip -Force }
$staging = Join-Path $env:TEMP "KillerNotes-src-$Version"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null
# The landing site is a separate deployable, not app source - and a release's own exe
# hash can never live correctly inside the source it is built from (it is circular).
# Exclude the site so the bundle is buildable-app-only and never carries stale site info.
$srcFiles = @(git ls-files) | Where-Object { $_ -notlike 'notes-landing/*' }
if ($srcFiles.Count -eq 0) { Fail 'git ls-files returned no tracked files' }
foreach ($f in $srcFiles) {
    # Tracked but deleted on disk (removed without git rm): skip, do not abort the bundle.
    if (-not (Test-Path $f)) { Write-Warning "Skipping tracked file missing on disk: $f"; continue }
    $dst = Join-Path $staging $f
    $parent = Split-Path $dst -Parent
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    Copy-Item $f $dst -Force
}
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $srcZip -Force
Remove-Item $staging -Recurse -Force
$srcZipMB = '{0:N1} MB' -f ((Get-Item $srcZip).Length / 1MB)
Write-Host "Source bundle: $srcZip ($srcZipMB)"

# --- 7a2. LGPL source for the bundled LAME encoder ---
# libmp3lame.dll is embedded in the exe, and LGPL-2.1 obliges us to offer the matching source
# WHEREVER THE BINARY IS DISTRIBUTED. What people download is the exe, not the repo, so the
# tarball ships as its own release asset rather than only living in the source bundle - nobody
# should have to unpack the whole source zip to exercise that right.
# libFLAC is BSD-3-Clause and carries no such obligation.
Step "Staging LGPL source (LAME)"
$lameSrc = Get-ChildItem (Join-Path $PSScriptRoot 'third_party\audio') -Filter 'lame-*.tar.gz' -ErrorAction SilentlyContinue |
           Select-Object -First 1
if ($lameSrc) {
    $lameAsset = Join-Path $outDir $lameSrc.Name
    Copy-Item $lameSrc.FullName $lameAsset -Force
    Write-Host "LGPL source asset: $($lameSrc.Name)"
} elseif (Test-Path (Join-Path $PSScriptRoot 'third_party\audio\libmp3lame.dll')) {
    # Shipping the binary without its source is the one state that is not allowed.
    Fail 'libmp3lame.dll is bundled but no lame-*.tar.gz is present in third_party\audio - LGPL requires the matching source to be offered alongside the binary.'
} else {
    Write-Host 'No LAME binary bundled - skipping LGPL source asset'
}

# --- 7b. Checksums (SHA256SUMS.txt) ---
# The in-app updater (About.cs DoSelfUpdateAsync) downloads this asset next to the exe and
# verifies the download against it; WITHOUT it the "Update" button falls back to just opening
# the releases page. Line format is "<filename>  <sha256>" - the updater matches the line that
# starts with KillerNotes.exe and takes the LAST whitespace token as the hash.
Step "Writing SHA256SUMS.txt"
# Size and hash both come from the exe in its FINAL, signed state - this is the file people
# actually download, so it is what the landing page must describe.
$exeMB = '{0:N1} MB' -f ((Get-Item $exe).Length / 1MB)
Write-Host "Signed KillerNotes.exe is $exeMB"
$sumsFile = Join-Path $outDir 'SHA256SUMS.txt'
$sumsAssets = @($exe, $srcZip)
if ($lameAsset) { $sumsAssets += $lameAsset }
$sumsLines = foreach ($asset in $sumsAssets) {
    $hash = (Get-FileHash $asset -Algorithm SHA256).Hash.ToLower()
    '{0}  {1}' -f (Split-Path $asset -Leaf), $hash
}
Set-Content -Path $sumsFile -Encoding ascii -Value ($sumsLines -join "`r`n")
Write-Host ($sumsLines -join "`n")

# --- 7b. Landing page release info (notes-landing) ---
# The hero block (version, released date, size, sha256) and the verEgg footer on
# every page carry release facts the script already knows, so they are rewritten
# here and committed BEFORE the tag - the tag always matches the live site data.
# ReadAllText/WriteAllText keep the files BOM-less UTF-8 (PS 5.1 Set-Content -Encoding UTF8 adds a BOM).
Step "Updating notes-landing release info"
if ($DryRun) {
    Write-Host "DryRun: would update notes-landing to v$Version and commit"
} else {
    $exeHash    = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
    $releaseDate = Get-Date -Format 'yyyy-MM-dd'
    $siteDir    = Join-Path (Get-Location).Path 'notes-landing'
    $indexPath  = Join-Path $siteDir 'index.html'
    $indexRaw   = [System.IO.File]::ReadAllText($indexPath)
    $indexNew   = Edit-SiteFact $indexRaw 'KillerNotes v[0-9]+\.[0-9]+\.[0-9]+' "KillerNotes v$Version" 'the hero version'
    $indexNew   = Edit-SiteFact $indexNew '(<span class="k">released</span>&nbsp;<span class="v">)[0-9]{4}-[0-9]{2}-[0-9]{2}' ('${1}' + $releaseDate) 'the hero released date'
    $indexNew   = Edit-SiteFact $indexNew '(<span class="k">size</span>&nbsp;<span class="v">)[^<]*' ('${1}' + $exeMB + ' single exe') 'the hero size row'
    $indexNew   = Edit-SiteFact $indexNew '<span class="v hash"><span>[0-9a-f]{32}</span><span>[0-9a-f]{32}</span></span>' ('<span class="v hash"><span>' + $exeHash.Substring(0, 32) + '</span><span>' + $exeHash.Substring(32, 32) + '</span></span>') 'the hero sha256 block'
    if ($indexNew -ne $indexRaw) { [System.IO.File]::WriteAllText($indexPath, $indexNew) }

    # technical.html states the exe size twice in prose (the Distribution paragraph and the
    # Specs table). Those are the only release facts outside index.html's hero block, and
    # they drifted from it before this ran here - index said 4.6 MB while both of these
    # still said 4.5.
    $techPath = Join-Path $siteDir 'technical.html'
    $techRaw  = [System.IO.File]::ReadAllText($techPath)
    $techNew  = Edit-SiteFact $techRaw '(<b>single signed exe</b> - about )[0-9.]+ MB' ('${1}' + $exeMB) 'the Distribution paragraph size'
    $techNew  = Edit-SiteFact $techNew '(Single signed exe \(~)[0-9.]+ MB'            ('${1}' + $exeMB) 'the Specs table size'
    if ($techNew -ne $techRaw) { [System.IO.File]::WriteAllText($techPath, $techNew) }

    foreach ($page in 'index.html', 'about.html', 'help.html', 'technical.html') {
        $p   = Join-Path $siteDir $page
        $raw = [System.IO.File]::ReadAllText($p)
        $new = Edit-SiteFact $raw '(id="verEgg"[^>]*>)v[0-9]+\.[0-9]+\.[0-9]+' ('${1}' + "v$Version") "the verEgg footer version in $page"
        if ($new -ne $raw) { [System.IO.File]::WriteAllText($p, $new) }
    }
    $siteDirty = git status --porcelain notes-landing
    if ($siteDirty) {
        git add notes-landing
        git commit -m "site: v$Version release info" --quiet
        git push origin $defaultBranch --quiet
        if ($LASTEXITCODE -ne 0) { Fail 'Landing page commit failed to push' }
        Write-Host "notes-landing updated to v$Version and pushed"
    } else {
        Write-Host 'notes-landing already current'
    }
}

# --- 8. Release notes from CHANGELOG section ---
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
# Written license offer, alongside the tarball asset itself. LGPL-2.1 wants the source OFFERED,
# not merely present, so the notes have to say it is there and what it is for.
if ($lameSrc) {
    $notes.Add('')
    $notes.Add('---')
    $notes.Add('')
    $notes.Add("KillerNotes embeds the LAME MP3 encoder (libmp3lame, LGPL-2.1) for exporting recordings. Its complete corresponding source is attached to this release as ``$($lameSrc.Name)``, and also lives in ``third_party/audio/`` in the tagged source. Build instructions are in ``third_party/audio/README.md``.")
}
$notes -join "`r`n" | Set-Content -Path $notesFile -Encoding UTF8
Write-Host "Notes written to $notesFile ($($notes.Count) lines)"

if ($DryRun) {
    Step "DryRun: stopping before tag and release"
    Write-Host "Would create tag $Tag, push it, and publish release with KillerNotes.exe ($exeMB), $(Split-Path $srcZip -Leaf) ($srcZipMB), and SHA256SUMS.txt"
    exit 0
}

# --- 9. Tag and push ---
Step "Tagging $Tag"
git tag -a $Tag -m "KillerNotes $Tag"
git push origin $Tag
if ($LASTEXITCODE -ne 0) { Fail 'Tag push failed' }

# --- 10. GitHub release ---
Step "Creating GitHub release"
$releaseAssets = @($exe, $srcZip, $sumsFile)
if ($lameAsset) { $releaseAssets += $lameAsset }   # LGPL source for the bundled LAME encoder
gh release create $Tag @releaseAssets --title "KillerNotes $Tag" --notes-file $notesFile --verify-tag
if ($LASTEXITCODE -ne 0) { Fail 'gh release create failed' }

Step "Refreshing thekiller.net software page"
gh workflow run deploy.yml --repo SteveTheKiller/thekiller-site
if ($LASTEXITCODE -ne 0) {
    Write-Warning 'The release is published, but thekiller.net refresh could not be started. Run: gh workflow run deploy.yml --repo SteveTheKiller/thekiller-site'
}

# Publishing this release is also what fires .github/workflows/winget-release.yml, which
# submits to winget-pkgs via komac. Do NOT add a komac call here - it would double-submit.
# That workflow uses `komac update`, which only works once the package already exists in
# winget-pkgs; the very first submission has to be `komac new` by hand (see the workflow).
#
# This script used to submit here, and it failed silently: komac's exit code only produced a
# Write-Warning, so seven releases (1.1.0 through 1.1.6) never reached winget and it sat on
# 1.0.1 unnoticed. As a workflow the failure is a red X on the Actions tab instead, and it can
# be retried with workflow_dispatch without cutting a new release.

Step "Done"
Write-Host "Release $Tag published:"
gh release view $Tag --json url --jq '.url'
Write-Host "  winget: submitted by .github/workflows/winget-release.yml (needs the WINGET_TOKEN secret)" -ForegroundColor Yellow
