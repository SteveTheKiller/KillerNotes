// ═══════════════════════════════════════════════════════════
//  MARKDOWN CONVERSION  -  FlowDocument to markdown and back
// ═══════════════════════════════════════════════════════════
//
// Pure conversion between the two note content types (1.3.0). No UI, no store access, no
// MainWindow, so the test project can exercise it directly and the vault export can call it
// without an editor on screen.
//
// Direction matters. Markdown to rich is total: everything markdown can express has a
// FlowDocument equivalent. Rich to markdown is LOSSY, because a FlowDocument carries things
// markdown has no syntax for. Losses() names them up front so a caller can put the list in
// front of the user before anything is rewritten, rather than discovering it afterward.
//
// Headings are deliberately NOT reverse-engineered from font size on the way out. A large bold
// paragraph might be a heading or might be a large bold paragraph, and guessing wrong rewrites
// the user's document. Markdown to rich sets a heading's size from a base the caller supplies,
// so headings survive a round trip that stays inside one note; a rich note authored by hand
// keeps its paragraphs as paragraphs.
//
// Hyperlinks are held to the same three schemes Links.cs allows. A markdown file is untrusted
// input once it can arrive by import or from a vault folder, so a javascript: or file: URL must
// never become a live Hyperlink just because it was spelled with link syntax.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
// Block and Inline are declared in BOTH Markdig.Syntax and System.Windows.Documents, and this
// file needs both namespaces. Bare "Block" or "Inline" is an ambiguous reference, so all four
// names are aliased and neither bare name is used anywhere below.
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;

namespace KillerNotes.Services
{
    internal static class MarkdownConvert
    {
        // Same pipeline the preview pane uses (Preview.cs): advanced extensions for tables and
        // strikethrough, DisableHtml so raw HTML in a markdown note is inert text rather than
        // markup we would then have to defuse a second time.
        private static readonly MarkdownPipeline Pipeline =
            new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

        // The only schemes allowed to become a clickable Hyperlink, matching Links.cs.
        private static readonly string[] SafeSchemes = ["http", "https", "mailto"];

        // Heading sizes as multiples of the editor's base font size. Markdown has six levels;
        // past h3 the differences are small on purpose so a deep outline does not turn into
        // body text that happens to be bold.
        private static readonly double[] HeadingScale = [1.85, 1.55, 1.32, 1.16, 1.06, 1.0];

        private const string CodeFont = "Consolas";

        // ---------------------------------------------------------------
        //  Markdown  ->  FlowDocument
        // ---------------------------------------------------------------

        /// <summary>Renders markdown into a FlowDocument. baseFontSize is the editor's current
        /// size; headings and code scale from it so a converted note matches the note around it
        /// instead of carrying a hardcoded point size.</summary>
        public static FlowDocument ToDocument(string markdown, double baseFontSize)
        {
            var doc = new FlowDocument();
            if (baseFontSize > 0) doc.FontSize = baseFontSize;
            double base_ = baseFontSize > 0 ? baseFontSize : 14;

            MarkdownDocument parsed;
            try { parsed = Markdown.Parse(markdown ?? "", Pipeline); }
            catch
            {
                // Unparseable markdown must still open. Fall back to the text itself rather
                // than handing back an empty document and losing the note's content.
                doc.Blocks.Add(TextParagraph(markdown ?? ""));
                return doc;
            }

            foreach (var block in parsed)
                foreach (var b in ConvertBlock(block, base_))
                    doc.Blocks.Add(b);

            if (doc.Blocks.Count == 0) doc.Blocks.Add(new Paragraph());
            return doc;
        }

