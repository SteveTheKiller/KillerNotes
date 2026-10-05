using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // Optional markdown/HTML preview. When the note's plain text looks like markdown or
    // HTML, a Preview submenu appears in the format bar with three picks: Source (raw),
    // Rendered (the WYSIWYG page), and Split (both side by side). F4 cycles them. The
    // rendered page is a WPF FlowDocument, drawn by WPF like every other surface: markdown
    // converts through MarkdownConvert, HTML through HtmlConvert, and neither runs
    // anything. The last picked mode persists per app - a note that opens as undetected
    // always starts in Source.
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

        // Created on first preview open and dropped on Source, so an idle window carries no
        // second document. Read-only viewer with selection, so Ctrl+A / Ctrl+C still work.
        private FlowDocumentScrollViewer? _previewViewer;
        private DispatcherTimer? _previewRefreshTimer;

        // The preview's body size and face, matching what the page used before.
        private const double PreviewFontSize = 13;
        private const string PreviewFont = "Segoe UI";

        private FlowDocumentScrollViewer PreviewViewerLazy()
        {
            if (_previewViewer == null)
            {
                _previewViewer = new FlowDocumentScrollViewer
                {
                    IsToolBarVisible = false,
                    IsSelectionEnabled = true,
                    Focusable = true,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    BorderBrush = Brushes.Transparent,
                };
                _previewViewer.SetResourceReference(Control.ForegroundProperty, "TextBrush");
                _previewViewer.AddHandler(Hyperlink.RequestNavigateEvent,
                    new System.Windows.Navigation.RequestNavigateEventHandler(PreviewLink_RequestNavigate));
                _previewViewer.PreviewKeyDown += PreviewViewer_PreviewKeyDown;

                // The same film grain every other surface carries, behind the text.
                var grain = new Border { IsHitTestVisible = false };
                grain.SetResourceReference(Border.BackgroundProperty, "GrainTileBrush");
                grain.SetResourceReference(UIElement.OpacityProperty, "GrainOpacity");
                grain.SetBinding(Border.CornerRadiusProperty,
                    new System.Windows.Data.Binding(nameof(Border.CornerRadius)) { Source = PreviewPane });

                var host = new Grid();
                host.Children.Add(grain);
                host.Children.Add(_previewViewer);
                PreviewPane.Child = host;
            }
            return _previewViewer;
        }

        private ScrollViewer? PreviewScroller() =>
            _previewViewer?.Template?.FindName("PART_ContentHost", _previewViewer) as ScrollViewer;

        /// <summary>Reading keys for the preview: Home, End, Page Up/Down, the arrows and Space
        /// scroll it, the same set the editor answers to.</summary>
        private void PreviewViewer_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var sv = PreviewScroller();
            if (sv == null || Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
                Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            switch (e.Key)
            {
                case Key.Home:     sv.ScrollToTop(); break;
                case Key.End:      sv.ScrollToBottom(); break;
                case Key.PageUp:   sv.PageUp(); break;
                case Key.PageDown: sv.PageDown(); break;
                case Key.Up:       sv.ScrollToVerticalOffset(sv.VerticalOffset - 40); break;
                case Key.Down:     sv.ScrollToVerticalOffset(sv.VerticalOffset + 40); break;
                case Key.Space:    if (shift) sv.PageUp(); else sv.PageDown(); break;
                default: return;
            }
            e.Handled = true;
        }

        // Clicked links open in the default browser. The converters only ever make http,
        // https and mailto links, so nothing else can arrive here.
        private void PreviewLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            e.Handled = true;
            if (e.Uri == null) return;
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch { /* no browser - ignore */ }
        }

        /// <summary>The top meets the format bar; only the outer bottom corners are rounded.</summary>
        private void ApplyPreviewCorners()
        {
            var r = TryFindResource("PanelCornerRadius") is CornerRadius cr ? cr : new CornerRadius(4);
            PreviewPane.CornerRadius = _previewMode == PreviewMode.Rendered
                ? new CornerRadius(0, 0, r.BottomRight, r.BottomLeft)
                : new CornerRadius(0, 0, r.BottomRight, 0);
        }

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
            // A note stored as markdown is markdown whatever its text looks like, so it always
            // gets the preview and F4. Detection only guesses for rich-text notes.
            _docKind = CurrentIsMarkdown ? DocKind.Markdown
                : DetectMarkdownGlobally ? DetectDocKind(text) : DocKind.None;
            bool detected = _docKind != DocKind.None;
            PreviewMenuItem.Visibility = detected ? Visibility.Visible : Visibility.Collapsed;
            PreviewModeBtn.Visibility = PreviewMenuItem.Visibility;
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
            // The format-bar chip names the current mode, so it is always visible which view is up.
            PreviewModeText.Text = Loc(_previewMode switch
            {
                PreviewMode.Rendered => "Str_Preview_Rendered",
                PreviewMode.Split    => "Str_Preview_Split",
                _                    => "Str_Preview_Source",
            });
        }

        private void PreviewModeBtn_Click(object sender, RoutedEventArgs e) => CyclePreviewMode();

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

        // HTML detection requires real HTML tags outside Markdown code spans and fences.
        // Angle-bracket placeholders in technical notes are ordinary text.
        private static DocKind DetectDocKind(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return DocKind.None;
            if (PreviewDocument.HasHtml(t)) return DocKind.Html;

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
        /// visibility, and brings up or drops the preview viewer.</summary>
        private void SetPreviewMode(PreviewMode mode, bool persist = true)
        {
            if (mode == _previewMode) { SyncPreviewMenuChecks(); return; }
            _previewMode = mode;
            switch (mode)
            {
                case PreviewMode.Source:
                    _previewRefreshTimer?.Stop();
                    EditorCol.Width = new GridLength(1, GridUnitType.Star);
                    Editor.Visibility = Visibility.Visible;
                    PreviewCol.Width = new GridLength(0);
                    PreviewPane.Visibility = Visibility.Collapsed;
                    if (_previewViewer != null)
                    {
                        // Focus leaving with the viewer would land nowhere, so hand it to the note.
                        bool hadFocus = _previewViewer.IsKeyboardFocusWithin;
                        _previewViewer = null;
                        PreviewPane.Child = null;
                        if (hadFocus) Editor.Focus();
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
            // Only Split has a draggable divider, and only Split needs the limits that keep either
            // side from being dragged away to nothing. The other modes set a column to 0.
            bool split = mode == PreviewMode.Split;
            PreviewDividerCol.Width = new GridLength(split ? 6 : 0);
            EditorCol.MinWidth = split ? 200 : 0;
            PreviewCol.MinWidth = split ? 200 : 0;
            PreviewSplitter.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
            SyncPreviewMenuChecks();
            if (persist) App.SetSetting(PreviewModeSettingKey, mode.ToString());
        }

        private void RenderPreview(string text, bool preserveScroll = false)
        {
            try
            {
                var viewer = PreviewViewerLazy();
                double? keep = preserveScroll ? PreviewScroller()?.VerticalOffset : null;

                FlowDocument doc = PreviewDocument.Render(text, PreviewFontSize);
                doc.FontFamily = new FontFamily(PreviewFont);
                doc.PagePadding = new Thickness(12, 12, 18, 12);
                doc.Background = Brushes.Transparent;
                doc.SetResourceReference(FlowDocument.ForegroundProperty, "TextBrush");
                StylePreviewBlocks(doc.Blocks);

                ApplyPreviewCorners();
                viewer.Document = doc;

                if (keep.HasValue)
                    Dispatcher.BeginInvoke(new Action(() => PreviewScroller()?.ScrollToVerticalOffset(keep.Value)),
                        DispatcherPriority.Loaded);
                // Rendered mode hides the editor, so give the preview the keyboard: Home, End,
                // Page Up/Down and the arrows then scroll it.
                if (_previewMode == PreviewMode.Rendered && !preserveScroll)
                    Dispatcher.BeginInvoke(new Action(() => _previewViewer?.Focus()), DispatcherPriority.Input);
            }
            catch (Exception ex) { StatusText.Text = string.Format(Loc("Str_St_PreviewFailed"), ex.Message); }
        }

        /// <summary>Theme-live colors for the converted document: links take the accent, code
        /// takes the code tint. Resource references, so a theme switch restyles them in place.</summary>
        private static void StylePreviewBlocks(BlockCollection blocks)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case Paragraph p:
                        if (IsCodeFont(p.FontFamily)) p.SetResourceReference(TextElement.BackgroundProperty, "CardBorderBrush");
                        StylePreviewInlines(p.Inlines);
                        break;
                    case Section s:
                        StylePreviewBlocks(s.Blocks);
                        break;
                    case List l:
                        foreach (var li in l.ListItems) StylePreviewBlocks(li.Blocks);
                        break;
                    case Table t:
                        t.SetResourceReference(Block.BorderBrushProperty, "CardBorderBrush");
                        foreach (var g in t.RowGroups)
                            foreach (var row in g.Rows)
                                foreach (var cell in row.Cells)
                                {
                                    cell.SetResourceReference(TableCell.BorderBrushProperty, "CardBorderBrush");
                                    StylePreviewBlocks(cell.Blocks);
                                }
                        break;
                }
            }
        }

        private static void StylePreviewInlines(InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Hyperlink h:
                        h.SetResourceReference(TextElement.ForegroundProperty, "PrimaryBrush");
                        StylePreviewInlines(h.Inlines);
                        break;
                    case Span s:
                        StylePreviewInlines(s.Inlines);
                        break;
                    case Run r when r.ReadLocalValue(TextElement.FontFamilyProperty) is FontFamily f && IsCodeFont(f):
                        r.SetResourceReference(TextElement.BackgroundProperty, "CardBorderBrush");
                        break;
                }
            }
        }

        private static bool IsCodeFont(FontFamily? f) =>
            f != null && f.Source.IndexOf("Consolas", StringComparison.OrdinalIgnoreCase) >= 0;

        // Theme color as #RRGGBB for the HTML export (ImportExport.cs).
        private string BrushHex(string key, string fallback) =>
            TryFindResource(key) is System.Windows.Media.SolidColorBrush b
                ? $"#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}"
                : fallback;
    }
}
