param([string]$ReleaseScript = (Join-Path $PSScriptRoot '..\release.ps1'))
$ErrorActionPreference = 'Stop'
$tokens = $null
$errors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile($ReleaseScript, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw "Release script does not parse: $errors" }
$source = Get-Content -LiteralPath $ReleaseScript -Raw
$start = $source.IndexOf('Step "Scanning for vulnerable packages"')
$end = $source.IndexOf('# --- 3b. Release tests ---', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Audit gate was not found.' }
$gate = [scriptblock]::Create($source.Substring($start, $end - $start))
function Step([string]$Message) {}
function Fail([string]$Message) { throw "Audit rejected: $Message" }
function dotnet {
    if ($args[0] -eq 'restore') {
        $global:LASTEXITCODE = $case.RestoreExit
        return
    }
    $global:LASTEXITCODE = $case.AuditExit
    $case.Report
}
$clean = '{"version":1,"sources":["https://example.test/index.json"],"projects":[{"path":"KillerNotes.csproj"}]}'
$cases = @(
    @{ Name = 'restore failure'; RestoreExit = 1; AuditExit = 0; Report = $clean; Reject = $true },
    @{ Name = 'audit failure'; RestoreExit = 0; AuditExit = 1; Report = $clean; Reject = $true },
    @{ Name = 'unreadable report'; RestoreExit = 0; AuditExit = 0; Report = 'not JSON'; Reject = $true },
    @{ Name = 'missing projects'; RestoreExit = 0; AuditExit = 0; Report = '{"version":1,"sources":["x"]}'; Reject = $true },
    @{ Name = 'missing sources'; RestoreExit = 0; AuditExit = 0; Report = '{"version":1,"projects":[{"path":"x"}]}'; Reject = $true },
    @{ Name = 'audit source warning'; RestoreExit = 0; AuditExit = 0; Report = '{"version":1,"sources":["x"],"projects":[{"path":"x"}],"logs":[{"level":"warning","message":"NU1900"}]}'; Reject = $true },
    @{ Name = 'audit source error'; RestoreExit = 0; AuditExit = 0; Report = '{"version":1,"sources":["x"],"projects":[{"path":"x"}],"logs":[{"level":"error","message":"source unavailable"}]}'; Reject = $true },
    @{ Name = 'top level vulnerability'; RestoreExit = 0; AuditExit = 0; Report = '{"version":1,"sources":["x"],"projects":[{"path":"x","frameworks":[{"topLevelPackages":[{"id":"unsafe","vulnerabilities":[{"severity":"High"}]}]}]}]}'; Reject = $true },
    @{ Name = 'transitive vulnerability'; RestoreExit = 0; AuditExit = 0; Report = '{"version":1,"sources":["x"],"projects":[{"path":"x","frameworks":[{"transitivePackages":[{"id":"unsafe","vulnerabilities":[{"severity":"Moderate"}]}]}]}]}'; Reject = $true },
    @{ Name = 'clean report'; RestoreExit = 0; AuditExit = 0; Report = $clean; Reject = $false },
    @{ Name = 'clean framework'; RestoreExit = 0; AuditExit = 0; Report = '{"version":1,"sources":["x"],"projects":[{"path":"x","frameworks":[{"framework":"net48"}]}]}'; Reject = $false }
)
foreach ($case in $cases) {
    $rejected = $false
    try { & $gate } catch { $rejected = $true }
    if ($rejected -ne $case.Reject) { throw "Unexpected release verdict: $($case.Name), rejected=$rejected" }
    Write-Output "PASS: $($case.Name)"
}
Write-Output "Passed $($cases.Count) selected audit cases."
