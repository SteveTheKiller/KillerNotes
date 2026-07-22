using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace KillerNotes
{
    // Optional line-number gutter, code-editor style: a number for EVERY line in the editor
    // (each Enter, each list bullet, each blank line, each row of content), continuous like
    // VS Code. The gutter is a Canvas left of the editor (MainWindow.xaml: GutterCol /
    // LineGutter); numbers are placed by walking the document's rendered lines
    // (TextPointer.GetLineStartPosition) and mapping each line's caret rect into the gutter with
    // TransformToVisual, so they track the editor scroll and both zooms. The numbers use the
    // editor's own font and size (times the per-note zoom, since the gutter is outside the
    // editor's zoom transform) so they line up vertically with the text. Rebuilt on
    // edit / scroll / resize / zoom. Toggle from the rail button or F11; remembered per app.
    public partial class MainWindow
    {
        private bool _lineNumbers;

        private void InitLineNumbers()
        {
            _lineNumbers = App.GetSetting("LineNumbers") == "1";
            Editor.TextChanged += (_, _) => RebuildLineNumbers();
            Editor.SizeChanged += (_, _) => RebuildLineNumbers();
            Editor.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((_, _) => RebuildLineNumbers()));
            ApplyLineNumbers(_lineNumbers);
        }

        private void LineNumbers_Click(object sender, RoutedEventArgs e)
        {
            ApplyLineNumbers(!_lineNumbers);
            App.SetSetting("LineNumbers", _lineNumbers ? "1" : "0");
            StatusText.Text = Loc(_lineNumbers ? "Str_St_LineNumOn" : "Str_St_LineNumOff");
        }

        private void ApplyLineNumbers(bool on)
        {
            _lineNumbers = on;
            RebuildLineNumbers();
        }

        // Repaints the gutter. Only on-screen lines get a TextBlock, but the counter runs
        // through every line (top to bottom) so the numbering is correct and the width can be
        // sized from the TOTAL count.
        internal void RebuildLineNumbers()
        {
            if (LineGutter == null || GutterCol == null) return;
            LineGutter.Children.Clear();
            if (!_lineNumbers || _currentId < 0)
            {
                if (GutterCol.Width.Value != 0) GutterCol.Width = new GridLength(0);
                return;
            }

            double zoom = _editorZoom <= 0 ? 1 : _editorZoom;
            double fontSize = Editor.FontSize * zoom;   // match the editor text (gutter is outside its zoom transform)
            var fontFamily = Editor.FontFamily;
            double h = LineGutter.ActualHeight;
            int n = 0;

            // Walk every rendered line. GetLineStartPosition moves by visual line, so it needs the
            // editor laid out (fine after first render). Wrapped in try so a not-yet-laid-out
            // document or an odd structure skips this pass instead of crashing.
            try
            {
                var toGutter = Editor.TransformToVisual(LineGutter);
                TextPointer? pos = Editor.Document.ContentStart.GetLineStartPosition(0)
                                   ?? Editor.Document.ContentStart;
                bool pastBottom = false;   // below the viewport: keep counting (for width), stop placing
                for (int guard = 0; pos != null && guard < 200000; guard++)
                {
                    n++;
                    if (!pastBottom)
                    {
                        var rect = pos.GetCharacterRect(LogicalDirection.Forward);
                        if (!rect.IsEmpty)
                        {
                            Point p = toGutter.Transform(rect.TopLeft);
                            if (p.Y > h + 8) pastBottom = true;
                            else if (p.Y >= -rect.Height - 8)   // on-screen
                            {
                                var tb = new TextBlock
                                {
                                    Text = n.ToString(),
                                    FontFamily = fontFamily,
                                    FontSize = fontSize,
                                };
                                tb.SetResourceReference(TextElement.ForegroundProperty, "DimTextBrush");
                                Canvas.SetTop(tb, p.Y);
                                Canvas.SetRight(tb, 4);
                                LineGutter.Children.Add(tb);
                            }
                        }
                    }
                    TextPointer? next = pos.GetLineStartPosition(1, out int moved);
                    if (moved < 1 || next == null || next.CompareTo(pos) <= 0) break;
                    pos = next;
                }
            }
            catch { /* layout not ready or an odd document - skip this pass, never crash */ }

            // Reserve at least two digits so the gutter (and the editor) do not shift when the
            // count crosses 9 -> 10; the width tracks the TOTAL count so it stays put while
            // scrolling, and only ever grows (never jumps back to 1 digit).
            int digits = System.Math.Max(2, n.ToString().Length);
            double want = n > 0 ? digits * fontSize * 0.62 + 8 : 0;
            if (System.Math.Abs(GutterCol.Width.Value - want) > 0.5)
                GutterCol.Width = new GridLength(want);
        }
    }
}
