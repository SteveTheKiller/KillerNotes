param(
    [string]$Output = (Join-Path $PSScriptRoot 'shortcuts.generated.js'),
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$appRoot = Split-Path $PSScriptRoot -Parent
[xml]$resources = [IO.File]::ReadAllText((Join-Path $appRoot 'Strings\en-US.xaml'))
$labels = @{}
foreach ($node in $resources.ResourceDictionary.ChildNodes) {
    if ($node.NodeType -ne 'Element') { continue }
    $key = $node.GetAttribute('Key', 'http://schemas.microsoft.com/winfx/2006/xaml')
    if ($key) { $labels[$key] = $node.InnerText }
}
function Get-Label([string]$key) {
    if (!$key) { return '' }
    if (!$labels.ContainsKey($key)) { throw "Missing English label: $key" }
    return $labels[$key]
}
$rows = [Collections.Generic.List[object]]::new()
$layers = [ordered]@{ base=[ordered]@{}; ctrl=[ordered]@{}; ctrlshift=[ordered]@{}; alt=[ordered]@{} }
$pattern = 'new\("(?<keys>[^"]*)",\s*"(?<label>[^"]*)",\s*"(?<category>[^"]+)",\s*\[(?<caps>[\s\S]*?)\](?:,\s*Listed:\s*(?<listed>false))?\)'
foreach ($name in @('ShortcutsTable.cs', 'DialogShortcutRows.cs', 'PickerShortcutRows.cs')) {
    $path = Join-Path $appRoot "Shell\$name"
    if (!(Test-Path -LiteralPath $path)) { continue }
    $source = [IO.File]::ReadAllText($path)
    $scope = 'Main window'
    foreach ($match in [regex]::Matches($source, $pattern)) {
        $label = Get-Label $match.Groups['label'].Value
        if (!$match.Groups['keys'].Value -and $label) { $scope = $label }
        $row = [ordered]@{ keys=$match.Groups['keys'].Value; label=$label; scope=$scope; category=$match.Groups['category'].Value.ToLowerInvariant(); listed=($match.Groups['listed'].Value -ne 'false') }
        $rows.Add($row)
        foreach ($cap in [regex]::Matches($match.Groups['caps'].Value, '\(KbLayer\.(?<layer>\w+),\s*"(?<id>[^"]+)",\s*"(?<label>[^"]+)"\)')) {
            $layer = $cap.Groups['layer'].Value.ToLowerInvariant()
            $layers[$layer][$cap.Groups['id'].Value] = @($row.category, (Get-Label $cap.Groups['label'].Value))
        }
    }
}
$actions = [IO.File]::ReadAllText((Join-Path $appRoot 'Shell\ActionShortcuts.cs'))
$actionPattern = 'new\("(?<keys>[^"]+)",\s*Key\.(?<id>\w+),\s*(?<mods>ModifierKeys[^,]+),\s*"(?<label>[^"]+)",\s*"(?<category>[^"]+)"'
foreach ($match in [regex]::Matches($actions, $actionPattern)) {
    $row = [ordered]@{ keys=$match.Groups['keys'].Value; label=(Get-Label $match.Groups['label'].Value); scope='Main window'; category=$match.Groups['category'].Value.ToLowerInvariant(); listed=$true }
    $rows.Add($row)
    $layer = if ($match.Groups['mods'].Value -match 'Alt') { 'alt' } elseif ($match.Groups['mods'].Value -match 'Shift') { 'ctrlshift' } else { 'ctrl' }
    $layers[$layer][$match.Groups['id'].Value] = @($row.category, $row.label)
}
$program = [IO.File]::ReadAllText((Join-Path $appRoot 'KillerNotes.Cli\Program.cs'))
$help = [regex]::Match($program, 'private static void PrintHelp\(\)\s*\{(?<body>[\s\S]*?)\n\s*\}').Groups['body'].Value
$cli = @([regex]::Matches($help, 'Console.WriteLine\("(?<text>(?:\\.|[^"\\])*)"\)') | ForEach-Object { [regex]::Unescape($_.Groups['text'].Value) })
if ($rows.Count -lt 60 -or $cli.Count -lt 10) { throw 'Shortcut or CLI extraction is incomplete' }
$data = [ordered]@{ rows=@($rows.ToArray()); layers=$layers; cli=$cli }
$content = "/* Generated from the app shortcut registries, English strings and CLI help. */`nwindow.KN_HELP=" + ($data | ConvertTo-Json -Depth 7 -Compress) + ";`n"
if ($content.IndexOf([char]0x2013) -ge 0 -or $content.IndexOf([char]0x2014) -ge 0) { throw 'Prohibited dash in generated content' }
if ($Check) {
    if (!(Test-Path -LiteralPath $Output) -or [IO.File]::ReadAllText($Output) -cne $content) { throw 'Generated help is stale. Run generate-shortcuts.ps1' }
    Write-Host "Generated help is current ($($rows.Count) rows, $($cli.Count) CLI lines)"
} else {
    [IO.File]::WriteAllText($Output, $content, [Text.UTF8Encoding]::new($false))
    Write-Host "Generated help ($($rows.Count) rows, $($cli.Count) CLI lines)"
}
