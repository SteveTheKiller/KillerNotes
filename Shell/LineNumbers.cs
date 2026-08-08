using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace KillerNotes.Shell
{
    // Optional line-number gutter, VS Code style: one number per LOGICAL line - each paragraph,
    // each list bullet, each table-cell paragraph, each embedded object - so a long paragraph
    // that wraps carries ONE number, exactly like VS Code with word wrap on. The gutter is a
    // Canvas left of the editor (MainWindow.xaml: GutterCol / LineGutter); visible numbers are
    // mapped into the gutter with TransformToVisual so they track scroll and both zooms, using
    // the editor's own font and size (times the per-note zoom, since the gutter sits outside
    // the editor's zoom transform). Toggle from the rail button or F11; remembered per app.
    //
    // REWRITTEN 2026-08-08 (#16, MrPapaya-JRR). The original numbered every VISUAL line by
    // walking the whole document with GetLineStartPosition on EVERY TextChanged, SizeChanged
    // and ScrollChanged event. That walk forces WPF to lay out and format every line it
    // crosses, so a 6000-line note re-formatted IN FULL on every scroll tick: minutes of Not
    // Responding, gigabytes of allocation churn - and, because the setting persists and the
    // last note loads at startup, an app that hung before its first paint, locking the user
    // out of their own database. Two rules keep it scalable now; do not violate either:
    //   1. NOTHING here may force layout outside the viewport. Numbering comes from a flat
    //      list of the document's leaf blocks - an object-graph walk that touches no layout -
    //      and only the handful of on-screen blocks ever get a GetCharacterRect call.
    //   2. NOTHING here runs once per event. Every trigger schedules ONE rebuild on a 150ms
    //      Background-priority timer, so a scroll storm or a loading document collapses to a
    //      single pass after the burst settles.
    public partial class MainWindow
    {
        private bool _lineNumbers;
        private DispatcherTimer? _gutterTimer;
        private readonly List<Block> _gutterBlocks = [];
        private readonly Dictionary<Block, int> _gutterIndex = [];
        private bool _gutterBlocksDirty = true;

        private void InitLineNumbers()
        {
            _lineNumbers = App.GetSetting("LineNumbers") == "1";
            Editor.TextChanged += (_, _) => { _gutterBlocksDirty = true; RebuildLineNumbers(); };
            Editor.SizeChanged += (_, _) => RebuildLineNumbers();
            Editor.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((_, _) => RebuildLineNumbers()));
            ApplyLineNumbers(_lineNumbers);
        }

        private void LineNumbers_Click(object sender, RoutedEventArgs e)
        {
            ApplyLineNumbers(!_lineNumbers);
            App.SetSetting("LineNumbers", _lineNumbers ? "1" : "0");
            FlashStatus(Loc(_lineNumbers ? "Str_St_LineNumOn" : "Str_St_LineNumOff"));
        }

        private void ApplyLineNumbers(bool on)
        {
            _lineNumbers = on;
            RebuildLineNumbers();
        }

        /// <summary>Schedules a gutter repaint. Named for its callers (zoom, toggles, note
        /// switches) - the actual work happens once, on the debounce timer, in
        /// RebuildLineNumbersNow.</summary>
        internal void RebuildLineNumbers()
        {
            if (_gutterTimer == null)
            {
                _gutterTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(150)
                };
                _gutterTimer.Tick += (_, _) => { _gutterTimer!.Stop(); RebuildLineNumbersNow(); };
            }
            _gutterTimer.Stop();
            _gutterTimer.Start();
        }

        private void RebuildLineNumbersNow()
        {
            if (LineGutter == null || GutterCol == null) return;
            LineGutter.Children.Clear();
            if (!_lineNumbers || _currentId < 0)
            {
                if (GutterCol.Width.Value != 0) GutterCol.Width = new GridLength(0);
                return;
            }

            if (_gutterBlocksDirty) RebuildGutterBlocks();

            double zoom = _editorZoom <= 0 ? 1 : _editorZoom;
            double fontSize = Editor.FontSize * zoom;   // match the editor text (gutter is outside its zoom transform)
            var fontFamily = Editor.FontFamily;
            double h = LineGutter.ActualHeight;

            // Place numbers for the visible blocks only. Finding the first visible block and
            // reading rects for the on-screen ones is the ONLY layout this file touches.
            try
            {
                var toGutter = Editor.TransformToVisual(LineGutter);
                int first = FirstVisibleGutterIndex();
                int emptyStreak = 0;
                for (int i = first; i < _gutterBlocks.Count; i++)
                {
                    var rect = _gutterBlocks[i].ElementStart.GetCharacterRect(LogicalDirection.Forward);
                    if (rect.IsEmpty)
                    {
                        // Block not laid out yet. A big note keeps formatting in the background
                        // after load, and walking on through thousands of unformatted blocks
                        // would FORCE that formatting - the exact #16 hang this rewrite exists
                        // to kill. Tolerate a couple (a collapsed or odd block mid-viewport),
                        // then stop; the pass after the next scroll/layout event resumes from
                        // wherever layout has reached.
                        if (++emptyStreak >= 3) break;
                        continue;
                    }
                    emptyStreak = 0;
                    Point p = toGutter.Transform(rect.TopLeft);
                    if (p.Y > h + 8) break;                    // below the viewport: done
                    if (p.Y < -rect.Height - 8) continue;      // above it (partial first block)
                    var tb = new TextBlock
                    {
                        Text = (i + 1).ToString(),
                        FontFamily = fontFamily,
                        FontSize = fontSize,
                    };
                    tb.SetResourceReference(TextElement.ForegroundProperty, "DimTextBrush");
                    Canvas.SetTop(tb, p.Y);
                    Canvas.SetRight(tb, 4);
                    LineGutter.Children.Add(tb);
                }
            }
            catch { /* layout not ready or an odd document - skip this pass, never crash */ }

            // Reserve at least two digits so the gutter (and the editor) do not shift when the
            // count crosses 9 -> 10. The width tracks the TOTAL count - known from the block
            // list without touching layout - so it stays put while scrolling.
            int n = _gutterBlocks.Count;
            int digits = Math.Max(2, n.ToString().Length);
            double want = n > 0 ? digits * fontSize * 0.62 + 8 : 0;
            if (Math.Abs(GutterCol.Width.Value - want) > 0.5)
                GutterCol.Width = new GridLength(want);
        }

        /// <summary>Flat list of the document's numberable leaf blocks, in document order.
        /// A pure object-graph walk: no TextPointers, no layout, cheap even at 6000 lines.
        /// Rebuilt lazily after edits (_gutterBlocksDirty).</summary>
        private void RebuildGutterBlocks()
        {
            _gutterBlocks.Clear();
            _gutterIndex.Clear();
            AddGutterBlocks(Editor.Document.Blocks);
            _gutterBlocksDirty = false;
        }

        private void AddGutterBlocks(BlockCollection blocks)
        {
            foreach (Block b in blocks)
            {
                switch (b)
                {
                    case List list:
                        foreach (ListItem li in list.ListItems) AddGutterBlocks(li.Blocks);
                        break;
                    case Table table:
                        foreach (TableRowGroup g in table.RowGroups)
                            foreach (TableRow r in g.Rows)
                                foreach (TableCell c in r.Cells)
                                    AddGutterBlocks(c.Blocks);
                        break;
                    case Section s:
                        AddGutterBlocks(s.Blocks);
                        break;
                    default:   // Paragraph, BlockUIContainer, rules - one number each
                        _gutterIndex[b] = _gutterBlocks.Count;
                        _gutterBlocks.Add(b);
                        break;
                }
            }
        }

        /// <summary>Index of the first block visible in the viewport, found from the character
        /// under the editor's top-left corner - a viewport-local query, no document walk.</summary>
        private int FirstVisibleGutterIndex()
        {
            var pos = Editor.GetPositionFromPoint(new Point(2, 2), true);
            if (pos == null) return 0;
            DependencyObject? d = pos.Paragraph ?? (DependencyObject?)pos.Parent;
            while (d != null)
            {
                if (d is Block b && _gutterIndex.TryGetValue(b, out int i))
                    // Step back one so a block scrolled partly above the top edge still shows
                    // its number when its first line is within the tolerance band.
                    return Math.Max(0, i - 1);
                d = d is FrameworkContentElement fce ? fce.Parent
                    : d is FrameworkElement fe ? fe.Parent : null;
            }
            return 0;
        }
    }
}
