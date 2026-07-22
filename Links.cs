using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;

namespace KillerNotes
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
            if (uri is null) return;
            string s = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString;
            if (!Regex.IsMatch(s, "^(https?|mailto):", RegexOptions.IgnoreCase)) return;
            try { Process.Start(new ProcessStartInfo(s) { UseShellExecute = true }); }
            catch { FlashStatus(Loc("Str_St_LinkFailed")); }
        }

        // ---- Ctrl+K: insert / edit ----

        private void LinkMenu_Click(object sender, RoutedEventArgs e) => LinkShortcut();

        private void LinkShortcut()
        {
            if (_currentId < 0) return;
            var existing = FindLink(Editor.Selection.Start) ?? FindLink(Editor.Selection.End);
            string initial = existing?.NavigateUri?.OriginalString ?? GuessClipboardUrl();

            var dlg = new InputDialog(
                Loc(existing is null ? "Str_Dlg_LinkAdd" : "Str_Dlg_LinkEdit"),
                initial, Loc("Str_Btn_Link")) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            string raw = dlg.Value.Trim();
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
                _ = new Hyperlink(Editor.Selection.Start, Editor.Selection.End) { NavigateUri = url };
            }
            MarkDirty();
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
            try
            {
                if (_currentId < 0 || !Editor.Selection.IsEmpty) return;
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
                Editor.CaretPosition = h.ElementEnd.GetNextInsertionPosition(LogicalDirection.Forward)
                                       ?? h.ElementEnd;
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
                var inlines = HtmlFragmentToInlines(CfHtmlFragment(cf));
                if (!inlines.OfType<Hyperlink>().Any()) return false;   // nothing the native paste would lose

                e.CancelCommand();
                Editor.BeginChange();
                try
                {
                    Editor.Selection.Text = "";
                    var at = Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
                    foreach (var inl in inlines)
                    {
                        switch (inl)
                        {
                            case Hyperlink h:
                                var linkRun = h.Inlines.FirstInline as Run;
                                var nh = new Hyperlink(new Run(linkRun?.Text ?? ""), at) { NavigateUri = h.NavigateUri };
                                at = nh.ElementEnd;
                                break;
                            case LineBreak:
                                at = new LineBreak(at).ElementEnd;
                                break;
                            case Run r:
                                var nr = new Run(r.Text, at)
                                {
                                    FontWeight = r.FontWeight,
                                    FontStyle = r.FontStyle,
                                };
                                if (r.TextDecorations is { Count: > 0 } td) nr.TextDecorations = td;
                                at = nr.ElementEnd;
                                break;
                        }
                    }
                    Editor.CaretPosition = at;
                }
                finally { Editor.EndChange(); }
                MarkDirty();
                return true;
            }
            catch { return false; }   // any parse hiccup: the native paste still runs
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

        /// <summary>Tiny HTML-to-inlines converter for the paste path. Understands
        /// links, bold/italic/underline, and line/paragraph breaks; every other tag is
        /// dropped and its text kept. Deliberately not a general HTML engine - the
        /// preview pane owns real HTML rendering.</summary>
        private static List<Inline> HtmlFragmentToInlines(string html)
        {
            var result = new List<Inline>();
            var text = new StringBuilder();
            var linkText = new StringBuilder();
            int bold = 0, italic = 0, under = 0;
            string? href = null;
            bool lastWasBreak = true;   // swallow leading breaks

            void FlushText()
            {
                if (text.Length == 0) return;
                var run = new Run(text.ToString());
                text.Clear();
                if (bold > 0) run.FontWeight = FontWeights.Bold;
                if (italic > 0) run.FontStyle = FontStyles.Italic;
                if (under > 0) run.TextDecorations = TextDecorations.Underline;
                result.Add(run);
                lastWasBreak = false;
            }
            void Break()
            {
                FlushText();
                if (lastWasBreak) return;   // collapse runs of breaks
                result.Add(new LineBreak());
                lastWasBreak = true;
            }
            void Append(string raw)
            {
                var t = Regex.Replace(System.Net.WebUtility.HtmlDecode(raw), @"\s+", " ");
                if (t.Length == 0) return;
                if (href != null) linkText.Append(t); else text.Append(t);
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
                        i = endTag < 0 ? html.Length : html.IndexOf('>', endTag) + 1;
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
                            if (Uri.TryCreate(href, UriKind.Absolute, out var u) &&
                                (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps || u.Scheme == "mailto"))
                            {
                                result.Add(new Hyperlink(new Run(label)) { NavigateUri = u });
                                lastWasBreak = false;
                            }
                            else text.Append(label);   // non-web target: keep the words, drop the link
                        }
                        href = null;
                        linkText.Clear();
                        break;
                    case "b" or "strong": bold = closing ? Math.Max(0, bold - 1) : bold + 1; break;
                    case "i" or "em": italic = closing ? Math.Max(0, italic - 1) : italic + 1; break;
                    case "u": under = closing ? Math.Max(0, under - 1) : under + 1; break;
                    case "br": Break(); break;
                    case "p" or "div" or "li" or "tr" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "table" or "ul" or "ol":
                        Break();   // block boundary either way
                        break;
                }
            }
            FlushText();
            while (result.Count > 0 && result[^1] is LineBreak) result.RemoveAt(result.Count - 1);
            return result;
        }
    }
}
