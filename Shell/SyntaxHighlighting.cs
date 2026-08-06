using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace KillerNotes.Shell
{
    // Whole-note, mixed-language syntax highlighting. Detection is deliberately local to each
    // paragraph: a runbook can contain prose, PowerShell, HTML and JSON without choosing one
    // language for the document. Highlight spans are transient and removed before serialization.
    public partial class MainWindow
    {
        private const string SyntaxTag = "KillerNotes.SyntaxHighlight";
        private bool _syntaxHighlight;
        private bool _applyingSyntax;
        private readonly Dictionary<Paragraph, Brush> _syntaxOriginalBrushes = [];
        private readonly DispatcherTimer _syntaxTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };

        private void InitSyntaxHighlighting()
        {
            _syntaxTimer.Tick += (_, _) => { _syntaxTimer.Stop(); ApplySyntaxHighlighting(); };
        }

        private void LoadSyntaxHighlightState()
        {
            _syntaxHighlight = Equals(Editor.Document.Tag, SyntaxTag);
            SyncSyntaxButton();
            QueueSyntaxHighlighting();
        }

        private void SyntaxHighlight_Click(object sender, RoutedEventArgs e)
        {
            _syntaxHighlight = !_syntaxHighlight;
            Editor.Document.Tag = _syntaxHighlight ? SyntaxTag : null;
            SyncSyntaxButton();
            ApplySyntaxHighlighting();
            MarkDirty();
        }

        private void SyncSyntaxButton()
        {
            if (SyntaxHighlightMenuItem != null)
                SyntaxHighlightMenuItem.IsChecked = _syntaxHighlight;
        }

        private void QueueSyntaxHighlighting()
        {
            _syntaxTimer.Stop();
            if (_syntaxHighlight) _syntaxTimer.Start();
            else ClearSyntaxHighlighting();
        }

        private void ClearSyntaxHighlighting()
        {
            foreach (var saved in _syntaxOriginalBrushes.ToList())
            {
                try
                {
                    new TextRange(saved.Key.ContentStart, saved.Key.ContentEnd)
                        .ApplyPropertyValue(TextElement.ForegroundProperty, saved.Value);
                }
                catch (InvalidOperationException) { /* paragraph was removed by an edit/load */ }
            }
            _syntaxOriginalBrushes.Clear();
            foreach (var paragraph in Paragraphs(Editor.Document.Blocks))
            {
                paragraph.TextEffects = null;
                foreach (var run in Runs(paragraph.Inlines)) run.TextEffects = null;
            }
        }

        private void ApplySyntaxHighlighting()
        {
            if (_applyingSyntax) return;
            _applyingSyntax = true;
            try
            {
                ClearSyntaxHighlighting();
                if (!_syntaxHighlight || _loadingNote) return;
                foreach (var p in Paragraphs(Editor.Document.Blocks).ToList())
                {
                    string text = new TextRange(p.ContentStart, p.ContentEnd).Text.TrimEnd('\r', '\n');
                    var language = DetectLanguage(text);
                    if (language != CodeLanguage.Plain) HighlightParagraph(p, text, language);
                }
            }
            finally { _applyingSyntax = false; }
        }

        private enum CodeLanguage
        {
            Plain, PowerShell, Html, Xaml, Xml, Vue, Json, Yaml, Markdown,
            Sql, CSharp, Python, JavaScript, TypeScript, Css, Bash
        }

        private static CodeLanguage DetectLanguage(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return CodeLanguage.Plain;
            if (Regex.IsMatch(s, @"<template\b|<script\b[^>]*\bsetup\b|<style\b[^>]*\bscoped\b|\bv-(?:if|for|bind|on|model)\b|(?:^|\s)[:@][\w-]+=", RegexOptions.IgnoreCase)) return CodeLanguage.Vue;
            if (Regex.IsMatch(s, @"\bxmlns(?::\w+)?\s*=|\bx:Class\s*=|\bx:Key\s*=|\{(?:Binding|StaticResource|DynamicResource)\b|</?(?:Grid|StackPanel|DockPanel|ResourceDictionary|Window|UserControl)\b")) return CodeLanguage.Xaml;
            if (Regex.IsMatch(s, @"<!DOCTYPE\s+html|</?(?:html|head|body|div|span|script|style|a|p|table|form|input|button|section|article)\b", RegexOptions.IgnoreCase)) return CodeLanguage.Html;
            if (Regex.IsMatch(s, @"^\s*<\?xml\b|</?[A-Za-z_][\w.-]*(?::[\w.-]+)?(?:\s+[\w:.-]+\s*=|\s*/?>)", RegexOptions.IgnoreCase)) return CodeLanguage.Xml;
            if (Regex.IsMatch(s, @"^\s*[\[{].*[:\]}]", RegexOptions.Singleline) && Regex.IsMatch(s, @"""[^""]+""\s*:")) return CodeLanguage.Json;
            if (Regex.IsMatch(s, @"^\s*(?:---|\.\.\.)\s*$|^\s*(?:-\s+)?[A-Za-z_][\w.-]*\s*:\s*(?:\S.*)?$", RegexOptions.Multiline)) return CodeLanguage.Yaml;
            if (Regex.IsMatch(s, @"^\s*(?:#{1,6}\s+\S|```\w*\s*$|>\s+\S|\[[^\]]+\]\([^)]+\)|(?:[-*_]\s*){3,}$)", RegexOptions.Multiline)) return CodeLanguage.Markdown;
            if (Regex.IsMatch(s, @"\$[A-Za-z_][\w:]*|\b(?:Get|Set|New|Remove|Start|Stop|Invoke|Write|Select|Where|ForEach)-[A-Za-z]+\b", RegexOptions.IgnoreCase)) return CodeLanguage.PowerShell;
            if (Regex.IsMatch(s, @"^\s*#!.*(?:bash|sh)\b|\b(?:fi|then|elif|done|export)\b|\$\{[^}]+\}")) return CodeLanguage.Bash;
            if (Regex.IsMatch(s, @"\b(?:SELECT|INSERT|UPDATE|DELETE|CREATE|ALTER|DROP)\b.*\b(?:FROM|INTO|TABLE|SET|WHERE)\b", RegexOptions.IgnoreCase)) return CodeLanguage.Sql;
            if (Regex.IsMatch(s, @"\b(?:using\s+[\w.]+;|namespace\s+\w+|public\s+(?:class|static|void)|Console\.WriteLine)\b")) return CodeLanguage.CSharp;
            if (Regex.IsMatch(s, @"^\s*(?:def|class|from|import)\s+\w+|\b(?:print|len|range)\s*\(", RegexOptions.Multiline)) return CodeLanguage.Python;
            if (Regex.IsMatch(s, @"\b(?:interface|type|enum|namespace|implements|declare|readonly|keyof|unknown|never)\b|\b(?:const|let|function)\s+\w+\s*(?:<[^>]+>)?\s*(?:\([^)]*:[^)]*\)|:\s*[A-Za-z_$][\w.$<>\[\]| ]*)")) return CodeLanguage.TypeScript;
            if (Regex.IsMatch(s, @"(?:^|\})\s*[.#]?[A-Za-z][\w.#:[\]>+~ -]*\s*\{|\b(?:color|display|margin|padding|background|font-size|grid-template|flex-direction)\s*:\s*[^;{}]+;?", RegexOptions.IgnoreCase)) return CodeLanguage.Css;
            if (Regex.IsMatch(s, @"\b(?:const|let|var|function)\s+\w+|=>|console\.log\s*\(")) return CodeLanguage.JavaScript;
            return CodeLanguage.Plain;
        }

        private void HighlightParagraph(Paragraph paragraph, string s, CodeLanguage language)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (!_syntaxOriginalBrushes.ContainsKey(paragraph))
                _syntaxOriginalBrushes[paragraph] = paragraph.Foreground;
            var tokens = new List<(int Start, int Length, Color Color)>();
            string comments = language switch
            {
                CodeLanguage.PowerShell or CodeLanguage.Python or CodeLanguage.Yaml or CodeLanguage.Bash => @"#.*$",
                CodeLanguage.CSharp or CodeLanguage.JavaScript or CodeLanguage.TypeScript or CodeLanguage.Css => @"//.*$|/\*.*?\*/",
                CodeLanguage.Html or CodeLanguage.Xaml or CodeLanguage.Xml or CodeLanguage.Vue or CodeLanguage.Markdown => @"<!--.*?-->",
                CodeLanguage.Sql => @"--.*$|/\*.*?\*/",
                _ => ""
            };
            if (comments.Length > 0)
                Add(tokens, s, comments, Color.FromRgb(106, 153, 85), RegexOptions.Multiline | RegexOptions.Singleline);
            Add(tokens, s, @"(['""])(?:\\.|(?!\1).)*\1", Color.FromRgb(206, 145, 120));
            Add(tokens, s, @"(?<!\w)(?:\d+(?:\.\d+)?)(?!\w)", Color.FromRgb(181, 206, 168));
            if (language == CodeLanguage.PowerShell)
            {
                Add(tokens, s, @"\$(?:true|false|null)\b", Color.FromRgb(197, 134, 192), RegexOptions.IgnoreCase);
                Add(tokens, s, @"\$[A-Za-z_][\w:]*", Color.FromRgb(156, 220, 254));
                Add(tokens, s, @"\b(?:Get|Set|New|Remove|Start|Stop|Invoke|Write|Select|Where|ForEach|Test|Sort|Export|Import|Update|Add|Clear|Copy|Move|Out|ConvertTo|ConvertFrom|Measure|Compare)-[A-Za-z]+\b",
                    Color.FromRgb(78, 201, 176), RegexOptions.IgnoreCase);
                Add(tokens, s, @"(?<!\w)-[A-Za-z][\w-]*", Color.FromRgb(156, 220, 254));
                Add(tokens, s, @"\[[A-Za-z_][\w.]*\]", Color.FromRgb(78, 201, 176));
                Add(tokens, s, @"\b[A-Za-z_]\w*(?=\s*=)", Color.FromRgb(156, 220, 254));
                Add(tokens, s, @"(?:\|\||&&|==|!=|-eq\b|-ne\b|-like\b|-match\b|-and\b|-or\b|[|=@{}()[\];])",
                    Color.FromRgb(197, 134, 192), RegexOptions.IgnoreCase);
                Add(tokens, s, @"\b(?:gpupdate|gpresult|ping|ipconfig|nslookup|robocopy|netsh|winget|dotnet|git)\b",
                    Color.FromRgb(78, 201, 176), RegexOptions.IgnoreCase);
            }
            string keywords = language switch
            {
                CodeLanguage.PowerShell => @"\b(?:function|param|if|else|elseif|foreach|for|while|switch|return|try|catch|finally|throw|class|filter|begin|process|end|in|break|continue|trap|data|dynamicparam)\b",
                CodeLanguage.Html => @"</?[A-Za-z][\w:-]*|\b(?:class|id|href|src|style|data-[\w-]+)(?=\s*=)",
                CodeLanguage.Xaml => @"</?[A-Za-z][\w:.-]*|\b(?:x:Class|x:Key|x:Name|xmlns(?::\w+)?|Grid\.(?:Row|Column)|RowDefinition|ColumnDefinition)(?=\s*=)?|\{(?:Binding|StaticResource|DynamicResource)\b",
                CodeLanguage.Xml => @"</?[A-Za-z_][\w:.-]*|\b[A-Za-z_][\w:.-]*(?=\s*=)|<\?xml|\?>",
                CodeLanguage.Vue => @"</?[A-Za-z][\w:.-]*|\b(?:template|script|style|setup|scoped|v-if|v-else|v-for|v-bind|v-on|v-model|defineProps|defineEmits|ref|computed)(?=\b|\s*=)|(?:^|\s)[:@][\w-]+(?=\s*=)",
                CodeLanguage.Json => @"\b(?:true|false|null)\b|""[^""]+""(?=\s*:)",
                CodeLanguage.Yaml => @"(?m)^\s*(?:-\s+)?[A-Za-z_][\w.-]*(?=\s*:)|\b(?:true|false|null|yes|no|on|off)\b|[&*!|>]\w*",
                CodeLanguage.Markdown => @"(?m)^\s*#{1,6}(?=\s)|```\w*|`[^`]+`|\*\*|__|~~|(?<=\])\([^)]+\)",
                CodeLanguage.Sql => @"\b(?:SELECT|FROM|WHERE|JOIN|ON|INSERT|INTO|UPDATE|SET|DELETE|CREATE|ALTER|DROP|TABLE|VALUES|AS|AND|OR|NULL|ORDER|GROUP|BY|HAVING|LIMIT)\b",
                CodeLanguage.CSharp => @"\b(?:using|namespace|class|struct|interface|public|private|protected|internal|static|readonly|void|string|int|bool|var|new|return|if|else|foreach|for|while|try|catch|throw|null|true|false|async|await)\b",
                CodeLanguage.Python => @"\b(?:def|class|from|import|as|return|if|elif|else|for|while|try|except|finally|raise|with|lambda|True|False|None|async|await|in|is|not|and|or)\b",
                CodeLanguage.JavaScript => @"\b(?:const|let|var|function|class|return|if|else|for|while|try|catch|throw|null|undefined|true|false|async|await|new|import|export|from)\b",
                CodeLanguage.TypeScript => @"\b(?:const|let|var|function|class|interface|type|enum|namespace|implements|extends|public|private|protected|readonly|abstract|declare|keyof|typeof|unknown|never|any|string|number|boolean|void|return|if|else|for|while|try|catch|throw|null|undefined|true|false|async|await|new|import|export|from|as|in|of)\b",
                CodeLanguage.Css => @"[#.]?[A-Za-z][\w-]*(?=\s*\{)|--[\w-]+|\b(?:color|display|position|margin|padding|background|border|width|height|font|font-size|grid|flex|align-items|justify-content|var|calc|rgb|rgba|url)(?=\s*[:(])|#[0-9a-fA-F]{3,8}\b",
                CodeLanguage.Bash => @"\b(?:if|then|elif|else|fi|for|while|do|done|case|esac|function|export|local|return|in)\b|\$\{?\w+\}?",
                _ => ""
            };
            if (keywords.Length > 0) Add(tokens, s, keywords, Color.FromRgb(86, 156, 214), RegexOptions.IgnoreCase);
            foreach (var token in tokens.OrderByDescending(t => t.Start))
            {
                var start = PositionAtCharacter(paragraph.ContentStart, paragraph.ContentEnd, token.Start);
                var end = PositionAtCharacter(paragraph.ContentStart, paragraph.ContentEnd, token.Start + token.Length);
                if (start != null && end != null && start.CompareTo(end) < 0)
                {
                    try
                    {
                        new TextRange(start, end).ApplyPropertyValue(
                            TextElement.ForegroundProperty, new SolidColorBrush(token.Color));
                    }
                    catch (InvalidOperationException) { /* document changed during deferred refresh */ }
                }
            }
        }

        private static void Add(List<(int Start, int Length, Color Color)> dst, string text, string pattern, Color color, RegexOptions options = RegexOptions.None)
        {
            foreach (Match m in Regex.Matches(text, pattern, options))
                if (m.Length > 0 && !dst.Any(t => m.Index < t.Start + t.Length && t.Start < m.Index + m.Length))
                    dst.Add((m.Index, m.Length, color));
        }

        private static TextPointer? PositionAtCharacter(TextPointer start, TextPointer end, int offset)
        {
            int seen = 0;
            TextPointer? p = start;
            while (p != null && p.CompareTo(end) < 0)
            {
                if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string text = p.GetTextInRun(LogicalDirection.Forward);
                    if (seen + text.Length >= offset)
                        return p.GetPositionAtOffset(offset - seen, LogicalDirection.Forward);
                    seen += text.Length;
                    p = p.GetPositionAtOffset(text.Length, LogicalDirection.Forward);
                }
                else p = p.GetNextContextPosition(LogicalDirection.Forward);
            }
            return offset == seen ? end : null;
        }

        private static IEnumerable<Paragraph> Paragraphs(IEnumerable<Block> blocks)
        {
            foreach (var b in blocks)
                if (b is Paragraph p) yield return p;
                else if (b is Section s) foreach (var p2 in Paragraphs(s.Blocks)) yield return p2;
                else if (b is List l) foreach (var i in l.ListItems) foreach (var p2 in Paragraphs(i.Blocks)) yield return p2;
                else if (b is Table t) foreach (var g in t.RowGroups) foreach (var r in g.Rows) foreach (var c in r.Cells) foreach (var p2 in Paragraphs(c.Blocks)) yield return p2;
        }

        private static IEnumerable<Run> Runs(IEnumerable<Block> blocks) => Paragraphs(blocks).SelectMany(p => Runs(p.Inlines));
        private static IEnumerable<Run> Runs(IEnumerable<Inline> inlines)
        {
            foreach (var i in inlines)
                if (i is Run r) yield return r;
                else if (i is Span s) foreach (var r2 in Runs(s.Inlines)) yield return r2;
        }
    }
}
