$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:ProgramFiles 'KillerNotes'
$installExe = Join-Path $installDir 'KillerNotes.exe'

if (Test-Path $installExe) {
    Start-Process -FilePath $installExe -ArgumentList '/uninstall' -Wait -NoNewWindow
} elseif (Test-Path $installDir) {
    Remove-Item $installDir -Recurse -Force
}

$startMenuPath = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\KillerNotes'
if (Test-Path $startMenuPath) { Remove-Item $startMenuPath -Recurse -Force }
