<#
.SYNOPSIS
    Keyboard-only run-through of KillerNotes' everyday tasks (1.6.0, KN-P08).

.DESCRIPTION
    Starts KillerNotes in demo mode (a scratch notepad; your own notes are never opened) and drives
    it by keyboard alone: create a note, edit it, find text in it, search for it, move it to the
    trash and restore it, and open the export. After each step it reads the focused control through
    UI Automation and fails when focus is lost, lands on something without a name, or does not
    reach the control the step expects.

    It sends real keystrokes to the desktop, so run it only when nobody is typing, with the screen
    unlocked. It is part of the hands-on check, never run unattended.

.PARAMETER Exe
    The KillerNotes.exe to test. Defaults to the Release build of this checkout.

.EXAMPLE
    .\tests\Accessibility\Run-KeyboardWorkflows.ps1
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\..\bin\Release\net48\KillerNotes.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms

if (-not (Test-Path $Exe)) { throw "Build first: $Exe not found" }
$process = Start-Process -FilePath (Resolve-Path $Exe) -ArgumentList '--demo' -PassThru
$failures = New-Object System.Collections.Generic.List[string]

function Wait-Window {
    for ($i = 0; $i -lt 60; $i++) {
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children, $condition)
        if ($window) { return $window }
        Start-Sleep -Milliseconds 500
    }
    throw 'KillerNotes did not open a window'
}

function Send([string]$keys, [int]$wait = 400) {
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds $wait
}

function Focused {
    $element = [System.Windows.Automation.AutomationElement]::FocusedElement
    [pscustomobject]@{
        Name = $element.Current.Name
        Type = $element.Current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
        Id   = $element.Current.AutomationId
        Pid  = $element.Current.ProcessId
    }
}

function Check([string]$step, [string[]]$expectTypes) {
    $f = Focused
    $line = '{0,-28} {1,-12} {2,-24} "{3}"' -f $step, $f.Type, $f.Id, $f.Name
    if ($f.Pid -ne $process.Id) { $failures.Add("$step - focus left KillerNotes"); Write-Host "FAIL $line" -ForegroundColor Red; return }
    if ([string]::IsNullOrWhiteSpace($f.Name) -and $f.Type -notin @('Document', 'Edit')) { $failures.Add("$step - focused $($f.Type) has no name"); Write-Host "FAIL $line" -ForegroundColor Red; return }
    if ($expectTypes -and $f.Type -notin $expectTypes) { $failures.Add("$step - expected $($expectTypes -join '/'), got $($f.Type)"); Write-Host "FAIL $line" -ForegroundColor Red; return }
    Write-Host "ok   $line"
}

try {
    $window = Wait-Window
    $window.SetFocus()
    Start-Sleep -Seconds 2

    # Create and edit
    Send '^n'; Check 'New note (Ctrl+N)' @('Edit', 'Document')
    Send 'Keyboard run-through{TAB}'; Check 'Title, then Tab' @('Document', 'Edit')
    Send 'Typed with the keyboard only.'; Check 'Typing in the note' @('Document', 'Edit')

    # Find in this note
    Send '^f'; Check 'Find (Ctrl+F)' @('Edit')
    Send 'keyboard'; Send '{ENTER}'; Check 'Find next (Enter)' @('Edit')
    Send '{ESC}'; Check 'Close find (Esc)' @('Document', 'Edit')

    # Search every note
    Send '{F3}'; Check 'Search (F3)' @('Edit')
    Send 'run-through tag:'; Send '{BACKSPACE 5}'; Check 'Typing a search' @('Edit')
    Send '{ESC}'; Check 'Clear search (Esc)' $null

    # Filters menu, then back out
    Send '%+f'; Check 'Filters (Alt+Shift+F)' @('MenuItem')
    Send '{ESC}'; Check 'Close filters (Esc)' $null

    # Trash and restore from the note list
    Send '{F5}'; Send '{F5}'                     # sidebar shown and focused
    Send '{DEL}'; Check 'Move to trash (Delete)' $null
    Send '%{DEL}'; Check 'Open the trash (Alt+Delete)' $null
    Send '^{F8}'; Check 'Restore (Ctrl+F8)' $null

    # Export, then cancel the file picker
    Send '{F8}' 1200; Check 'Export (F8)' @('Edit', 'Button', 'ListItem', 'List', 'ComboBox')
    Send '{ESC}' 800; Check 'Cancel export (Esc)' $null

    # The About card, where the lock, tray and shortcut options live
    Send '{F12}' 800; Check 'About (F12)' $null
    Send '{ESC}'; Check 'Close About (Esc)' $null
}
finally {
    if (-not $process.HasExited) { $process.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2 }
    if (-not $process.HasExited) { $process.Kill() }
}

if ($failures.Count -gt 0) {
    Write-Host "`n$($failures.Count) problem(s):" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "`nEvery step kept a named focus inside KillerNotes." -ForegroundColor Green
