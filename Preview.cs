using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Markdig;

namespace KillerNotes
{
    // Optional markdown/HTML preview. When the note's plain text looks like markdown or
    // HTML, a preview toggle appears in the format bar; opening it splits the editor and
    // renders through the built-in WPF WebBrowser (IE engine). Markdown converts via
    // Markdig; HTML notes are defused first (no scripts, handlers, frames, or js: URLs).
    public partial class MainWindow
    {
        private enum DocKind { None, Markdown, Html }
        private DocKind _docKind = DocKind.None;
        private bool _previewOpen;

        // Created on first preview open, disposed on close: a hosted WebBrowser (IE
        // ActiveX) adds message-loop overhead to the whole window just by existing,
        // so it must never sit idle in the tree.
        private WebBrowser? _previewBrowser;

        private WebBrowser PreviewBrowserLazy()
        {
            if (_previewBrowser == null)
            {
                _previewBrowser = new WebBrowser();
                _previewBrowser.Navigating += PreviewBrowser_Navigating;
                PreviewPane.Child = _previewBrowser;
            }
            return _previewBrowser;
        }

        // DisableHtml: raw HTML embedded inside markdown is ignored rather than rendered,
        // so the markdown path can never smuggle active content past StripActiveContent.
        private static readonly MarkdownPipeline MdPipeline =
            new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

        private string EditorPlainText() =>
            new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;

        /// <summary>Re-detects the note kind; shows/hides the toggle and refreshes an open pane.
        /// Called after a note loads and after every autosave.</summary>
        private void UpdatePreviewState()
        {
            string text = EditorPlainText();
            _docKind = DetectDocKind(text);
            bool detected = _docKind != DocKind.None;
            PreviewBtn.Visibility = detected ? Visibility.Visible : Visibility.Collapsed;
            PreviewBtn.ToolTip = Loc(_docKind == DocKind.Html ? "Str_TT_PreviewHtml" : "Str_TT_PreviewMd");
            if (!detected && _previewOpen) ClosePreview();
            else if (_previewOpen) RenderPreview(text);
        }

        // Cheap heuristics on the plain text. HTML wins when both could match, because
        // real HTML usually contains markdown-ish characters too.
        private static DocKind DetectDocKind(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return DocKind.None;
            if (Regex.Matches(t, @"</?[a-zA-Z][a-zA-Z0-9]*(\s[^<>]*)?>").Count >= 3) return DocKind.Html;

            int signals = 0;
            if (Regex.IsMatch(t, @"(?m)^#{1,6}\s")) signals++;          // # headers
            if (Regex.IsMatch(t, @"(?m)^\s*[-*+]\s+\S")) signals++;      // bullet lists
            if (Regex.IsMatch(t, @"(?m)^\s*\d+\.\s+\S")) signals++;      // numbered lists
            if (t.Contains("```")) signals++;                            // fenced code
            if (Regex.IsMatch(t, @"\[[^\]]+\]\([^)]+\)")) signals++;     // [text](link)
            if (Regex.IsMatch(t, @"\*\*[^*]+\*\*")) signals++;           // **bold**
            if (Regex.IsMatch(t, @"(?m)^>\s")) signals++;                // blockquote
            if (Regex.IsMatch(t, @"(?m)^\|.+\|\s*$")) signals++;         // | table |
            return signals >= 2 ? DocKind.Markdown : DocKind.None;
        }

        private void TogglePreview_Click(object sender, RoutedEventArgs e)
        {
            if (_previewOpen) { ClosePreview(); return; }
            _previewOpen = true;
            PreviewPane.Visibility = Visibility.Visible;
            PreviewCol.Width = new GridLength(1, GridUnitType.Star);
            RenderPreview(EditorPlainText());
        }

        private void ClosePreview()
        {
            _previewOpen = false;
            PreviewPane.Visibility = Visibility.Collapsed;
            PreviewCol.Width = new GridLength(0);
            if (_previewBrowser != null)
            {
                PreviewPane.Child = null;
                _previewBrowser.Dispose();
                _previewBrowser = null;
            }
        }

        private void RenderPreview(string text)
        {
            try
            {
                string body = _docKind == DocKind.Markdown
                    ? Markdown.ToHtml(text, MdPipeline)
                    : StripActiveContent(text);
                PreviewBrowserLazy().NavigateToString(BuildHtmlShell(body));
            }
            catch (Exception ex) { StatusText.Text = string.Format(Loc("Str_St_PreviewFailed"), ex.Message); }
        }

        // Defuse an HTML note before it reaches the IE engine: strip scripts, event-handler
        // attributes, frames/objects, and javascript: URLs. Belt and braces - the pane is a
        // viewer, never a place where a pasted page gets to run.
        private static string StripActiveContent(string html)
        {
            html = Regex.Replace(html, @"<script[\s\S]*?</script\s*>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<script[^>]*>", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<(iframe|frame|object|embed|applet)[\s\S]*?(</\1\s*>|/>)", "",
                RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"\son\w+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", "", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"javascript\s*:", "blocked:", RegexOptions.IgnoreCase);
            return html;
        }

        // Wraps the body in a shell styled from the live theme so the preview reads as part
        // of the pane. IE=edge meta keeps the WebBrowser control in IE11 mode, not IE7.
        private string BuildHtmlShell(string body)
        {
            string bg     = BrushHex("PaneBrush", "#1c1c1c");
            string fg     = BrushHex("TextBrush", "#e0e0e0");
            string accent = BrushHex("PrimaryBrush", "#B982E3");
            string border = BrushHex("CardBorderBrush", "#3a3a3a");
            return "<!DOCTYPE html><html><head>" +
                "<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"/><meta charset=\"utf-8\"/>" +
                "<style>" +
                $"body{{background:{bg};color:{fg};font-family:'Segoe UI',sans-serif;font-size:13px;margin:12px}}" +
                $"a{{color:{accent}}}" +
                $"code,pre{{font-family:Consolas,monospace;background:{border};border-radius:3px;padding:1px 4px}}" +
                "pre{padding:8px;overflow-x:auto}" +
                $"table{{border-collapse:collapse}}th,td{{border:1px solid {border};padding:3px 8px}}" +
                $"blockquote{{border-left:3px solid {accent};margin-left:0;padding-left:10px}}" +
                "img{max-width:100%}" +
                "</style></head><body>" + body + "</body></html>";
        }

        private string BrushHex(string key, string fallback) =>
            TryFindResource(key) is System.Windows.Media.SolidColorBrush b
                ? $"#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}"
                : fallback;

        // Clicked links open in the default browser instead of navigating the pane.
        // e.Uri is null for NavigateToString content - let that through.
        private void PreviewBrowser_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
        {
            if (e.Uri == null) return;
            e.Cancel = true;
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch { /* no browser - ignore */ }
        }
    }
}
