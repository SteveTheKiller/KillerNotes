$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:ProgramFiles 'KillerNotes'
$installExe = Join-Path $installDir 'KillerNotes.exe'

if (Test-Path $installExe) {
    $uninstaller = Start-Process -FilePath $installExe -ArgumentList '/uninstall-silent' -Wait -PassThru -WindowStyle Hidden
    if ($uninstaller.ExitCode -ne 0) { throw "KillerNotes uninstall failed with exit code $($uninstaller.ExitCode)." }
}
