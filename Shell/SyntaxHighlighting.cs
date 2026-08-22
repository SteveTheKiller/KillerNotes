using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
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

        // ── Token colors ────────────────────────────────────────────────────────
        //
        // These were the VS Code Dark+ hexes, written straight into the Add() calls. That is a
        // palette designed for ONE background - a near-black editor - and on a white page it
        // falls apart: the pale green number color (#B5CEA8) and the muted comment green
        // (#6A9955) both land around 1.8:1 against white, which is not readable text, it is a
        // watermark. 98SE and Light are white-paged, so they were the worst hit, but every light
        // accent had the same problem. (2026-08-07)
        //
        // Each role resolves through the theme dictionary and falls back to the Dark+ value, so a
        // theme that says nothing looks exactly as it did. A theme with a light page states the
        // seven keys and gets a palette built for its own background.
        private static Color Syn(string key, byte r, byte g, byte b)
        {
            if (Application.Current?.TryFindResource(key) is SolidColorBrush sb) return sb.Color;
            return Color.FromRgb(r, g, b);
        }

        private static Color SynComment  => Syn("Syn_Comment",  106, 153,  85);
        private static Color SynString   => Syn("Syn_String",   206, 145, 120);
        private static Color SynNumber   => Syn("Syn_Number",   181, 206, 168);
        private static Color SynVariable => Syn("Syn_Variable", 156, 220, 254);
        private static Color SynType     => Syn("Syn_Type",      78, 201, 176);
        private static Color SynOperator => Syn("Syn_Operator", 197, 134, 192);
        private static Color SynKeyword  => Syn("Syn_Keyword",   86, 156, 214);
        private bool _syntaxHighlight;
        private bool _applyingSyntax;
        // The paragraphs currently carrying token colors. Restoring one CLEARS its local
        // foregrounds (RestoreParagraphForeground) - never re-applies a captured brush. The old
        // code saved paragraph.Foreground and painted it back, which baked the PREVIOUS theme's
        // color in as an unthemed local value: switch a dark theme to 98SE and the restored
        // text was white on the white page, so 98SE showed white syntax highlighting
        // (2026-08-08).
        private readonly HashSet<Paragraph> _syntaxColored = [];
        // The text each paragraph was last highlighted FOR, the language it resolved to (its
        // own detection or the contagion context - the Lang half seeds the next pass's context
        // mid-document), and the DOCUMENT VERSION it was validated at. The version is the
        // whole scroll-performance story: text can only change on a real edit (the syntax
        // writes are _applyingSyntax-guarded out of TextChanged), so between edits a painted
        // paragraph is skipped on a LONG COMPARE - no TextRange.Text materialisation, no
        // string allocation, nothing. Reading every visible paragraph's text once per scroll
        // FRAME was the sluggishness; scrolling has to feel instant (2026-08-08).
        private readonly Dictionary<Paragraph, (string Text, CodeLanguage Lang, long Version)> _syntaxSeen = [];
        // Bumped on every edit path (QueueSyntaxHighlighting); a pass revalidates a paragraph
        // by text compare only when its stored version is stale, then re-stamps it.
        private long _syntaxDocVersion;
        // Cached flat paragraph list, rebuilt only after edits (the gutter's _gutterBlocksDirty
        // pattern): scroll passes run once per FRAME, and re-walking a 6000-block document per
        // frame is allocation churn for nothing when the structure has not changed.
        private List<Paragraph> _syntaxFlat = [];
        private readonly Dictionary<Paragraph, int> _syntaxFlatIndex = [];
        private bool _syntaxFlatDirty = true;
        private bool _syntaxRepaintQueued;
        // Never tokenise a pathological paragraph. net48's regex engine matches recursively on
        // the thread stack, and one enormous paragraph (a whole script pasted as LineBreaks, a
        // minified blob) is how the paste crashed with a StackOverflowException. VS Code caps
        // per-line tokenisation the same way.
        private const int MaxHighlightChars = 20000;
        private static readonly TimeSpan SynRegexTimeout = TimeSpan.FromMilliseconds(250);
        private readonly DispatcherTimer _syntaxTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };

        private void InitSyntaxHighlighting()
        {
            _syntaxTimer.Tick += (_, _) => { _syntaxTimer.Stop(); ApplySyntaxHighlighting(); };
            // A theme switch changes the Syn_* palette, and the incremental skip would otherwise
            // keep every unchanged paragraph on the old colors.
            KillerNotes.Services.ThemeManager.ThemeChanged += () =>
            {
                _syntaxSeen.Clear();
                QueueSyntaxHighlighting();
            };
            // Passes are viewport-scoped, so scrolling into unpainted territory must fill in -
            // and it must fill in NOW, not on the 350ms edit debounce: the debounce restarts on
            // every scroll tick, so text sat uncolored for seconds after a scroll, which reads
            // as bad performance (2026-08-08). Per-frame coalesced, the
            // same shape as the gutter's QueueGutterRepaint; already-painted regions cost only
            // dictionary lookups.
            Editor.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((_, _) => { if (_syntaxHighlight) RepaintSyntaxSoon(); }));
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
            // Every caller of this path is an EDIT (or a toggle/note switch), so the document
            // structure may have changed and the cached flat list must rebuild - and every
            // cached paragraph needs revalidation (one text compare each, once).
            _syntaxFlatDirty = true;
            _syntaxDocVersion++;
            _syntaxTimer.Stop();
            if (_syntaxHighlight) _syntaxTimer.Start();
            else ClearSyntaxHighlighting();
        }

        /// <summary>Immediate repaint, coalesced to one per frame - the scroll path. Loaded
        /// priority runs after the frame's layout, so the rects read are current.</summary>
        private void RepaintSyntaxSoon()
        {
            if (_syntaxRepaintQueued) return;
            _syntaxRepaintQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _syntaxRepaintQueued = false;
                ApplySyntaxHighlighting();
            }), DispatcherPriority.Loaded);
        }

        private void ClearSyntaxHighlighting()
        {
            // GUARDED and BATCHED. The un-coloring writes are document changes like any other:
            // unguarded, they fired Editor_TextChanged -> MarkDirty from INSIDE SaveCurrentNote's
            // pre-serialize clear (re-dirtying the note the save was about to mark clean), and
            // unbatched, every write was its own TextChanged + layout pass - thousands on a big
            // note (2026-08-08).
            bool was = _applyingSyntax;
            _applyingSyntax = true;
            Editor.BeginChange();
            try
            {
                foreach (var colored in _syntaxColored.ToList())
                {
                    try { RestoreParagraphForeground(colored); }
                    catch (InvalidOperationException) { /* paragraph was removed by an edit/load */ }
                }
                _syntaxColored.Clear();
                _syntaxSeen.Clear();
                foreach (var paragraph in Paragraphs(Editor.Document.Blocks))
                {
                    paragraph.TextEffects = null;
                    foreach (var run in Runs(paragraph.Inlines)) run.TextEffects = null;
                }
            }
            finally
            {
                Editor.EndChange();
                _applyingSyntax = was;
            }
        }

        // INCREMENTAL and VIEWPORT-SCOPED, the same two rules as the LineNumbers rewrite. The
        // original cleared and re-tokenised the ENTIRE document on every pass - and a pass ran
        // 350ms after every keystroke AND inside every 2s autosave - so a pasted 960-line script
        // meant seconds of Not Responding per edit and a 20-second theme switch, and the regex
        // walk over pathological paragraphs could overflow the stack outright (2026-08-08).
        //   1. Only paragraphs in (or just around) the viewport are considered; scrolling queues
        //      a fill-in pass for territory not yet painted.
        //   2. A paragraph whose text matches what it was last painted for is skipped whole -
        //      no clear, no detection, no tokens. Only CHANGED paragraphs pay anything.
        //   3. All writes ride ONE BeginChange block: one TextChanged, one layout pass.
        private void ApplySyntaxHighlighting()
        {
            if (_applyingSyntax) return;
            _applyingSyntax = true;
            try
            {
                if (!_syntaxHighlight || _loadingNote)
                {
                    ClearSyntaxHighlighting();
                    return;
                }

                // The flat list, its index, and the cache PRUNE all ride the dirty flag: the
                // document's structure can only change on an edit, so a SCROLL pass reuses all
                // three untouched. Rebuilding them per frame was a full document walk plus a
                // thousand-entry set and two LINQ prunes of allocation churn, every frame of a
                // scroll, and the performance showed it (2026-08-08).
                if (_syntaxFlatDirty)
                {
                    _syntaxFlat = Paragraphs(Editor.Document.Blocks).ToList();
                    _syntaxFlatIndex.Clear();
                    for (int fi = 0; fi < _syntaxFlat.Count; fi++) _syntaxFlatIndex[_syntaxFlat[fi]] = fi;
                    // Prune cache entries whose paragraphs left the document, so a deleted-and-
                    // replaced paragraph cannot inherit a stale skip or stay marked colored.
                    var live = new HashSet<Paragraph>(_syntaxFlat);
                    foreach (var dead in _syntaxSeen.Keys.Where(p => !live.Contains(p)).ToList())
                        _syntaxSeen.Remove(dead);
                    _syntaxColored.RemoveWhere(p => !live.Contains(p));
                    _syntaxFlatDirty = false;
                }
                var flat = _syntaxFlat;
                if (flat.Count == 0) return;

                int first = FirstVisibleSyntaxIndex(flat);
                double viewBottom = Editor.ActualHeight + 200;   // a margin below, so a small scroll is already painted
                // LAZY change block, and a per-pass paint budget. Opening BeginChange/EndChange
                // unconditionally made every scroll pass - even a pure-read one over painted
                // territory - end in a layout invalidation of a huge fragmented document, and a
                // flick into unpainted territory painted the whole runway in one frame; together
                // they starved input until the note stopped scrolling at all after a few
                // seconds (2026-08-08). A read-only pass now touches
                // no change machinery, and painting spreads across frames.
                bool changeOpen = false;
                int paintBudget = 12;
                try
                {
                    int emptyStreak = 0;
                    int start = Math.Max(0, first - 2);
                    // Language CONTEXT for contagion. Most lines of a pasted code block carry no
                    // language tell of their own - "base.OnStartup(e);" is anonymous - so
                    // per-paragraph detection left whole blocks of real code uncolored
                    // (2026-08-08). An anonymous
                    // line that LOOKS like code inherits the language of the lines above it;
                    // blank lines carry the context through; a prose line breaks the block.
                    var context = CodeLanguage.Plain;
                    if (start > 0 && _syntaxSeen.TryGetValue(flat[start - 1], out var seed))
                        context = seed.Lang;
                    for (int i = start; i < flat.Count; i++)
                    {
                        var p = flat[i];
                        Rect rect;
                        try { rect = p.ContentStart.GetCharacterRect(LogicalDirection.Forward); }
                        catch (InvalidOperationException) { break; }
                        if (rect.IsEmpty)
                        {
                            // Not laid out yet (big note still formatting in the background).
                            // Walking on would FORCE that formatting - the LineNumbers rule.
                            if (++emptyStreak >= 3) break;
                            continue;
                        }
                        emptyStreak = 0;
                        if (rect.Top > viewBottom) break;      // below the viewport: a later pass fills in
                        if (rect.Bottom < -200) continue;      // above it

                        // Version first, text second, tokens last. Same document version = no
                        // edit since this paragraph was validated, so its text CANNOT differ -
                        // skip on a long compare, no TextRange.Text materialisation. Reading
                        // every visible paragraph's text once per scroll FRAME was the string
                        // churn behind the sluggish scrolling. After an edit, one text compare
                        // revalidates and re-stamps.
                        bool had = _syntaxSeen.TryGetValue(p, out var prev);
                        if (had && prev.Version == _syntaxDocVersion)
                        {
                            context = prev.Lang;
                            continue;
                        }
                        string text = new TextRange(p.ContentStart, p.ContentEnd).Text.TrimEnd('\r', '\n');
                        if (had && prev.Text == text)
                        {
                            _syntaxSeen[p] = (prev.Text, prev.Lang, _syntaxDocVersion);
                            context = prev.Lang;
                            continue;
                        }

                        // Out of budget for this frame: queue a continuation and stop before
                        // touching this paragraph - it is untouched, so the next pass resumes
                        // here naturally.
                        if (paintBudget <= 0)
                        {
                            RepaintSyntaxSoon();
                            break;
                        }
                        paintBudget--;
                        if (!changeOpen) { Editor.BeginChange(); changeOpen = true; }

                        // The paragraph changed: drop its local foregrounds first so stale token
                        // colors cannot linger where the tokens moved.
                        if (_syntaxColored.Remove(p))
                        {
                            try { RestoreParagraphForeground(p); }
                            catch (InvalidOperationException) { }
                        }
                        if (text.Length > MaxHighlightChars)
                        {
                            // The StackOverflow guard. A giant blob carries the block onward
                            // rather than breaking it.
                            _syntaxSeen[p] = (text, context, _syntaxDocVersion);
                            continue;
                        }
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            _syntaxSeen[p] = (text, context, _syntaxDocVersion);   // blank line inside a block
                            continue;
                        }
                        // A markdown note is markdown on every line, so skip detection for one
                        // (Markdown.cs). Detection is per paragraph, and an ordinary prose line
                        // between two headings carries no signal of its own - it came back Plain
                        // and lost its list and emphasis markers while its neighbors kept theirs.
                        var language = CurrentIsMarkdown ? CodeLanguage.Markdown : DetectLanguage(text);
                        if (language == CodeLanguage.Plain && context != CodeLanguage.Plain && LooksLikeCode(text))
                            language = context;
                        context = language;
                        _syntaxSeen[p] = (text, language, _syntaxDocVersion);
                        if (language != CodeLanguage.Plain) HighlightParagraph(p, text, language);
                    }
                }
                finally { if (changeOpen) Editor.EndChange(); }
            }
            finally { _applyingSyntax = false; }
        }

        /// <summary>Index in the flat paragraph list of the paragraph under the editor's top-left
        /// corner - a viewport-local query, no document walk. 0 when it cannot tell.</summary>
        private int FirstVisibleSyntaxIndex(List<Paragraph> flat)
        {
            try
            {
                var pos = Editor.GetPositionFromPoint(new Point(2, 2), true);
                if (pos?.Paragraph is Paragraph para
                    && _syntaxFlatIndex.TryGetValue(para, out int i))
                    return i;   // O(1) via the cached index - IndexOf was a per-frame reference scan
            }
            catch (InvalidOperationException) { }
            return 0;
        }

        private enum CodeLanguage
        {
            Plain, PowerShell, Html, Xaml, Xml, Vue, Json, Yaml, Markdown,
            Sql, CSharp, Python, JavaScript, TypeScript, Css, Bash
        }

        private static CodeLanguage DetectLanguage(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return CodeLanguage.Plain;
            if (SafeIsMatch(s, @"<template\b|<script\b[^>]*\bsetup\b|<style\b[^>]*\bscoped\b|\bv-(?:if|for|bind|on|model)\b|(?:^|\s)[:@][\w-]+=", RegexOptions.IgnoreCase)) return CodeLanguage.Vue;
            if (SafeIsMatch(s, @"\bxmlns(?::\w+)?\s*=|\bx:Class\s*=|\bx:Key\s*=|\{(?:Binding|StaticResource|DynamicResource)\b|</?(?:Grid|StackPanel|DockPanel|ResourceDictionary|Window|UserControl)\b")) return CodeLanguage.Xaml;
            if (SafeIsMatch(s, @"<!DOCTYPE\s+html|</?(?:html|head|body|div|span|script|style|a|p|table|form|input|button|section|article)\b", RegexOptions.IgnoreCase)) return CodeLanguage.Html;
            if (SafeIsMatch(s, @"^\s*<\?xml\b|</?[A-Za-z_][\w.-]*(?::[\w.-]+)?(?:\s+[\w:.-]+\s*=|\s*/?>)", RegexOptions.IgnoreCase)) return CodeLanguage.Xml;
            if (SafeIsMatch(s, @"^\s*[\[{].*[:\]}]", RegexOptions.Singleline) && SafeIsMatch(s, @"""[^""]+""\s*:")) return CodeLanguage.Json;
            if (SafeIsMatch(s, @"^\s*(?:---|\.\.\.)\s*$|^\s*(?:-\s+)?[A-Za-z_][\w.-]*\s*:\s*(?:\S.*)?$", RegexOptions.Multiline)) return CodeLanguage.Yaml;
            if (SafeIsMatch(s, @"^\s*(?:#{1,6}\s+\S|```\w*\s*$|>\s+\S|\[[^\]]+\]\([^)]+\)|(?:[-*_]\s*){3,}$)|\*\*[^*\r\n]+\*\*|`[^`\r\n]+`", RegexOptions.Multiline)) return CodeLanguage.Markdown;
            // The verb list mirrors the PowerShell token pattern in HighlightParagraph - keep the
            // two in step. The short original missed real cmdlets outright: "Publish-Module -Path
            // ..." detected as nothing at all (2026-08-08).
            if (SafeIsMatch(s, @"\$[A-Za-z_][\w:]*|\b(?:Add|Approve|Assert|Backup|Block|Build|Checkpoint|Clear|Close|Compare|Complete|Compress|Confirm|Connect|ConvertFrom|ConvertTo|Convert|Copy|Debug|Deny|Deploy|Disable|Disconnect|Dismount|Edit|Enable|Enter|Exit|Expand|Export|Find|ForEach|Format|Get|Grant|Group|Hide|Import|Initialize|Install|Invoke|Join|Limit|Lock|Measure|Merge|Mount|Move|New|Open|Optimize|Out|Ping|Pop|Protect|Publish|Push|Read|Receive|Redo|Register|Remove|Rename|Repair|Request|Reset|Resize|Resolve|Restart|Restore|Resume|Revoke|Save|Search|Select|Send|Set|Show|Skip|Sort|Split|Start|Step|Stop|Submit|Suspend|Switch|Sync|Test|Trace|Unblock|Undo|Uninstall|Unlock|Unprotect|Unpublish|Unregister|Update|Use|Wait|Watch|Where|Write)-[A-Za-z]+\b", RegexOptions.IgnoreCase)) return CodeLanguage.PowerShell;
            if (SafeIsMatch(s, @"^\s*#!.*(?:bash|sh)\b|\b(?:fi|then|elif|done|export)\b|\$\{[^}]+\}")) return CodeLanguage.Bash;
            if (SafeIsMatch(s, @"\b(?:SELECT|INSERT|UPDATE|DELETE|CREATE|ALTER|DROP)\b.*\b(?:FROM|INTO|TABLE|SET|WHERE)\b", RegexOptions.IgnoreCase)) return CodeLanguage.Sql;
            // Method signatures and base/this calls included: real C# pasted line-by-line is
            // mostly lines like "protected override void OnStartup(StartupEventArgs e)", which
            // the original tells ("using ...;", "public class") never matched (2026-08-08).
            if (SafeIsMatch(s, @"\busing\s+[\w.]+;|\bnamespace\s+\w+|\bpublic\s+(?:class|static|void)\b|\bConsole\.WriteLine\b|\b(?:public|private|protected|internal)\s+(?:(?:static|override|virtual|sealed|async|abstract|readonly)\s+)*[\w<>\[\],.]+\s+\w+\s*\(|\bbase\.\w+\s*\(")) return CodeLanguage.CSharp;
            if (SafeIsMatch(s, @"^\s*(?:def|class|from|import)\s+\w+|\b(?:print|len|range)\s*\(", RegexOptions.Multiline)) return CodeLanguage.Python;
            if (SafeIsMatch(s, @"\b(?:interface|type|enum|namespace|implements|declare|readonly|keyof|unknown|never)\b|\b(?:const|let|function)\s+\w+\s*(?:<[^>]+>)?\s*(?:\([^)]*:[^)]*\)|:\s*[A-Za-z_$][\w.$<>\[\]| ]*)")) return CodeLanguage.TypeScript;
            if (SafeIsMatch(s, @"(?:^|\})\s*[.#]?[A-Za-z][\w.#:[\]>+~ -]*\s*\{|\b(?:color|display|margin|padding|background|font-size|grid-template|flex-direction)\s*:\s*[^;{}]+;?", RegexOptions.IgnoreCase)) return CodeLanguage.Css;
            if (SafeIsMatch(s, @"\b(?:const|let|var|function)\s+\w+|=>|console\.log\s*\(")) return CodeLanguage.JavaScript;
            return CodeLanguage.Plain;
        }

        private void HighlightParagraph(Paragraph paragraph, string s, CodeLanguage language)
        {
            if (string.IsNullOrEmpty(s)) return;
            _syntaxColored.Add(paragraph);
            var tokens = new List<(int Start, int Length, Color Color)>();
            string comments = language switch
            {
                // PowerShell is split out for <# ... #> block comments, which comment-based help
                // uses for every .SYNOPSIS header. On the shared "#.*$" it highlighted only the
                // lines that literally began with #, so a help block came out striped.
                CodeLanguage.PowerShell => @"<#[\s\S]*?#>|#[^\n]*",
                CodeLanguage.Python or CodeLanguage.Yaml or CodeLanguage.Bash => @"#.*$",
                CodeLanguage.CSharp or CodeLanguage.JavaScript or CodeLanguage.TypeScript or CodeLanguage.Css => @"//.*$|/\*.*?\*/",
                CodeLanguage.Html or CodeLanguage.Xaml or CodeLanguage.Xml or CodeLanguage.Vue or CodeLanguage.Markdown => @"<!--.*?-->",
                CodeLanguage.Sql => @"--.*$|/\*.*?\*/",
                _ => ""
            };
            if (comments.Length > 0)
                Add(tokens, s, comments, SynComment, RegexOptions.Multiline | RegexOptions.Singleline);
            Add(tokens, s, @"(['""])(?:\\.|(?!\1).)*\1", SynString);
            Add(tokens, s, @"(?<!\w)(?:\d+(?:\.\d+)?)(?!\w)", SynNumber);
            if (language == CodeLanguage.PowerShell)
            {
                Add(tokens, s, @"\$(?:true|false|null)\b", SynOperator, RegexOptions.IgnoreCase);
                Add(tokens, s, @"\$[A-Za-z_][\w:]*", SynVariable);
                // The FULL approved-verb set (Get-Verb), not a hand-picked subset. The old list of
                // 45 missed Expand, Compress, Rename, Split, Mount, Enter, Exit, Group, Repair,
                // Search, Grant, Revoke, Suspend, Resume, Initialize and more - every one of which
                // fell through to the parameter rule below, which painted the -Noun half as a
                // parameter and left the verb uncolored. ConvertFrom/ConvertTo precede Convert so
                // the longer names win the alternation.
                Add(tokens, s, @"\b(?:Add|Approve|Assert|Backup|Block|Build|Checkpoint|Clear|Close|Compare|Complete|Compress|Confirm|Connect|ConvertFrom|ConvertTo|Convert|Copy|Debug|Deny|Deploy|Disable|Disconnect|Dismount|Edit|Enable|Enter|Exit|Expand|Export|Find|ForEach|Format|Get|Grant|Group|Hide|Import|Initialize|Install|Invoke|Join|Limit|Lock|Measure|Merge|Mount|Move|New|Open|Optimize|Out|Ping|Pop|Protect|Publish|Push|Read|Receive|Redo|Register|Remove|Rename|Repair|Request|Reset|Resize|Resolve|Restart|Restore|Resume|Revoke|Save|Search|Select|Send|Set|Show|Skip|Sort|Split|Start|Step|Stop|Submit|Suspend|Switch|Sync|Test|Trace|Unblock|Undo|Uninstall|Unlock|Unprotect|Unpublish|Unregister|Update|Use|Wait|Watch|Where|Write)-[A-Za-z]+\b",
                    SynType, RegexOptions.IgnoreCase);
                // BEFORE the parameter rule: Add() keeps the FIRST match and skips overlaps, so an
                // operator rule placed after it can never fire. The symbolic-operator rule further
                // down did list -eq and friends, and had been dead code for exactly that reason -
                // every comparison and logical operator was coloring as a parameter.
                Add(tokens, s, @"(?<!\w)-(?:[ci]?(?:eq|ne|lt|gt|le|ge|notlike|notmatch|notcontains|notin|like|match|replace|contains|in)|isnot|is|as|and|or|xor|not|band|bor|bxor|bnot|shl|shr|join|split|f)\b",
                    SynOperator, RegexOptions.IgnoreCase);
                Add(tokens, s, @"(?<!\w)-[A-Za-z][\w-]*", SynVariable);
                Add(tokens, s, @"\[[A-Za-z_][\w.]*\]", SynType);
                Add(tokens, s, @"\b[A-Za-z_]\w*(?=\s*=)", SynVariable);
                // Symbols only. The -word operators moved above the parameter rule, where they can
                // actually match; listing them here again would be dead weight.
                Add(tokens, s, @"(?:\|\||&&|==|!=|[|=@{}()[\];])",
                    SynOperator, RegexOptions.IgnoreCase);
                Add(tokens, s, @"\b(?:gpupdate|gpresult|ping|ipconfig|nslookup|robocopy|netsh|winget|dotnet|git)\b",
                    SynType, RegexOptions.IgnoreCase);
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
                // Whole SPANS, not just the markers: **bold text**, __bold__, *italic*, ~~gone~~
                // color the content too, and list/quote markers read as structure - Markdown
                // detection needs to reach bold text and the rest (2026-08-08).
                CodeLanguage.Markdown => @"(?m)^\s*#{1,6}(?=\s)|```\w*|`[^`\r\n]+`|\*\*[^*\r\n]+\*\*|__[^_\r\n]+__|~~[^~\r\n]+~~|\*[^*\r\n]+\*|^\s*[-*+](?=\s)|^\s*\d+\.(?=\s)|^\s*>(?=\s)|(?<=\])\([^)]+\)",
                CodeLanguage.Sql => @"\b(?:SELECT|FROM|WHERE|JOIN|ON|INSERT|INTO|UPDATE|SET|DELETE|CREATE|ALTER|DROP|TABLE|VALUES|AS|AND|OR|NULL|ORDER|GROUP|BY|HAVING|LIMIT)\b",
                CodeLanguage.CSharp => @"\b(?:using|namespace|class|struct|interface|public|private|protected|internal|static|readonly|void|string|int|bool|var|new|return|if|else|foreach|for|while|try|catch|throw|null|true|false|async|await)\b",
                CodeLanguage.Python => @"\b(?:def|class|from|import|as|return|if|elif|else|for|while|try|except|finally|raise|with|lambda|True|False|None|async|await|in|is|not|and|or)\b",
                CodeLanguage.JavaScript => @"\b(?:const|let|var|function|class|return|if|else|for|while|try|catch|throw|null|undefined|true|false|async|await|new|import|export|from)\b",
                CodeLanguage.TypeScript => @"\b(?:const|let|var|function|class|interface|type|enum|namespace|implements|extends|public|private|protected|readonly|abstract|declare|keyof|typeof|unknown|never|any|string|number|boolean|void|return|if|else|for|while|try|catch|throw|null|undefined|true|false|async|await|new|import|export|from|as|in|of)\b",
                CodeLanguage.Css => @"[#.]?[A-Za-z][\w-]*(?=\s*\{)|--[\w-]+|\b(?:color|display|position|margin|padding|background|border|width|height|font|font-size|grid|flex|align-items|justify-content|var|calc|rgb|rgba|url)(?=\s*[:(])|#[0-9a-fA-F]{3,8}\b",
                CodeLanguage.Bash => @"\b(?:if|then|elif|else|fi|for|while|do|done|case|esac|function|export|local|return|in)\b|\$\{?\w+\}?",
                _ => ""
            };
            if (keywords.Length > 0) Add(tokens, s, keywords, SynKeyword, RegexOptions.IgnoreCase);
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
            // Timeout-guarded: net48's regex engine backtracks recursively, and a pathological
            // paragraph can otherwise hang the UI or overflow the stack (2026-08-08). On timeout
            // the tokens found so far are simply not added - an unhighlighted paragraph, not a
            // crashed app.
            try
            {
                foreach (Match m in Regex.Matches(text, pattern, options, SynRegexTimeout))
                    if (m.Length > 0 && !dst.Any(t => m.Index < t.Start + t.Length && t.Start < m.Index + m.Length))
                        dst.Add((m.Index, m.Length, color));
            }
            catch (RegexMatchTimeoutException) { }
        }

        /// <summary>Cheap "is this line code-shaped" test for contagion - never a language
        /// detector, just enough to keep prose from inheriting a code block's language:
        /// indented, ends like a statement, or starts like a comment or brace.</summary>
        private static bool LooksLikeCode(string s)
        {
            if (char.IsWhiteSpace(s[0])) return true;               // code blocks are indented
            string t = s.TrimEnd();
            char last = t[t.Length - 1];
            if (last is ';' or '{' or '}' or ')') return true;      // statement / brace / call line
            return t.StartsWith("//") || t.StartsWith("#") || t.StartsWith("/*")
                || t.StartsWith("}") || t.StartsWith("{");
        }

        /// <summary>Timeout-guarded IsMatch for language detection, same reasoning as Add.</summary>
        private static bool SafeIsMatch(string s, string pattern, RegexOptions options = RegexOptions.None)
        {
            try { return Regex.IsMatch(s, pattern, options, SynRegexTimeout); }
            catch (RegexMatchTimeoutException) { return false; }
        }

        /// <summary>Removes every LOCAL foreground inside the paragraph, so its text falls back
        /// to the theme-inherited editor brush - correct on whatever theme is active NOW and
        /// after every future switch. (The flip side: a user-applied text color inside a
        /// code-detected paragraph is cleared too - the old captured-brush restore flattened
        /// those identically, so nothing is lost that was ever kept.)</summary>
        private static void RestoreParagraphForeground(Paragraph p)
        {
            p.ClearValue(TextElement.ForegroundProperty);
            ClearInlineForegrounds(p.Inlines);
        }

        private static void ClearInlineForegrounds(IEnumerable<Inline> inlines)
        {
            foreach (var i in inlines)
            {
                i.ClearValue(TextElement.ForegroundProperty);
                if (i is Span s) ClearInlineForegrounds(s.Inlines);
            }
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
