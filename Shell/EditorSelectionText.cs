using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KillerNotes.Shell
{
    // WHITE selected text in the note editor (98SE's navy selection block).
    //
    // WPF's RichTextBox cannot recolor selected text. SelectionTextBrush and non-adorner
    // selection rendering are documented for TextBox and PasswordBox ONLY (Microsoft's 4.8
    // compatibility notes list exactly those two as the affected APIs), so the editor's
    // selection is always a rectangle painted OVER the glyphs at SelectionOpacity. An
    // era-correct solid navy therefore erased the text outright, and translucent navy washed
    // out to purple over the white page - both were shipped and rejected on 2026-08-08
    // before this file existed. The theme brushes were never the problem.
    //
    // So the white text is drawn BY HAND: an adorner in the window's adorner layer re-renders
    // the selected glyphs in TextSelectionTextBrush on top of the selection rectangle. The
    // DOCUMENT IS NEVER TOUCHED - no ApplyPropertyValue, no formatting - so the undo stack,
    // the dirty flag, autosave and syntax highlighting never see any of this, and nothing can
    // bake white-on-white runs into a saved note. The worst possible failure mode is a
    // cosmetic misdraw for one frame.
    //
    // Enabled ONLY by the explicit EditorSelectionOverlay boolean, which only 98SE's palette
    // states. Do NOT gate on TextSelectionTextBrush existing: ThemeManager synthesizes that
    // key for EVERY theme (aliased from TextBrush), so a brush-existence gate is always true -
    // that mistake ran this overlay on all thirteen themes and painted solid unfocused fills
    // over notes and images on the twelve that expect a translucent wash (2026-08-08).
    // It also renders while the editor is UNFOCUSED (ribbon clicks, the text-color picker) -
    // and in that state it draws the FILL itself too. WPF's own inactive-selection highlight
    // is not usable: IsInactiveSelectionHighlightEnabled paints the user's WINDOWS accent
    // color at full strength over the glyphs and resolves that brush past the app's resource
    // dictionaries, so it is left off (see the note in ThemeManager.LoadDict) and this
    // adorner supplies both the accent fill and the white text when focus is elsewhere.
    public partial class MainWindow
    {
        private void InitSelectionTextOverlay()
        {
            Editor.Loaded += (_, _) =>
            {
                var layer = AdornerLayer.GetAdornerLayer(Editor);
                if (layer == null) return;   // no AdornerDecorator above the editor: feature off, nothing breaks
                var existing = layer.GetAdorners(Editor);
                if (existing != null)
                    foreach (var a in existing)
                        if (a is SelectionTextAdorner) return;   // Loaded refires; never stack two
                layer.Add(new SelectionTextAdorner(Editor));
            };
        }
    }

    internal sealed class SelectionTextAdorner : Adorner
    {
        private readonly RichTextBox _rtb;

        public SelectionTextAdorner(RichTextBox rtb) : base(rtb)
        {
            _rtb = rtb;
            IsHitTestVisible = false;
            rtb.SelectionChanged += (_, _) => InvalidateVisual();
            rtb.TextChanged += (_, _) => InvalidateVisual();
            rtb.SizeChanged += (_, _) => InvalidateVisual();
            rtb.IsKeyboardFocusWithinChanged += (_, _) => InvalidateVisual();
            // Scrolling moves the selection rectangles; the inner ScrollViewer's event bubbles.
            rtb.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((_, _) => InvalidateVisual()));
        }

        protected override void OnRender(DrawingContext dc)
        {
            // A render pass must never take the app down; the next invalidation redraws.
            try { RenderSelection(dc); }
            catch { }
        }

        private void RenderSelection(DrawingContext dc)
        {
            // Theme gate: the EXPLICIT opt-in key, never brush existence (see the class comment).
            if (Application.Current?.TryFindResource("EditorSelectionOverlay") is not true) return;
            if (Application.Current?.TryFindResource("TextSelectionTextBrush") is not Brush brush) return;
            var sel = _rtb.Selection;
            if (sel == null || sel.IsEmpty || !_rtb.IsLoaded) return;

            // Clamp to the viewport: GetPositionFromPoint snaps to the nearest character, so a
            // select-all on a large note walks only the visible slice, not the whole document.
            TextPointer start = sel.Start, end = sel.End;
            var topPos = _rtb.GetPositionFromPoint(new Point(0, 0), true);
            var bottomPos = _rtb.GetPositionFromPoint(
                new Point(Math.Max(0, _rtb.ActualWidth - 1), Math.Max(0, _rtb.ActualHeight - 1)), true);
            if (topPos != null && topPos.CompareTo(start) > 0) start = topPos;
            if (bottomPos != null && bottomPos.CompareTo(end) < 0) end = bottomPos;
            if (start.CompareTo(end) >= 0) return;

            dc.PushClip(new RectangleGeometry(new Rect(_rtb.RenderSize)));
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            // Focused: WPF paints the selection fill (SelectionBrush) and this adorner adds only
            // the text. Unfocused: WPF paints NOTHING (inactive highlighting is deliberately off,
            // see the class comment), so the adorner draws the accent fill under each segment too.
            Brush? fill = _rtb.IsKeyboardFocusWithin
                ? null
                : Application.Current.TryFindResource("TextSelectionBrush") as Brush;

            // Line by line, because a wrapped run needs one draw per visual line.
            var lineStart = start;
            while (lineStart != null && lineStart.CompareTo(end) < 0)
            {
                TextPointer nextLine = lineStart.GetLineStartPosition(1);
                TextPointer lineEnd = (nextLine == null || nextLine.CompareTo(end) > 0) ? end : nextLine;
                DrawLineSegment(dc, lineStart, lineEnd, brush, fill, ppd);
                lineStart = nextLine;
            }
            dc.Pop();
        }

        private void DrawLineSegment(DrawingContext dc, TextPointer pos, TextPointer lineEnd, Brush brush, Brush? fill, double ppd)
        {
            while (pos != null && pos.CompareTo(lineEnd) < 0)
            {
                if (pos.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string run = pos.GetTextInRun(LogicalDirection.Forward);
                    int len = Math.Min(run.Length, pos.GetOffsetToPosition(lineEnd));
                    if (len <= 0) return;
                    Rect rect = pos.GetCharacterRect(LogicalDirection.Forward);
                    if (rect != Rect.Empty && rect.Bottom > 0 && rect.Top < _rtb.ActualHeight)
                    {
                        // Each segment redraws with ITS run's own font, size, style and
                        // decorations, read as effective (inherited) values, so bold, italic,
                        // sizes and underlines all land exactly where the black glyphs are.
                        var elem = pos.Parent as TextElement;
                        var typeface = new Typeface(
                            (FontFamily)(elem?.GetValue(TextElement.FontFamilyProperty) ?? _rtb.FontFamily),
                            elem?.GetValue(TextElement.FontStyleProperty) is FontStyle fs ? fs : FontStyles.Normal,
                            elem?.GetValue(TextElement.FontWeightProperty) is FontWeight fw ? fw : FontWeights.Normal,
                            elem?.GetValue(TextElement.FontStretchProperty) is FontStretch fst ? fst : FontStretches.Normal);
                        double size = elem?.GetValue(TextElement.FontSizeProperty) is double d ? d : _rtb.FontSize;
                        var ft = new FormattedText(run.Substring(0, len), CultureInfo.CurrentUICulture,
                            _rtb.FlowDirection, typeface, size, brush, null, TextFormattingMode.Display, ppd);
                        if ((elem as Inline)?.TextDecorations is { Count: > 0 } deco)
                            ft.SetTextDecorations(deco);
                        if (fill != null)
                            dc.DrawRectangle(fill, null,
                                new Rect(rect.TopLeft, new Size(ft.WidthIncludingTrailingWhitespace, rect.Height)));
                        dc.DrawText(ft, rect.TopLeft);
                    }
                    pos = pos.GetPositionAtOffset(len);
                }
                else pos = pos.GetNextContextPosition(LogicalDirection.Forward);
            }
        }
    }
}
