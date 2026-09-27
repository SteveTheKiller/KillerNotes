using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Markdig;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // Optional markdown/HTML preview. When the note's plain text looks like markdown or
    // HTML, a Preview submenu appears in the format bar with three picks: Source (raw),
    // Rendered (the WYSIWYG page), and Split (both side by side). F4 cycles them. The
    // rendered page draws through the built-in WPF WebBrowser (IE engine). Markdown
    // converts via Markdig; HTML notes are defused first (no scripts, handlers, frames,
    // or js: URLs). The last picked mode persists per app - a note that opens as
    // undetected always starts in Source.
    public partial class MainWindow
    {
        private enum DocKind { None, Markdown, Html }
        private DocKind _docKind = DocKind.None;

        // Source: editor full width, no browser.  Rendered: browser full width, editor
        // hidden.  Split: both side by side (the pre-1.3.2 shape). Persisted app-wide
        // under the PreviewMode setting, so a tech who lives in Rendered gets Rendered
        // on the next detected note without re-picking every time.
        private enum PreviewMode { Source, Rendered, Split }
        private PreviewMode _previewMode = PreviewMode.Source;

        // Legacy shorthand: everything that used to ask "is the pane open" wants to know
        // "am I in Rendered or Split", so a computed alias keeps those call sites working.
        private bool PreviewOpen => _previewMode != PreviewMode.Source;

        private const string PreviewModeSettingKey = "PreviewMode";

        // Created on first preview open, disposed on close: a hosted WebBrowser (IE
        // ActiveX) adds message-loop overhead to the whole window just by existing,
        // so it must never sit idle in the tree.
        private WebBrowser? _previewBrowser;
        private DispatcherTimer? _previewRefreshTimer;
        private double? _pendingPreviewScroll;

        private WebBrowser PreviewBrowserLazy()
        {
            if (_previewBrowser == null)
            {
                _previewBrowser = new WebBrowser();
                _previewBrowser.Navigating += PreviewBrowser_Navigating;
                _previewBrowser.LoadCompleted += (_, _) => RestorePreviewScroll();
                // Born hidden if an overlay is up (airspace, see SetPreviewOverlayHidden).
                if (ShortcutOverlay.Visibility == Visibility.Visible ||
                    AboutOverlay.Visibility == Visibility.Visible)
                    _previewBrowser.Visibility = Visibility.Hidden;
                PreviewPane.Child = _previewBrowser;
            }
            return _previewBrowser;
        }

        /// <summary>The preview WebBrowser is a hosted NATIVE window, so it draws over
        /// every WPF element in its rectangle - including the F1/About overlays (WPF
        /// airspace). The overlay fade helpers (About.cs) hide the browser for the
        /// duration; Hidden (not Collapsed) keeps the split layout from shifting.</summary>
        private void SetPreviewOverlayHidden(bool hidden)
        {
            if (_previewBrowser == null) return;
            _previewBrowser.Visibility = hidden ? Visibility.Hidden : Visibility.Visible;
        }

        // DisableHtml: raw HTML embedded inside markdown is ignored rather than rendered,
        // so the markdown path can never smuggle active content past StripActiveContent.
        private static readonly MarkdownPipeline MdPipeline =
            new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

        private string EditorPlainText() =>
            new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;

        /// <summary>App-wide switch for the markdown/HTML detection (#14). On unless turned off.
        /// The detector itself is the real fix for the false positives - this is the blunt "stop
        /// guessing" option for anyone who never wants a preview offered.</summary>
        private static bool DetectMarkdownGlobally =>
            App.GetSetting("DetectMarkdown") != "off";

        /// <summary>The app-wide off switch for detection.</summary>
        private void PreviewDetectGlobal_Click(object sender, RoutedEventArgs e)
        {
            bool on = !DetectMarkdownGlobally;
            App.SetSetting("DetectMarkdown", on ? "on" : "off");
            UpdatePreviewState();
            FlashStatus(Loc(on ? "Str_St_PreviewGlobalOn" : "Str_St_PreviewGlobalOff"));
        }

        /// <summary>Re-applies the note's effective kind; shows/hides the picker and refreshes an
        /// open pane. Called after a note loads and after every autosave. On a note that stops
        /// being detected mid-edit, the mode snaps back to Source and the browser is torn down
        /// exactly as it would be on an explicit pick.</summary>
        private void UpdatePreviewState(bool preserveScroll = false)
        {
            string text = EditorPlainText();
            _docKind = DetectMarkdownGlobally ? DetectDocKind(text) : DocKind.None;
            bool detected = _docKind != DocKind.None;
            PreviewMenuItem.Visibility = detected ? Visibility.Visible : Visibility.Collapsed;
            PreviewMenuLabel.Text = Loc(_docKind == DocKind.Html ? "Str_TT_PreviewHtml" : "Str_TT_PreviewMd");
            SyncPreviewMenuChecks();
            if (!detected && PreviewOpen) SetPreviewMode(PreviewMode.Source, persist: false);
            else if (PreviewOpen) RenderPreview(text, preserveScroll);
        }

        /// <summary>Marks the current mode on the three-way submenu and clears the others.
        /// One is always shown as picked so the picker never looks empty.</summary>
        private void SyncPreviewMenuChecks()
        {
            if (PreviewSourceMenuItem == null) return;   // defensive - InitializeComponent may not have run
            PreviewSourceMenuItem.IsChecked   = _previewMode == PreviewMode.Source;
            PreviewRenderedMenuItem.IsChecked = _previewMode == PreviewMode.Rendered;
            PreviewSplitMenuItem.IsChecked    = _previewMode == PreviewMode.Split;
        }

        private void QueuePreviewRefresh()
        {
            if (!PreviewOpen || _loadingNote) return;
            if (_previewRefreshTimer == null)
            {
                _previewRefreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(250),
                };
                _previewRefreshTimer.Tick += (_, _) =>
                {
                    _previewRefreshTimer.Stop();
                    UpdatePreviewState(preserveScroll: true);
                };
            }
            _previewRefreshTimer.Stop();
            _previewRefreshTimer.Start();
        }

        // Cheap heuristics on the plain text. HTML wins when both could match, because
        // real HTML usually contains markdown-ish characters too.
        private static DocKind DetectDocKind(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return DocKind.None;
            if (Regex.Matches(t, @"</?[a-zA-Z][a-zA-Z0-9]*(\s[^<>]*)?>").Count >= 3) return DocKind.Html;

            // STRONG signals: syntax nobody types unless they mean markdown. One is enough.
            int strong = 0;
            if (Regex.IsMatch(t, @"(?m)^#{1,6}\s")) strong++;            // # headers
            if (t.Contains("```")) strong++;                             // fenced code
            if (Regex.IsMatch(t, @"\[[^\]]+\]\([^)]+\)")) strong++;      // [text](link)
            if (Regex.IsMatch(t, @"\*\*[^*]+\*\*")) strong++;            // **bold**
            if (Regex.IsMatch(t, @"(?m)^>\s")) strong++;                 // blockquote
            if (Regex.IsMatch(t, @"(?m)^\|.+\|\s*$")) strong++;          // | table |

            // Deliberately NOT signals: "- bullet" and "1. numbered" lines. They are how everyone
            // writes a plain-text checklist, and the old "any 2 of 8" rule let those two alone add
            // up to markdown - so every tech's cutover list grew a Preview button it never wanted
            // (#14, MrPapaya-JRR). Real markdown almost always carries a header, fence, link, bold,
            // quote or table as well, so requiring one strong signal keeps detection and drops the
            // false positives.
            return strong >= 1 ? DocKind.Markdown : DocKind.None;
        }

        // Three submenu picks - one radio-style choice at a time. Each drives the same
        // SetPreviewMode; the click handlers exist only because MenuItem.Click needs a
        // named target.
        private void PreviewSource_Click(object sender, RoutedEventArgs e)   => SetPreviewMode(PreviewMode.Source);
        private void PreviewRendered_Click(object sender, RoutedEventArgs e) => SetPreviewMode(PreviewMode.Rendered);
        private void PreviewSplit_Click(object sender, RoutedEventArgs e)    => SetPreviewMode(PreviewMode.Split);

        /// <summary>F4 handler. Cycles Source -> Rendered -> Split -> Source on a detected
        /// note, and is a no-op on an undetected one so the key never fires blind.</summary>
        private void CyclePreviewMode()
        {
            if (PreviewMenuItem.Visibility != Visibility.Visible) return;
            PreviewMode next = _previewMode switch
            {
                PreviewMode.Source   => PreviewMode.Rendered,
                PreviewMode.Rendered => PreviewMode.Split,
                _                    => PreviewMode.Source,
            };
            SetPreviewMode(next);
        }

        /// <summary>Applies a mode change: swaps the two column widths, toggles the editor's
        /// visibility, and brings up or tears down the WebBrowser. The IE ActiveX control
        /// stays out of the tree whenever the mode is Source so an idle window carries no
        /// hosted-native overhead.</summary>
        private void SetPreviewMode(PreviewMode mode, bool persist = true)
        {
            if (mode == _previewMode) { SyncPreviewMenuChecks(); return; }
            _previewMode = mode;
            switch (mode)
            {
                case PreviewMode.Source:
                    _previewRefreshTimer?.Stop();
                    _pendingPreviewScroll = null;
                    EditorCol.Width = new GridLength(1, GridUnitType.Star);
                    Editor.Visibility = Visibility.Visible;
                    PreviewCol.Width = new GridLength(0);
                    PreviewPane.Visibility = Visibility.Collapsed;
                    if (_previewBrowser != null)
                    {
                        PreviewPane.Child = null;
                        _previewBrowser.Dispose();
                        _previewBrowser = null;
                    }
                    break;

                case PreviewMode.Rendered:
                    EditorCol.Width = new GridLength(0);
                    Editor.Visibility = Visibility.Collapsed;
                    PreviewCol.Width = new GridLength(1, GridUnitType.Star);
                    PreviewPane.Visibility = Visibility.Visible;
                    RenderPreview(EditorPlainText());
                    break;

                case PreviewMode.Split:
                    EditorCol.Width = new GridLength(1, GridUnitType.Star);
                    Editor.Visibility = Visibility.Visible;
                    PreviewCol.Width = new GridLength(1, GridUnitType.Star);
                    PreviewPane.Visibility = Visibility.Visible;
                    RenderPreview(EditorPlainText());
                    break;
            }
            SyncPreviewMenuChecks();
            if (persist) App.SetSetting(PreviewModeSettingKey, mode.ToString());
        }

        private void RenderPreview(string text, bool preserveScroll = false)
        {
            try
            {
                _pendingPreviewScroll = preserveScroll ? PreviewScrollOffset() : null;
                string body = _docKind == DocKind.Markdown
                    ? Markdown.ToHtml(text, MdPipeline)
                    : StripActiveContent(text);
                PreviewBrowserLazy().NavigateToString(BuildHtmlShell(body));
            }
            catch (Exception ex) { StatusText.Text = string.Format(Loc("Str_St_PreviewFailed"), ex.Message); }
        }

        private double? PreviewScrollOffset()
        {
            if (_previewBrowser == null) return null;
            try
            {
                object value = _previewBrowser.InvokeScript("eval",
                [
                    "Math.max(document.documentElement.scrollTop||0,document.body.scrollTop||0)",
                ]);
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }

        private void RestorePreviewScroll()
        {
            if (_previewBrowser == null || !_pendingPreviewScroll.HasValue) return;
            double offset = _pendingPreviewScroll.Value;
            _pendingPreviewScroll = null;
            try
            {
                _previewBrowser.InvokeScript("eval",
                [
                    "window.scrollTo(0," + offset.ToString(CultureInfo.InvariantCulture) + ")",
                ]);
            }
            catch { }
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

        /// <summary>The window's film grain as a tiled PNG data URI, so the preview carries the
        /// same texture as every other surface.
        ///
        /// Everywhere else the grain is a GrainTileBrush layered over the content in a Border. The
        /// preview cannot do that: its content is a hosted WebBrowser, a native window that draws
        /// over any WPF element in its rectangle (airspace), so an overlay would be invisible. The
        /// tile is therefore regenerated here and baked into the page's CSS background.
        ///
        /// Same seed and same distribution as Chrome.ApplyGrainTexture, so the two match. The one
        /// difference is that GrainOpacity is multiplied into each pixel's alpha up front, because
        /// CSS cannot fade a background image the way the WPF overlay's Opacity does.
        ///
        /// Deterministic, so it is built once and cached for the process.
        /// </summary>
        private static string? _grainUri;

        private static string GrainDataUri()
        {
            if (_grainUri != null) return _grainUri;

            const int size = 256;
            double opacity = Application.Current.TryFindResource("GrainOpacity") is double o ? o : 0.24;

            var bmp = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[size * size * 4];   // starts fully transparent
            var rng = new Random(1337);               // same seed as the WPF tile
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (rng.Next(3) != 0) continue;       // ~33% pixel density
                bool bright = rng.Next(2) == 0;       // half bright, half dark
                byte v = bright ? (byte)rng.Next(190, 255) : (byte)rng.Next(0, 50);
                byte a = (byte)rng.Next(35, 95);
                pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
                pixels[i + 3] = (byte)(a * opacity);  // bake the overlay opacity in
            }
            bmp.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);

            using var ms = new MemoryStream();
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            enc.Save(ms);
            _grainUri = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            return _grainUri;
        }

        // Wraps the body in a shell styled from the live theme so the preview reads as part
        // of the pane. IE=edge meta keeps the WebBrowser control in IE11 mode, not IE7.
        private string BuildHtmlShell(string body)
        {
            // SurfaceBrush, not PaneBrush: the preview matches the format bar - a step darker than
            // the note and a step lighter than the window - so it reads as chrome, not more paper.
            string bg     = BrushHex("SurfaceBrush", "#0d0d0d");
            string fg     = BrushHex("TextBrush", "#e0e0e0");
            string accent = BrushHex("PrimaryBrush", "#B982E3");
            string border = BrushHex("CardBorderBrush", "#3a3a3a");
            return "<!DOCTYPE html><html><head>" +
                "<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"/><meta charset=\"utf-8\"/>" +
                "<style>" +
                // Scrollbar recolor: IE11's -ms- properties are the only ones the WebBrowser control
                // honors - Chromium/Firefox spellings don't apply in IE mode. Face is the accent, track
                // is the pane color, the 3D shading columns collapse to the same accent so the bar
                // reads as one solid lozenge instead of the beveled Win98 default. Applied to html AND
                // body (IE reads either depending on the doctype).
                $"html,body{{scrollbar-face-color:{accent};scrollbar-track-color:{bg};" +
                $"scrollbar-arrow-color:{bg};scrollbar-highlight-color:{accent};" +
                $"scrollbar-shadow-color:{accent};scrollbar-3dlight-color:{accent};" +
                $"scrollbar-darkshadow-color:{bg}}}" +
                // Kill horizontal scroll on the outer page - a long inline token could otherwise
                // introduce a horizontal bar the whole reading area doesn't need. Code blocks that
                // want to scroll horizontally keep their own overflow-x:auto below.
                "html{overflow-x:hidden}body{overflow-x:hidden}" +
                $"body{{background:{bg} url({GrainDataUri()}) repeat;color:{fg};" +
                "font-family:'Segoe UI',sans-serif;font-size:13px;margin:12px}}" +
                $"a{{color:{accent}}}" +
                $"code,pre{{font-family:Consolas,monospace;background:{border};border-radius:3px;padding:1px 4px}}" +
                "pre{padding:8px;overflow-x:auto}" +
                $"table{{border-collapse:collapse}}th,td{{border:1px solid {border};padding:3px 8px}}" +
                $"blockquote{{border-left:3px solid {accent};margin-left:0;padding-left:10px}}" +
                "img{max-width:100%}" +
                // Context menu off at DOCUMENT level: right-click otherwise pops the IE
                // engine's native menu (Back/Print/View source), which cannot be themed -
                // suppressing it is the only clean option. Document level matters: a body
                // attribute misses clicks in the empty space past the content, where the
                // event targets the root element. Ctrl+A / Ctrl+C still work.
                "</style></head><body>" + body +
                "<script>document.oncontextmenu=function(){return false};</script></body></html>";
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
