using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KillerNotes.Controls;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // Hyperlinks (1.1.3, MrPapaya's CherryTree paste thread, DantexDT's +1): clickable
    // links in notes, preserved on paste, insertable with Ctrl+K, auto-linked as you type.
    //
    //  - Ctrl+Click opens a link (the standard editable-RichTextBox gesture). Only http,
    //    https, and mailto ever reach the shell - a hostile .kndb cannot hand it a
    //    script path.
    //  - Paste: WPF converts RTF natively (Word links already arrive as Hyperlinks) but
    //    has NO HTML clipboard path - which is why CherryTree and browser links pasted
    //    as dead text. The minimal CF_HTML converter below handles <a href>, bold /
    //    italic / underline, and line breaks; anything fancier keeps its text. It only
    //    takes over when the fragment actually contains links, so plain pastes are
    //    untouched.
    //  - Ctrl+K wraps the selection in a link (or edits the link under the caret) via
    //    the kit InputDialog; clearing the address removes the link.
    //  - Typing space / enter right after "https://..." or "www...." links the word.
    //  - Styling is an implicit accent-colored Hyperlink style in Editor.Resources;
    //    NormalizeElement (Editor.cs) clears the baked link-blue on load and paste so
    //    every link follows the live theme.
    public partial class MainWindow
    {
        private void InitLinks()
        {
            Editor.IsDocumentEnabled = true;   // Hyperlinks are interactive while editable
            Editor.AddHandler(Hyperlink.RequestNavigateEvent,
                new System.Windows.Navigation.RequestNavigateEventHandler((_, e) =>
                {
                    OpenLink(e.Uri);
                    e.Handled = true;
                }));
            // Auto-link runs BEFORE the space/enter inserts, while the word is complete.
            Editor.PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.Space or Key.Return &&
                    Keyboard.Modifiers == ModifierKeys.None) AutoLinkWordBeforeCaret();
            };
        }

        private void OpenLink(Uri? uri)
        {
            if (Services.LinkPolicy.AllowedHref(uri) is not string s) return;
            try { Process.Start(new ProcessStartInfo(s) { UseShellExecute = true }); }
            catch { FlashStatus(Loc("Str_St_LinkFailed")); }
        }

        // ---- Ctrl+K: insert / edit ----

        private void LinkMenu_Click(object sender, RoutedEventArgs e) => LinkShortcut();

        private void MarkdownLink_ToolTipOpening(object sender, ToolTipEventArgs e) =>
            ((Button)sender).ToolTip = KillerFamily.Layout.ShortcutHints.WithHint(Loc("Str_Ctx_Link"),"Ctrl+K");

        private void LinkShortcut()
        {
            if (!CanEditCurrentNote()) return;
            var markdownLink = UsesMarkdownTools ? MarkdownLinkAtSelection() : null;
            var existing = UsesMarkdownTools ? null : FindLink(Editor.Selection.Start) ?? FindLink(Editor.Selection.End);
            string initial = markdownLink?.Address ?? existing?.NavigateUri?.OriginalString ?? GuessClipboardUrl();

            var dlg = new InputDialog(
                Loc(existing is null && markdownLink == null ? "Str_Dlg_LinkAdd" : "Str_Dlg_LinkEdit"),
                initial, Loc("Str_Btn_Link")) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            string raw = dlg.Value.Trim();
            if (UsesMarkdownTools)
            {
                if (raw.Length == 0 && markdownLink == null) return;
                Uri? destination = raw.Length == 0 ? null : ParseUrl(raw);
                if (raw.Length > 0 && destination == null) { FlashStatus(Loc("Str_St_LinkBad")); return; }
                string label = markdownLink?.Label ?? (Editor.Selection.IsEmpty ? raw : Editor.Selection.Text.TrimEnd('\r','\n'));
                if (label.IndexOfAny(new[] { '\r','\n' }) >= 0) { FlashStatus(Loc("Str_St_LinkNotText")); return; }
                if (markdownLink == null) label = label.Replace("\\","\\\\").Replace("[","\\[").Replace("]","\\]");
                string address = destination?.AbsoluteUri.Replace("(","%28").Replace(")","%29") ?? "";
                Editor.BeginChange();
                try
                {
                    var range = markdownLink?.Range ?? new TextRange(Editor.Selection.Start,Editor.Selection.End);
                    range.Text = destination == null ? label : "["+label+"]("+address+")";
                    Editor.CaretPosition = range.End;
                }
                finally { Editor.EndChange(); }
                MarkDirty();
                Editor.Focus();
                return;
            }
            if (existing != null)
            {
                if (raw.Length == 0) { Unlink(existing); MarkDirty(); return; }   // cleared = unlink
                if (ParseUrl(raw) is not Uri edited) { FlashStatus(Loc("Str_St_LinkBad")); return; }
                existing.NavigateUri = edited;
                MarkDirty();
                return;
            }

            if (raw.Length == 0) return;
            if (ParseUrl(raw) is not Uri url) { FlashStatus(Loc("Str_St_LinkBad")); return; }
            if (Editor.Selection.IsEmpty)
            {
                var h = new Hyperlink(new Run(raw), Editor.CaretPosition) { NavigateUri = url };
                Editor.CaretPosition = h.ElementEnd;
            }
            else
            {
                // A Hyperlink spans Inlines within ONE Paragraph. An image or a recording chip sits
                // in its own container block, so selecting one gives pointers whose Paragraph is
                // null or differs - and the ctor throws ArgumentException ("'start' and 'end'
                // TextPointers are not in the same Paragraph") rather than failing softly. A
                // selection dragged across a paragraph break does the same.
                var from = Editor.Selection.Start.Paragraph;
                var to = Editor.Selection.End.Paragraph;
                if (from == null || !ReferenceEquals(from, to))
                { FlashStatus(Loc("Str_St_LinkNotText")); return; }
                try { _ = new Hyperlink(Editor.Selection.Start, Editor.Selection.End) { NavigateUri = url }; }
                catch (ArgumentException) { FlashStatus(Loc("Str_St_LinkNotText")); return; }
            }
            MarkDirty();
        }

        private (TextRange Range, string Label, string Address)? MarkdownLinkAtSelection()
        {
            var selection = Editor.Selection;
            var paragraph = selection.Start.Paragraph;
            if (paragraph == null || selection.End.GetInsertionPosition(LogicalDirection.Backward)?.Paragraph != paragraph) return null;
            int start = new TextRange(paragraph.ContentStart,selection.Start).Text.Length;
            int end = new TextRange(paragraph.ContentStart,selection.End.CompareTo(paragraph.ContentEnd) > 0 ? paragraph.ContentEnd : selection.End).Text.Length;
            foreach (Match match in Regex.Matches(TextOf(paragraph),@"(?<!!)\[(?<label>(?:\\.|[^\]\\\r\n])*)\]\((?<url><[^>\r\n]*>|[^()\r\n]*(?:\([^()\r\n]*\)[^()\r\n]*)*)\)"))
            {
                if (start < match.Index || end > match.Index+match.Length) continue;
                return (new TextRange(At(match.Index),At(match.Index+match.Length)),match.Groups["label"].Value,
                    match.Groups["url"].Value.Trim('<','>'));
            }
            return null;

            TextPointer At(int offset)
            {
                var position = paragraph.ContentStart;
                while (position.CompareTo(paragraph.ContentEnd) < 0)
                {
                    if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                    {
                        int length = position.GetTextInRun(LogicalDirection.Forward).Length;
                        if (offset <= length) return position.GetPositionAtOffset(offset,LogicalDirection.Forward);
                        offset -= length;
                    }
                    else if (position.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart
                        && position.GetAdjacentElement(LogicalDirection.Forward) is LineBreak) offset -= 2;
                    position = position.GetNextContextPosition(LogicalDirection.Forward) ?? paragraph.ContentEnd;
                }
                return paragraph.ContentEnd;
            }
        }

        /// <summary>Bare "thekiller.net" style input gets https:// prepended; anything
        /// that then is not an absolute http(s)/mailto URI is rejected.</summary>
        private static Uri? ParseUrl(string raw)
        {
            if (raw.Length == 0) return null;
            if (!Regex.IsMatch(raw, "^[a-zA-Z][a-zA-Z0-9+.-]*:")) raw = "https://" + raw;
            return Uri.TryCreate(raw, UriKind.Absolute, out var u) &&
                   (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps || u.Scheme == "mailto")
                ? u : null;
        }

        private static string GuessClipboardUrl()
        {
            try
            {
                var t = Clipboard.GetText()?.Trim() ?? "";
                if (Regex.IsMatch(t, @"^(https?://|www\.)\S+$", RegexOptions.IgnoreCase)) return t;
            }
            catch { /* clipboard busy: no prefill */ }
            return "";
        }

        private static Hyperlink? FindLink(TextPointer? pos)
        {
            for (var d = pos?.Parent as DependencyObject; d != null;
                 d = d is TextElement t ? t.Parent : null)
                if (d is Hyperlink h) return h;
            return null;
        }

        /// <summary>Removes the link wrapper but keeps its text (and formatting).</summary>
        private static void Unlink(Hyperlink h)
        {
            var siblings = h.SiblingInlines;
            if (siblings is null) return;
            foreach (var i in h.Inlines.ToList())
            {
                h.Inlines.Remove(i);
                siblings.InsertBefore(h, i);
            }
            siblings.Remove(h);
        }

        // ---- Auto-link while typing ----

        private void AutoLinkWordBeforeCaret()
        {
            if (UsesMarkdownTools) return;
            try
            {
                if (_currentId < 0 || !Editor.Selection.IsEmpty || !CanEditCurrentNote(explain: false)) return;
                var caret = Editor.CaretPosition;
                if (FindLink(caret) != null) return;   // already inside a link
                string back = caret.GetTextInRun(LogicalDirection.Backward);
                var m = Regex.Match(back, @"(https?://\S+|www\.\S+)$", RegexOptions.IgnoreCase);
                if (!m.Success) return;
                // Trailing sentence punctuation belongs to the sentence, not the URL.
                string url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')');
                if (url.Length < 5) return;
                var start = caret.GetPositionAtOffset(-m.Length);
                var end = caret.GetPositionAtOffset(-(m.Length - url.Length));
                if (start is null || end is null) return;
                if (ParseUrl(url) is not Uri u) return;
                var h = new Hyperlink(start, end) { NavigateUri = u };
                Editor.CaretPosition = h.ElementEnd;
                MarkDirty();
            }
            catch { /* linkifying must never break typing */ }
        }

        // ---- HTML clipboard paste (CherryTree, browsers) ----

        /// <summary>Handles a paste whose only rich format is HTML - WPF has no native
        /// HTML paste path, so links from CherryTree and browsers died to plain text.
        /// Returns false (native paste proceeds) unless the fragment contains links.</summary>
        private bool TryPasteHtml(System.Windows.DataObjectPastingEventArgs e)
        {
            try
            {
                if (e.DataObject.GetData(DataFormats.Html) is not string cf ||
                    cf.Length == 0 || cf.Length > 2_000_000) return false;
                CapturePasteRange();
                if (!PasteHtmlInto(Editor, cf)) { _pasteDoc = null; return false; }   // no links: native paste
                e.CancelCommand();
                MarkDirty();
                // The same theme tidy-up the native paste gets: pasted neutral colors follow
                // the theme and paragraphs take the editor's own margins.
                _pasteKeepsTableStyle = false;
                Dispatcher.BeginInvoke(new Action(NormalizePastedContent),
                    System.Windows.Threading.DispatcherPriority.Background);
                return true;
            }
            catch { _pasteDoc = null; return false; }   // any parse hiccup: the native paste still runs
        }

        /// <summary>Converts a CF_HTML payload and puts it in place of the box's selection,
        /// keeping headings, lists, code blocks, links and inline emphasis. Returns false and
        /// leaves the box alone when the fragment has no links, so the native paste runs.</summary>
        internal static bool PasteHtmlInto(RichTextBox box, string cf)
        {
            var blocks = HtmlFragmentToBlocks(CfHtmlFragment(cf), box.FontSize);
            if (!InlinesOf(blocks).OfType<Hyperlink>().Any()) return false;   // nothing the native paste would lose

            // Through XAML, the same way WPF's own paste lands a fragment: the first and last
            // paragraphs merge with the text around the caret and blocks in between stay blocks.
            var holder = new FlowDocument();
            holder.Blocks.AddRange(blocks);
            using var ms = new System.IO.MemoryStream();
            new TextRange(holder.ContentStart, holder.ContentEnd).Save(ms, DataFormats.Xaml);
            ms.Position = 0;
            box.BeginChange();
            try
            {
                box.Selection.Load(ms, DataFormats.Xaml);
                box.CaretPosition = box.Selection.End;
            }
            finally { box.EndChange(); }
            return true;
        }

        /// <summary>Every inline in the blocks, list items included, in document order.</summary>
        internal static IEnumerable<Inline> InlinesOf(IEnumerable<Block> blocks)
        {
            foreach (var b in blocks)
            {
                var inner = b switch
                {
                    Paragraph p => p.Inlines.SelectMany(InlineTree),
                    List l => InlinesOf(l.ListItems.SelectMany(li => li.Blocks)),
                    Section s => InlinesOf(s.Blocks),
                    _ => Enumerable.Empty<Inline>(),
                };
                foreach (var i in inner) yield return i;
            }
        }

        private static IEnumerable<Inline> InlineTree(Inline i)
        {
            yield return i;
            if (i is Span s && i is not Hyperlink)
                foreach (var c in s.Inlines.SelectMany(InlineTree)) yield return c;
        }

        /// <summary>Pulls the fragment out of a CF_HTML payload. The header's byte
        /// offsets are unreliable after decoding, so the marker comments are used.</summary>
        private static string CfHtmlFragment(string cf)
        {
            const string startMark = "<!--StartFragment-->", endMark = "<!--EndFragment-->";
            int s = cf.IndexOf(startMark, StringComparison.OrdinalIgnoreCase);
            int e = cf.IndexOf(endMark, StringComparison.OrdinalIgnoreCase);
            if (s >= 0 && e > s) return cf[(s + startMark.Length)..e];
            int body = cf.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
            return body >= 0 ? cf[body..] : cf;
        }

        /// <summary>The paste converter's inlines in document order (its tests read these).</summary>
        internal static List<Inline> HtmlFragmentToInlines(string html) =>
            InlinesOf(HtmlFragmentToBlocks(html, Headings.DefaultBase)).ToList();

        private static readonly FontFamily CodeFont = new("Consolas");

        /// <summary>Small HTML-to-blocks converter for the paste path. Keeps headings (in
        /// the Alt+1 to Alt+3 shape, h4 to h6 as level 3), ul/ol lists with nesting, pre
        /// blocks line for line in Consolas, inline code, links, bold/italic/underline and
        /// line breaks, with a blank paragraph between top-level blocks the way a note is
        /// typed. Every other tag is dropped and its text kept. Deliberately not a general
        /// HTML engine. The preview pane owns real HTML rendering.</summary>
        internal static List<Block> HtmlFragmentToBlocks(string html, double baseSize)
        {
            var top = new List<Block>();
            var lists = new Stack<(List List, ListItem? Item)>();
            var text = new StringBuilder();
            var linkText = new StringBuilder();
            int bold = 0, italic = 0, under = 0, mono = 0, pre = 0, heading = 0;
            string? href = null;
            Paragraph? cur = null;
            bool lineStart = true;   // collapsed whitespace is dropped at the start of a line

            // Adds a block where the parser is: the open list item, or the top level with a
            // blank paragraph before it when something came first.
            void AddBlock(Block b)
            {
                if (lists.Count > 0)
                {
                    var (list, item) = lists.Pop();
                    if (item == null) { item = new ListItem(); list.ListItems.Add(item); }
                    lists.Push((list, item));
                    item.Blocks.Add(b);
                    return;
                }
                if (top.Count > 0) top.Add(new Paragraph());
                top.Add(b);
            }
            Paragraph Para()
            {
                if (cur != null) return cur;
                cur = new Paragraph();
                if (heading > 0)
                {
                    cur.FontSize = Headings.SizeFor(heading, baseSize);
                    cur.FontWeight = FontWeights.Bold;
                }
                if (pre > 0) cur.FontFamily = CodeFont;
                AddBlock(cur);
                lineStart = true;
                return cur;
            }
            Run Styled(string s)
            {
                var run = new Run(s);
                if (bold > 0 || heading > 0) run.FontWeight = FontWeights.Bold;
                if (heading > 0) run.FontSize = Headings.SizeFor(heading, baseSize);
                if (italic > 0) run.FontStyle = FontStyles.Italic;
                if (under > 0) run.TextDecorations = TextDecorations.Underline;
                if (mono > 0 || pre > 0) run.FontFamily = CodeFont;
                return run;
            }
            void FlushText()
            {
                if (text.Length == 0) return;
                string s = text.ToString();
                text.Clear();
                if (pre > 0)
                {
                    // Code keeps every line and its indentation.
                    var lines = s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                    for (int n = 0; n < lines.Length; n++)
                    {
                        // HTML drops the newline right after <pre>; the trailing one goes in EndBlock.
                        if (n > 0 && Para().Inlines.Count > 0) Para().Inlines.Add(new LineBreak());
                        if (lines[n].Length > 0) Para().Inlines.Add(Styled(lines[n]));
                    }
                    lineStart = false;
                    return;
                }
                Para().Inlines.Add(Styled(s));
                lineStart = false;
            }
            void EndBlock()
            {
                FlushText();
                if (cur != null)
                {
                    // A trailing line break or space at the end of a block is noise.
                    while (cur.Inlines.LastInline is LineBreak lb) cur.Inlines.Remove(lb);
                    if (pre == 0 && cur.Inlines.LastInline is Run last && last.Text.EndsWith(" ")) last.Text = last.Text.TrimEnd();
                }
                cur = null;
                lineStart = true;
            }
            void Append(string raw)
            {
                var decoded = System.Net.WebUtility.HtmlDecode(raw);
                if (pre > 0 && href == null) { text.Append(decoded); return; }
                var t = Regex.Replace(decoded, @"\s+", " ");
                if (href != null) { linkText.Append(t); return; }
                if (lineStart && text.Length == 0) t = t.TrimStart();
                if (t.Length == 0) return;
                if (cur == null && t.Trim().Length == 0) return;   // whitespace between blocks
                text.Append(t);
                lineStart = false;
            }

            for (int i = 0; i < html.Length; )
            {
                int lt = html.IndexOf('<', i);
                if (lt < 0) { Append(html[i..]); break; }
                if (lt > i) Append(html[i..lt]);
                if (html.Length - lt > 3 && html[lt + 1] == '!' && html[lt + 2] == '-' && html[lt + 3] == '-')
                {
                    int c = html.IndexOf("-->", lt, StringComparison.Ordinal);
                    i = c < 0 ? html.Length : c + 3;
                    continue;
                }
                int gt = html.IndexOf('>', lt);
                if (gt < 0) break;
                string tag = html[(lt + 1)..gt].Trim();
                i = gt + 1;
                bool closing = tag.StartsWith("/");
                string name = Regex.Match(tag.TrimStart('/'), "^[a-zA-Z0-9]+").Value.ToLowerInvariant();
                switch (name)
                {
                    case "script" or "style" when !closing:
                        int endTag = html.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
                        // A close tag cut off before its '>' ends the fragment; IndexOf's -1 + 1
                        // would restart the scan at 0 and never finish.
                        int endGt = endTag < 0 ? -1 : html.IndexOf('>', endTag);
                        i = endGt < 0 ? html.Length : endGt + 1;
                        break;
                    case "a" when !closing:
                        FlushText();
                        var m = Regex.Match(tag, "href\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.IgnoreCase);
                        href = m.Success
                            ? System.Net.WebUtility.HtmlDecode(
                                m.Groups[1].Value.Length > 0 ? m.Groups[1].Value
                                : m.Groups[2].Value.Length > 0 ? m.Groups[2].Value : m.Groups[3].Value)
                            : null;
                        linkText.Clear();
                        break;
                    case "a":   // closing
                        if (href != null)
                        {
                            string label = Regex.Replace(linkText.ToString(), @"\s+", " ").Trim();
                            if (label.Length == 0) label = href;
                            if (Uri.TryCreate(href, UriKind.Absolute, out var u) && Services.LinkPolicy.IsAllowed(u))
                            {
                                Para().Inlines.Add(new Hyperlink(Styled(label)) { NavigateUri = u });
                                lineStart = false;
                            }
                            else { text.Append(label); lineStart = false; }   // non-web target: keep the words, drop the link
                        }
                        href = null;
                        linkText.Clear();
                        break;
                    case "b" or "strong":
                        FlushText();
                        bold = closing ? Math.Max(0, bold - 1) : bold + 1;
                        break;
                    case "i" or "em":
                        FlushText();
                        italic = closing ? Math.Max(0, italic - 1) : italic + 1;
                        break;
                    case "u":
                        FlushText();
                        under = closing ? Math.Max(0, under - 1) : under + 1;
                        break;
                    case "code" or "kbd" or "samp" or "tt":
                        FlushText();
                        mono = closing ? Math.Max(0, mono - 1) : mono + 1;
                        break;
                    case "br":
                        FlushText();
                        Para().Inlines.Add(new LineBreak());
                        lineStart = true;
                        break;
                    case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                        EndBlock();
                        heading = closing ? 0 : Math.Min(Headings.MaxLevel, name[1] - '0');
                        break;
                    case "pre":
                        EndBlock();
                        pre = closing ? Math.Max(0, pre - 1) : pre + 1;
                        break;
                    case "ul" or "ol" when !closing:
                        EndBlock();
                        var list = new List { MarkerStyle = name == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc };
                        AddBlock(list);
                        lists.Push((list, null));
                        break;
                    case "ul" or "ol":   // closing
                        EndBlock();
                        if (lists.Count > 0) lists.Pop();
                        break;
                    case "li":
                        EndBlock();
                        if (!closing)
                        {
                            if (lists.Count == 0)
                            {
                                // A stray li with no list around it still reads as a bullet.
                                var stray = new List { MarkerStyle = TextMarkerStyle.Disc };
                                AddBlock(stray);
                                lists.Push((stray, null));
                            }
                            var (owner, _) = lists.Pop();
                            var item = new ListItem();
                            owner.ListItems.Add(item);
                            lists.Push((owner, item));
                        }
                        break;
                    case "p" or "div" or "tr" or "table" or "blockquote" or "hr":
                        EndBlock();   // block boundary either way
                        break;
                }
            }
            EndBlock();
            return top;
        }
    }
}