        /// <summary>The text as one paragraph, its line breaks preserved as LineBreak inlines.
        /// A Run holding newline characters does not break lines in a FlowDocument - the text
        /// would come back as one long line - so the breaks have to be real elements.
        /// Only reached when Markdig cannot parse the input at all, where returning the content
        /// unformatted still beats returning an empty document.</summary>
        private static Paragraph TextParagraph(string text)
        {
            var p = new Paragraph();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) p.Inlines.Add(new LineBreak());
                p.Inlines.Add(new Run(lines[i]));
            }
            return p;
        }

        private static IEnumerable<WpfBlock> ConvertBlock(MdBlock block, double base_)
        {
            switch (block)
            {
                case HeadingBlock h:
                {
                    int level = Math.Max(1, Math.Min(6, h.Level));
                    var p = new Paragraph
                    {
                        FontSize = base_ * HeadingScale[level - 1],
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, level == 1 ? 2 : 8, 0, 4),
                    };
                    AppendInlines(p.Inlines, h.Inline);
                    yield return p;
                    break;
                }

                case ParagraphBlock para:
                {
                    var p = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                    AppendInlines(p.Inlines, para.Inline);
                    yield return p;
                    break;
                }

                case CodeBlock code:
                {
                    // Fenced and indented code alike. Lines are kept verbatim, joined by
                    // LineBreak rather than separate paragraphs so the block stays one unit.
                    var p = new Paragraph
                    {
                        FontFamily = new FontFamily(CodeFont),
                        FontSize = base_ * 0.95,
                        Margin = new Thickness(0, 0, 0, 8),
                        Padding = new Thickness(8, 6, 8, 6),
                    };
                    var lines = code.Lines.Lines;
                    // Lines.Count is the authoritative length; the backing array is oversized.
                    for (int i = 0; i < code.Lines.Count; i++)
                    {
                        if (i > 0) p.Inlines.Add(new LineBreak());
                        p.Inlines.Add(new Run(lines[i].Slice.ToString()));
                    }
                    yield return p;
                    break;
                }

                case QuoteBlock quote:
                {
                    // A Section carries the left rule; its children stay real blocks so a quote
                    // can hold lists and code, which a single indented Paragraph could not.
                    var sec = new Section
                    {
                        Margin = new Thickness(0, 0, 0, 8),
                        Padding = new Thickness(10, 0, 0, 0),
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)),
                    };
                    foreach (var child in quote)
                        foreach (var b in ConvertBlock(child, base_))
                            sec.Blocks.Add(b);
                    if (sec.Blocks.Count == 0) sec.Blocks.Add(new Paragraph());
                    yield return sec;
                    break;
                }

                case ListBlock list:
                {
                    var l = new List
                    {
                        MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Margin = new Thickness(0, 0, 0, 8),
                        Padding = new Thickness(24, 0, 0, 0),
                    };
                    if (list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.Integer,
                                                       CultureInfo.InvariantCulture, out int start) && start > 0)
                        l.StartIndex = start;
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        var li = new ListItem();
                        foreach (var child in item)
                            foreach (var b in ConvertBlock(child, base_))
                                li.Blocks.Add(b);
                        if (li.Blocks.Count == 0) li.Blocks.Add(new Paragraph());
                        l.ListItems.Add(li);
                    }
                    if (l.ListItems.Count > 0) yield return l;
                    break;
                }

                case ThematicBreakBlock:
                {
                    yield return new Paragraph
                    {
                        Margin = new Thickness(0, 4, 0, 12),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)),
                    };
                    break;
                }

                case ContainerBlock container:
                {
                    // Anything else that holds blocks (a table from the advanced extensions, a
                    // custom container) is flattened to its children rather than dropped, so no
                    // text disappears even when the structure has no FlowDocument equivalent.
                    foreach (var child in container)
                        foreach (var b in ConvertBlock(child, base_))
                            yield return b;
                    break;
                }

                case LeafBlock leaf:
                {
                    var p = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
                    AppendInlines(p.Inlines, leaf.Inline);
                    if (p.Inlines.Count > 0) yield return p;
                    break;
                }
            }
        }

        private static void AppendInlines(InlineCollection target, ContainerInline? inlines)
        {
            if (inlines == null) return;
            foreach (var inline in inlines)
                foreach (var run in ConvertInline(inline))
                    target.Add(run);
        }

        private static IEnumerable<WpfInline> ConvertInline(MdInline inline)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    yield return new Run(lit.Content.ToString());
                    break;

                case CodeInline code:
                    yield return new Run(code.Content) { FontFamily = new FontFamily(CodeFont) };
                    break;

                case EmphasisInline em:
                {
                    var span = new Span();
                    foreach (var child in em)
                        foreach (var r in ConvertInline(child))
                            span.Inlines.Add(r);
                    // Advanced extensions reuse the emphasis node for strikethrough (~~),
                    // subscript and superscript; only the three with a FlowDocument equivalent
                    // are styled, and the rest pass their text through unchanged.
                    if (em.DelimiterChar == '~' && em.DelimiterCount == 2)
                        span.TextDecorations = TextDecorations.Strikethrough;
                    else if (em.DelimiterCount >= 3) { span.FontWeight = FontWeights.Bold; span.FontStyle = FontStyles.Italic; }
                    else if (em.DelimiterCount == 2) span.FontWeight = FontWeights.Bold;
                    else if (em.DelimiterCount == 1) span.FontStyle = FontStyles.Italic;
                    yield return span;
                    break;
                }

                case LinkInline link:
                {
                    var inner = new List<WpfInline>();
                    foreach (var child in link)
                        inner.AddRange(ConvertInline(child));

                    if (link.IsImage)
                    {
                        // An image reference is left as its alt text plus the URL. Fetching a
                        // remote image would put a note load on the network, and a local path
                        // may not exist on the machine that opens the note.
                        string alt = string.Concat(inner.OfType<Run>().Select(r => r.Text));
                        yield return new Run(alt.Length > 0 ? $"[{alt}] ({link.Url})" : $"[image] ({link.Url})");
                        break;
                    }

                    if (inner.Count == 0) inner.Add(new Run(link.Url ?? ""));

                    if (IsSafeUrl(link.Url) && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri))
                    {
                        var h = new Hyperlink { NavigateUri = uri };
                        foreach (var r in inner) h.Inlines.Add(r);
                        yield return h;
                    }
                    else
                    {
                        // Not a scheme we let through: keep the words, drop the link.
                        foreach (var r in inner) yield return r;
                    }
                    break;
                }

                case LineBreakInline br:
                    if (br.IsHard) yield return new LineBreak();
                    else yield return new Run(" ");
                    break;

                case ContainerInline container:
                {
                    foreach (var child in container)
                        foreach (var r in ConvertInline(child))
                            yield return r;
                    break;
                }

                default:
                {
                    string s = inline.ToString() ?? "";
                    if (s.Length > 0) yield return new Run(s);
                    break;
                }
            }
        }

        private static bool IsSafeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
            foreach (string s in SafeSchemes)
                if (string.Equals(u.Scheme, s, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ---------------------------------------------------------------
        //  FlowDocument  ->  Markdown
        // ---------------------------------------------------------------

        /// <summary>Serializes a FlowDocument to markdown. Lossy: call Losses first and show
        /// the user what will not survive.</summary>
        public static string FromDocument(FlowDocument doc)
        {
            var sb = new StringBuilder();
            foreach (var block in doc.Blocks) WriteBlock(sb, block, 0);
            // Collapse the run of blank lines block writing leaves behind, and end with exactly
            // one newline so the file is well formed for git and for other markdown editors.
            string text = sb.ToString().Replace("\r\n", "\n");
            while (text.Contains("\n\n\n")) text = text.Replace("\n\n\n", "\n\n");
            return text.Trim('\n') + "\n";
        }

        private static void WriteBlock(StringBuilder sb, WpfBlock block, int depth)
        {
            string indent = new(' ', depth * 2);
            switch (block)
            {
                case Paragraph p:
                {
                    // A paragraph whose only content is a bottom border is the horizontal rule
                    // ToDocument emits; round-trip it back to markdown rather than as a blank.
                    if (p.Inlines.Count == 0 && p.BorderThickness.Bottom > 0)
                    {
                        sb.Append(indent).Append("---\n\n");
                        break;
                    }
                    string body = InlinesToMarkdown(p.Inlines);
                    if (body.Trim().Length == 0) { sb.Append('\n'); break; }
                    sb.Append(indent).Append(body).Append("\n\n");
                    break;
                }

                case List list:
                {
                    int n = list.StartIndex;
                    bool ordered = list.MarkerStyle is TextMarkerStyle.Decimal
                        or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin
                        or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman;
                    foreach (var item in list.ListItems)
                    {
                        string marker = ordered ? n++.ToString(CultureInfo.InvariantCulture) + ". " : "- ";
                        var inner = new StringBuilder();
                        foreach (var b in item.Blocks) WriteBlock(inner, b, depth + 1);

                        // The first line carries the marker; continuation lines line up under it
                        // so a nested list stays nested when the markdown is re-parsed.
                        var lines = inner.ToString().TrimEnd('\n').Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (i == 0) sb.Append(indent).Append(marker).Append(lines[i].TrimStart()).Append('\n');
                            else if (lines[i].Trim().Length == 0) sb.Append('\n');
                            else sb.Append(indent).Append("  ").Append(lines[i]).Append('\n');
                        }
                    }
                    sb.Append('\n');
                    break;
                }

                case Section sec:
                {
                    // The quote shape ToDocument produces: a left border and nothing else.
                    bool quote = sec.BorderThickness.Left > 0;
                    var inner = new StringBuilder();
                    foreach (var b in sec.Blocks) WriteBlock(inner, b, depth);
                    if (!quote) { sb.Append(inner); break; }
                    foreach (string line in inner.ToString().TrimEnd('\n').Split('\n'))
                        sb.Append(indent).Append(line.Length == 0 ? ">" : "> " + line).Append('\n');
                    sb.Append('\n');
                    break;
                }

                case Table table:
                {
                    // No markdown equivalent that survives arbitrary cell content, so the cells
                    // are emitted as plain paragraphs. Losses() warns about this before it runs.
                    foreach (var group in table.RowGroups)
                        foreach (var row in group.Rows)
                        {
                            var cells = row.Cells.Select(c =>
                            {
                                var cb = new StringBuilder();
                                foreach (var b in c.Blocks) WriteBlock(cb, b, 0);
                                return cb.ToString().Replace('\n', ' ').Trim();
                            });
                            string line = string.Join(" | ", cells).Trim();
                            if (line.Length > 0) sb.Append(indent).Append(line).Append("\n\n");
                        }
                    break;
                }

                case BlockUIContainer:
                    // An embedded control or image with no text to preserve.
                    break;
            }
        }

        private static string InlinesToMarkdown(InlineCollection inlines)
        {
            var sb = new StringBuilder();
            foreach (var inline in inlines) WriteInline(sb, inline, false, false);
            return sb.ToString().TrimEnd();
        }

        private static void WriteInline(StringBuilder sb, WpfInline inline, bool inBold, bool inItalic)
        {
            switch (inline)
            {
                case Run run:
                {
                    if (run.Text.Length == 0) break;
                    bool bold = !inBold && run.FontWeight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight();
                    bool italic = !inItalic && run.FontStyle != FontStyles.Normal;
                    bool strike = run.TextDecorations?.Count > 0
                                  && run.TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough);

                    string text = EscapeMarkdown(run.Text);
                    // Emphasis markers must hug the text: "** bold **" is literal asterisks in
                    // every markdown parser, not bold.
                    string lead = "", trail = "";
                    int ws = text.Length - text.TrimStart().Length;
                    int we = text.Length - text.TrimEnd().Length;
                    if (ws > 0 || we > 0)
                    {
                        lead = text[..ws];
                        trail = we > 0 ? text[^we..] : "";
                        text = text[ws..(text.Length - we)];
                    }
                    if (text.Length == 0) { sb.Append(lead).Append(trail); break; }

                    var wrap = new StringBuilder();
                    if (bold) wrap.Append("**");
                    if (italic) wrap.Append('*');
                    if (strike) wrap.Append("~~");
                    string open = wrap.ToString();
                    // Enumerable.Reverse spelled out: on a string, a bare .Reverse() can bind to
                    // MemoryExtensions.Reverse(Span<T>), which returns void.
                    string close = new([.. Enumerable.Reverse(open)]);
                    // Reversing turns "**" into "**" and "~~" into "~~" correctly, but a mixed
                    // run reverses to the right nesting order too, which is what markdown wants.
                    sb.Append(lead).Append(open).Append(text).Append(close).Append(trail);
                    break;
                }

                case Hyperlink link:
                {
                    var inner = new StringBuilder();
                    foreach (var child in link.Inlines) WriteInline(inner, child, inBold, inItalic);
                    string label = inner.ToString().Trim();
                    string url = link.NavigateUri?.ToString() ?? "";
                    if (url.Length == 0) { sb.Append(label); break; }
                    sb.Append('[').Append(label.Length > 0 ? label : url).Append("](").Append(url).Append(')');
                    break;
                }

                case LineBreak:
                    // Two trailing spaces is markdown's hard break.
                    sb.Append("  \n");
                    break;

                case Span span:
                {
                    bool bold = span.FontWeight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight();
                    bool italic = span.FontStyle != FontStyles.Normal;
                    var inner = new StringBuilder();
                    foreach (var child in span.Inlines)
                        WriteInline(inner, child, inBold || bold, inItalic || italic);
                    string text = inner.ToString();
                    if (text.Trim().Length == 0) { sb.Append(text); break; }
                    if (bold && !inBold) text = "**" + text.Trim() + "**";
                    if (italic && !inItalic) text = "*" + text.Trim() + "*";
                    sb.Append(text);
                    break;
                }

                case InlineUIContainer:
                    // Image, sketch or recording chip: nothing textual to carry across.
                    break;
            }
        }

        // Only the characters that would change the meaning of a line are escaped. Escaping
        // every special character makes ordinary prose unreadable in the file, which defeats
        // the point of storing markdown at all.
        private static string EscapeMarkdown(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c is '\\' or '`' or '*' or '_' or '[' or ']') sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------
        //  Loss reporting
        // ---------------------------------------------------------------

        /// <summary>What converting this document to markdown would discard. Empty means the
        /// conversion is clean. Returned as resource KEYS so the caller localizes them.</summary>
        public static List<string> Losses(FlowDocument doc)
        {
            var keys = new List<string>();
            bool tables = false, images = false, colors = false;
            Scan(doc.Blocks);

            void Scan(BlockCollection blocks)
            {
                foreach (var b in blocks)
                {
                    switch (b)
                    {
                        case Table t:
                            tables = true;
                            foreach (var g in t.RowGroups)
                                foreach (var r in g.Rows)
                                    foreach (var c in r.Cells) Scan(c.Blocks);
                            break;
                        case BlockUIContainer:
                            images = true;
                            break;
                        case Section s: Scan(s.Blocks); break;
                        case List l:
                            foreach (var li in l.ListItems) Scan(li.Blocks);
                            break;
                        case Paragraph p: ScanInlines(p.Inlines); break;
                    }
                }
            }

            void ScanInlines(InlineCollection inlines)
            {
                foreach (var i in inlines)
                {
                    switch (i)
                    {
                        case InlineUIContainer: images = true; break;
                        case Run r:
                            if (HasOwnColor(r)) colors = true;
                            break;
                        case Span sp: ScanInlines(sp.Inlines); break;
                    }
                }
            }

            // A DELIBERATE color, not an inherited one. Foreground is an inheriting dependency
            // property, so reading it off a plain Run returns whatever the document supplies, which
            // is a SolidColorBrush every time. Testing the value therefore reported "colors will be
            // lost" for any note at all, and the convert-to-markdown dialog listed a loss that was
            // not there. ReadLocalValue only answers for a value set on the run itself.
            static bool HasOwnColor(Run r) =>
                r.ReadLocalValue(TextElement.ForegroundProperty) != DependencyProperty.UnsetValue ||
                r.ReadLocalValue(TextElement.BackgroundProperty) != DependencyProperty.UnsetValue;

            if (tables) keys.Add("Str_MdLoss_Tables");
            if (images) keys.Add("Str_MdLoss_Images");
            if (colors) keys.Add("Str_MdLoss_Colors");
            return keys;
        }
    }
}
