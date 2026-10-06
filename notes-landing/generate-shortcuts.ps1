param(
    [string]$Output = (Join-Path $PSScriptRoot 'shortcuts.generated.js'),
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$appRoot = Split-Path $PSScriptRoot -Parent
$localeFiles = [ordered]@{
    en='en-US.xaml'; it='it-IT.xaml'; vi='vi-VN.xaml'; hu='hu-HU.xaml'; pl='pl-PL.xaml'; cs='cs-CZ.xaml'
    es='es.xaml'; de='de-DE.xaml'; fr='fr-FR.xaml'; tr='tr-TR.xaml'; zh='zh-TW.xaml'; 'zh-cn'='zh-CN.xaml'
    bn='bn.xaml'; ja='ja-JP.xaml'; ru='ru-RU.xaml'; kk='kk-KZ.xaml'; uk='uk-UA.xaml'; nb='nb-NO.xaml'; pt='pt-BR.xaml'
}
$dictionaries = [ordered]@{}
foreach ($locale in $localeFiles.Keys) {
    [xml]$resources = [IO.File]::ReadAllText((Join-Path $appRoot "Strings\$($localeFiles[$locale])"))
    $dictionary = @{}
    foreach ($node in $resources.ResourceDictionary.ChildNodes) {
        if ($node.NodeType -ne 'Element') { continue }
        $key = $node.GetAttribute('Key', 'http://schemas.microsoft.com/winfx/2006/xaml')
        if ($key) { $dictionary[$key] = $node.InnerText }
    }
    $dictionaries[$locale] = $dictionary
}
function Get-Label([string]$key) {
    if (!$key) { return '' }
    if (!$labels.ContainsKey($key)) { throw "Missing $locale label: $key" }
    return $labels[$key]
}
function Get-LocalizedHelp([string]$locale) {
$labels = $dictionaries[$locale]
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
$categories = [ordered]@{}
foreach ($entry in ([ordered]@{ file='Str_Sec_File'; note='Str_Sec_Notes'; format='Str_Sec_Format'; view='Str_Sec_View'; search='Str_Sec_Search'; edit='Str_Sec_Edit'; help='Str_Sec_Help' }).GetEnumerator()) {
    $categories[$entry.Key] = Get-Label $entry.Value
}
return [ordered]@{ rows=@($rows.ToArray()); layers=$layers; categories=$categories; hint=(Get-Label 'Str_KS_HoldHint') }
}
$locales = [ordered]@{}
foreach ($locale in $localeFiles.Keys) { $locales[$locale] = Get-LocalizedHelp $locale }
$program = [IO.File]::ReadAllText((Join-Path $appRoot 'KillerNotes.Cli\Program.cs'))
$help = [regex]::Match($program, 'private static void PrintHelp\(\)\s*\{(?<body>[\s\S]*?)\n\s*\}').Groups['body'].Value
$cli = @([regex]::Matches($help, 'Console.WriteLine\("(?<text>(?:\\.|[^"\\])*)"\)') | ForEach-Object { [regex]::Unescape($_.Groups['text'].Value) })
if ($locales.en.rows.Count -lt 60 -or $cli.Count -lt 10) { throw 'Shortcut or CLI extraction is incomplete' }
$data = [ordered]@{ rows=$locales.en.rows; layers=$locales.en.layers; cli=$cli; locales=$locales }
$content = "/* Generated from the app shortcut registries, every Strings dictionary and CLI help. */`nwindow.KN_HELP=" + ($data | ConvertTo-Json -Depth 9 -Compress) + ";`n"
$content = $content.Replace("'", '\u0027').Replace('<', '\u003c').Replace('>', '\u003e').Replace('&', '\u0026')
if ($content.IndexOf([char]0x2013) -ge 0 -or $content.IndexOf([char]0x2014) -ge 0) { throw 'Prohibited dash in generated content' }
if ($Check) {
    if (!(Test-Path -LiteralPath $Output) -or [IO.File]::ReadAllText($Output) -cne $content) { throw 'Generated help is stale. Run generate-shortcuts.ps1' }
    Write-Host "Generated help is current ($($locales.en.rows.Count) rows, $($locales.Count) locales, $($cli.Count) CLI lines)"
} else {
    [IO.File]::WriteAllText($Output, $content, [Text.UTF8Encoding]::new($false))
    Write-Host "Generated help ($($locales.en.rows.Count) rows, $($locales.Count) locales, $($cli.Count) CLI lines)"
}
