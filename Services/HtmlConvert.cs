// ═══════════════════════════════════════════════════════════
//  HTML CONVERSION  -  HTML note text to a FlowDocument
// ═══════════════════════════════════════════════════════════
//
// Drives the preview pane for HTML notes (Shell/Preview.cs). The preview is drawn by WPF, not
// by a hosted browser, so an HTML note is read into the same document model the editor uses.
//
// Deliberately a reader, not a browser engine. It understands the structure a note actually
// carries (paragraphs, headings, lists, quotes, code, tables, links, emphasis, breaks, rules)
// and keeps the text of everything else. Nothing executes: scripts, styles, frames and embeds
// are skipped with their contents, and links are held to the same three schemes Links.cs and
// MarkdownConvert allow.

using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace KillerNotes.Services
{
    internal static class HtmlConvert
    {
        private static readonly string[] SafeSchemes = ["http", "https", "mailto"];

        private const string CodeFont = "Consolas";

        // Skipped together with everything inside them.
        private static readonly HashSet<string> SkipTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "script", "style", "head", "title", "iframe", "frame", "frameset", "object", "embed",
            "applet", "noscript", "template", "svg", "math", "select", "textarea", "button",
        };

        // Tags that only separate paragraphs.
        private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "p", "div", "section", "article", "header", "footer", "main", "nav", "aside", "figure",
            "figcaption", "address", "dl", "dt", "dd", "form", "fieldset", "details", "summary",
            "center", "body", "html", "caption", "legend",
        };

        private static readonly Regex TagName = new("^[a-zA-Z][a-zA-Z0-9]*", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

        /// <summary>Reads HTML into a FlowDocument. baseFontSize sets the body size; headings and
        /// code scale from it the same way MarkdownConvert does.</summary>
        public static FlowDocument ToDocument(string html, double baseFontSize)
        {
            var b = new Builder(baseFontSize > 0 ? baseFontSize : 14);
            b.Read(html ?? "");
            if (b.Doc.Blocks.Count == 0) b.Doc.Blocks.Add(new Paragraph());
            return b.Doc;
        }

        private static Brush RuleBrush() => new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80));

        private static string? Attr(string tag, string name)
        {
            var m = Regex.Match(tag, @"\b" + name + @"\s*=\s*(""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string v = m.Groups[2].Success ? m.Groups[2].Value
                     : m.Groups[3].Success ? m.Groups[3].Value
                     : m.Groups[4].Value;
            return WebUtility.HtmlDecode(v);
        }

        private static Uri? SafeUri(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!Uri.TryCreate(url!.Trim(), UriKind.Absolute, out var u)) return null;
            foreach (string s in SafeSchemes)
                if (string.Equals(u.Scheme, s, StringComparison.OrdinalIgnoreCase)) return u;
            return null;
        }

        private sealed class TableCtx
        {
            public required Table Table;
            public required TableRowGroup Group;
            public TableRow? Row;
        }

        private sealed class Builder
        {
            public readonly FlowDocument Doc = new();
            private readonly double _base;

            // Block containers below the document: blockquote sections, list items, table cells.
            // Depth records which list or table a list item or cell belongs to.
            private readonly Stack<(BlockCollection Blocks, string Tag, int Depth)> _containers = new();
            private readonly Stack<List> _lists = new();
            private readonly Stack<TableCtx> _tables = new();

            private Paragraph? _para;
            private Hyperlink? _link;
            private Uri? _href;
            private int _bold, _italic, _under, _strike, _code, _pre, _heading;

            public Builder(double baseSize)
            {
                _base = baseSize;
                Doc.FontSize = baseSize;
            }

            private BlockCollection Container => _containers.Count > 0 ? _containers.Peek().Blocks : Doc.Blocks;

            public void Read(string html)
            {
                for (int i = 0; i < html.Length; )
                {
                    int lt = html.IndexOf('<', i);
                    if (lt < 0) { Text(html[i..]); break; }
                    if (lt > i) Text(html[i..lt]);

                    if (string.CompareOrdinal(html, lt, "<!--", 0, 4) == 0)
                    {
                        int c = html.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                        i = c < 0 ? html.Length : c + 3;
                        continue;
                    }
                    int gt = html.IndexOf('>', lt + 1);
                    if (gt < 0) { Text(html[lt..]); break; }
                    string tag = html.Substring(lt + 1, gt - lt - 1).Trim();
                    i = gt + 1;
                    if (tag.Length == 0 || tag[0] == '!' || tag[0] == '?') continue;

                    bool closing = tag[0] == '/';
                    string name = TagName.Match(closing ? tag[1..].TrimStart() : tag).Value.ToLowerInvariant();
                    if (name.Length == 0) { Text("<" + tag + ">"); continue; }

                    if (!closing && SkipTags.Contains(name))
                    {
                        if (tag.EndsWith("/")) continue;
                        int end = html.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
                        i = end < 0 ? html.Length : Math.Max(i, html.IndexOf('>', end) + 1);
                        if (i == 0) i = html.Length;
                        continue;
                    }

                    if (closing) Close(name); else Open(name, tag);
                }
            }

            private void Open(string name, string tag)
            {
                switch (name)
                {
                    case "b" or "strong": _bold++; break;
                    case "i" or "em" or "cite" or "var": _italic++; break;
                    case "u" or "ins": _under++; break;
                    case "s" or "strike" or "del": _strike++; break;
                    case "code" or "kbd" or "samp" or "tt": _code++; break;

                    case "a":
                        _href = SafeUri(Attr(tag, "href"));
                        _link = null;
                        break;

                    case "br":
                        EnsurePara();
                        Target().Add(new LineBreak());
                        break;

                    case "img":
                    {
                        string? alt = Attr(tag, "alt");
                        AddRun(string.IsNullOrWhiteSpace(alt) ? "[image]" : "[" + alt!.Trim() + "]");
                        break;
                    }

                    case "input":
                        if (string.Equals(Attr(tag, "type"), "checkbox", StringComparison.OrdinalIgnoreCase))
                        {
                            bool on = Regex.IsMatch(tag, @"\bchecked\b", RegexOptions.IgnoreCase);
                            AddRun((on ? Checklist.Checked : Checklist.Empty) + " ");
                        }
                        break;

                    case "hr":
                        ClosePara();
                        Container.Add(new Paragraph
                        {
                            Margin = new Thickness(0, 4, 0, 12),
                            BorderThickness = new Thickness(0, 0, 0, 1),
                            BorderBrush = RuleBrush(),
                        });
                        break;

                    case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                        ClosePara();
                        _heading = name[1] - '0';
                        break;

                    case "pre":
                        ClosePara();
                        _pre++;
                        break;

                    case "blockquote":
                    {
                        ClosePara();
                        var sec = new Section
                        {
                            Margin = new Thickness(0, 0, 0, 8),
                            Padding = new Thickness(10, 0, 0, 0),
                            BorderThickness = new Thickness(3, 0, 0, 0),
                            BorderBrush = RuleBrush(),
                        };
                        Container.Add(sec);
                        _containers.Push((sec.Blocks, "blockquote", 0));
                        break;
                    }

                    case "ul" or "ol":
                    {
                        ClosePara();
                        var list = new List
                        {
                            MarkerStyle = name == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                            Margin = new Thickness(0, 0, 0, 8),
                            Padding = new Thickness(24, 0, 0, 0),
                        };
                        if (name == "ol" && int.TryParse(Attr(tag, "start"), out int start) && start > 0)
                            list.StartIndex = start;
                        Container.Add(list);
                        _lists.Push(list);
                        break;
                    }

                    case "li":
                    {
                        ClosePara();
                        if (_lists.Count == 0) break;   // a stray item reads as a paragraph
                        PopOpen("li", _lists.Count);
                        var li = new ListItem();
                        _lists.Peek().ListItems.Add(li);
                        _containers.Push((li.Blocks, "li", _lists.Count));
                        break;
                    }

                    case "table":
                    {
                        ClosePara();
                        var table = new Table
                        {
                            CellSpacing = 0,
                            Margin = new Thickness(0, 0, 0, 8),
                            BorderBrush = RuleBrush(),
                            BorderThickness = new Thickness(1, 1, 0, 0),
                        };
                        var group = new TableRowGroup();
                        table.RowGroups.Add(group);
                        Container.Add(table);
                        _tables.Push(new TableCtx { Table = table, Group = group });
                        break;
                    }

                    case "tr":
                        if (_tables.Count == 0) { ClosePara(); break; }
                        ClosePara();
                        PopOpen("td", _tables.Count);
                        _tables.Peek().Row = new TableRow();
                        _tables.Peek().Group.Rows.Add(_tables.Peek().Row);
                        break;

                    case "td" or "th":
                    {
                        ClosePara();
                        if (_tables.Count == 0) break;
                        var t = _tables.Peek();
                        PopOpen("td", _tables.Count);
                        if (t.Row == null) { t.Row = new TableRow(); t.Group.Rows.Add(t.Row); }
                        var cell = new TableCell
                        {
                            BorderBrush = RuleBrush(),
                            BorderThickness = new Thickness(0, 0, 1, 1),
                            Padding = new Thickness(8, 3, 8, 3),
                        };
                        if (name == "th") cell.FontWeight = FontWeights.Bold;
                        t.Row.Cells.Add(cell);
                        _containers.Push((cell.Blocks, "td", _tables.Count));
                        break;
                    }

                    default:
                        if (BlockTags.Contains(name)) ClosePara();
                        break;
                }
            }

            private void Close(string name)
            {
                switch (name)
                {
                    case "b" or "strong": if (_bold > 0) _bold--; break;
                    case "i" or "em" or "cite" or "var": if (_italic > 0) _italic--; break;
                    case "u" or "ins": if (_under > 0) _under--; break;
                    case "s" or "strike" or "del": if (_strike > 0) _strike--; break;
                    case "code" or "kbd" or "samp" or "tt": if (_code > 0) _code--; break;
                    case "a": _href = null; _link = null; break;

                    case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                        ClosePara();
                        _heading = 0;
                        break;

                    case "pre":
                        ClosePara();
                        if (_pre > 0) _pre--;
                        break;

                    case "blockquote":
                        ClosePara();
                        PopThrough("blockquote");
                        break;

                    case "li":
                        ClosePara();
                        PopOpen("li", _lists.Count);
                        break;

                    case "ul" or "ol":
                        ClosePara();
                        if (_lists.Count == 0) break;
                        PopOpen("li", _lists.Count);
                        _lists.Pop();
                        break;

                    case "td" or "th":
                        ClosePara();
                        PopOpen("td", _tables.Count);
                        break;

                    case "tr":
                        ClosePara();
                        PopOpen("td", _tables.Count);
                        if (_tables.Count > 0) _tables.Peek().Row = null;
                        break;

                    case "table":
                        ClosePara();
                        if (_tables.Count == 0) break;
                        PopOpen("td", _tables.Count);
                        _tables.Pop();
                        break;

                    default:
                        if (BlockTags.Contains(name)) ClosePara();
                        break;
                }
            }

            /// <summary>Closes an open list item or table cell that belongs to the list or table
            /// at this depth, the way a browser closes an unclosed li or td.</summary>
            private void PopOpen(string tag, int depth)
            {
                if (_containers.Count > 0 && _containers.Peek().Tag == tag && _containers.Peek().Depth == depth)
                {
                    FillEmpty(_containers.Pop().Blocks);
                }
            }

            private void PopThrough(string tag)
            {
                foreach (var (_, currentTag, _) in _containers)
                {
                    if (currentTag != tag) continue;
                    while (_containers.Count > 0)
                    {
                        var (blocks, topTag, _) = _containers.Pop();
                        FillEmpty(blocks);
                        if (topTag == tag) return;
                    }
                }
            }

            // A list item or cell with no blocks is invalid in a FlowDocument.
            private static void FillEmpty(BlockCollection blocks)
            {
                if (blocks.Count == 0) blocks.Add(new Paragraph());
            }

            private void EnsurePara()
            {
                if (_para != null) return;
                _para = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                if (_containers.Count > 0 && _containers.Peek().Tag is "li" or "td")
                    _para.Margin = new Thickness(0, 0, 0, 2);
                if (_heading > 0)
                {
                    _para.FontSize = _base * Headings.Scale[Math.Min(Headings.Scale.Length, _heading) - 1];
                    _para.FontWeight = FontWeights.Bold;
                    _para.Margin = new Thickness(0, _heading == 1 ? 2 : 8, 0, 4);
                }
                if (_pre > 0)
                {
                    _para.FontFamily = new FontFamily(CodeFont);
                    _para.FontSize = _base * 0.95;
                    _para.Padding = new Thickness(8, 6, 8, 6);
                }
                Container.Add(_para);
            }

            private void ClosePara()
            {
                _para = null;
                _link = null;
            }

            private InlineCollection Target()
            {
                EnsurePara();
                if (_href != null)
                {
                    if (_link == null)
                    {
                        _link = new Hyperlink { NavigateUri = _href };
                        _para!.Inlines.Add(_link);
                    }
                    return _link.Inlines;
                }
                return _para!.Inlines;
            }

            private void Text(string raw)
            {
                if (raw.Length == 0) return;
                string t = WebUtility.HtmlDecode(raw);
                if (_pre > 0)
                {
                    // Preformatted text keeps its spacing; the newline right after <pre> is not content.
                    t = t.Replace("\r\n", "\n").Replace('\r', '\n');
                    if (_para == null && t.StartsWith("\n")) t = t[1..];
                    if (t.Length == 0) return;
                    string[] lines = t.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (i > 0) Target().Add(new LineBreak());
                        if (lines[i].Length > 0) AddRun(lines[i]);
                    }
                    return;
                }

                t = Whitespace.Replace(t, " ");
                if (_para == null || _para.Inlines.Count == 0 || _para.Inlines.LastInline is LineBreak)
                {
                    t = t.TrimStart();
                    if (t.Length == 0) return;
                }
                AddRun(t);
            }

            private void AddRun(string text)
            {
                var run = new Run(text);
                if (_bold > 0) run.FontWeight = FontWeights.Bold;
                if (_italic > 0) run.FontStyle = FontStyles.Italic;
                if (_code > 0) run.FontFamily = new FontFamily(CodeFont);
                if (_under > 0 || _strike > 0)
                {
                    var td = new TextDecorationCollection();
                    if (_under > 0) td.Add(TextDecorations.Underline);
                    if (_strike > 0) td.Add(TextDecorations.Strikethrough);
                    run.TextDecorations = td;
                }
                Target().Add(run);
            }
        }
    }
}
