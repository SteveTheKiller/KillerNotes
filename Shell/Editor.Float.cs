using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

// Floating images and recordings (1.2.0).
//
// An embedded object normally sits INLINE: it occupies a slot in the line like an oversized
// character, so a wide image pushes the text above and below it and leaves the rest of the line
// empty. Floating lifts it out of the line so text wraps beside it, and lets it be dragged.
//
// THE MECHANISM IS Floater, AFTER Figure FAILED TWICE. Figure is the one that carries
// HorizontalOffset/VerticalOffset and WrapDirection.Both, so on paper it is the free-placement
// element - but RichTextBox implements a reduced subset of the FlowDocument layout, and in an
// EDITABLE document it ignores both. Setting the offsets moved nothing, and WrapDirection.Both
// never wrapped: a right-placed Figure left the whole left-hand side of the line empty. Those
// features work in a read-only FlowDocumentPageViewer, which is not what this app is.
//
// Floater is the part RichTextBox does implement properly: text genuinely wraps down the side of
// it. What Floater lacks is an offset, so placement is driven by Margin, and the SIDE is chosen
// from where the object is dropped rather than from a menu.
//
// ANCHORING: a Floater reserves its side of the column FROM ITS ANCHOR POINT DOWNWARD, and text
// from there on wraps beside it. That is the whole model, and it decides how placement works:
//   - Horizontal position = which side, plus a margin within that side.
//   - Vertical position = WHICH PARAGRAPH it is anchored in. There is no margin trick for this.
//     Parking every float in the note's first paragraph was tried and is wrong: the reserved
//     column then starts at the top of the note and squeezes everything above the object,
//     including tables it has nothing to do with.
// So the anchor is re-homed to the paragraph under the cursor when a drag is released. Adding a
// line ABOVE a float still moves it down - that is what "anchored to a paragraph" means, and it
// is how a word processor behaves.
//
// Everything used here (Floater, BlockUIContainer, InlineUIContainer) is a public framework type,
// so a floated object survives the XamlPackage round trip that saving a note performs.
namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        /// <summary>Gutter inside the float box that keeps wrapped text off the object. It is
        /// Padding, so it comes OUT of the Floater's Width - the width must be set to the object's
        /// size PLUS twice this, or the object is squeezed and its content clips.</summary>
        private const double FloatGutter = 6;

        /// <summary>Keeps a dragged object clear of the scrollbar and the note's right padding.</summary>
        private const double FloatEdgePad = 16;

        /// <summary>
        /// Horizontal snap: the note's width is divided into this many columns and a dragged object
        /// lands on one. Twelve because it divides cleanly by 2, 3, 4 and 6, so halves, thirds and
        /// quarters all land exactly - which is what makes two images on different lines line up
        /// with each other without any effort.
        ///
        /// There is no vertical grid, and that is deliberate: the text's own lines ARE the vertical
        /// grid. A float's rectangle can only begin at a paragraph, so snapping to paragraphs makes
        /// the object and the hole it carves the same rectangle, perfectly aligned. A pixel grid
        /// would just put the two slightly out of step.
        /// </summary>
        private const int FloatColumns = 12;

        // What the last right-click landed on. Captured on the button press because by the time the
        // context menu opens the mouse is over the menu, not over the document.
        private FrameworkElement? _ctxObject;

        // ---- drag-to-place state ----
        private FrameworkElement? _dragObject;
        private Floater? _dragFloater;
        private Point _dragStart;
        // Horizontal only - there is no vertical drag offset to remember, because vertical position
        // is the anchor paragraph rather than a coordinate.
        private double _dragFromX;

        // An inline object that has been pressed but not yet dragged far enough to float.
        private FrameworkElement? _armedObject;
        private Point _armedStart;

        // How many blocks the note had when the drag began. Dragging below the last paragraph
        // appends empty ones so the float has somewhere lower to anchor (GrowAnchorsTo); on release
        // any past this count that are still blank come back out. The floor is what guarantees
        // blank lines the user typed are never touched - only paragraphs a drag created can be
        // taken away again.
        private int _dragBlockFloor;

        // ---- volume knob state ----
        private Border? _dragDial;
        private double _dragFromVol;

        private void InitFloat()
        {
            // Playback volume is remembered app-wide, so a note opened tomorrow plays at the level
            // that was set today.
            if (double.TryParse(App.GetSetting("DictationVolume"),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double vol))
                DictationPlayer.Volume = vol;

            Editor.PreviewMouseLeftButtonDown += Editor_FloatDragPress;
            Editor.PreviewMouseMove += Editor_FloatDragMove;
            Editor.PreviewMouseLeftButtonUp += Editor_FloatDragRelease;
            // A drag that loses capture never reaches the release handler, so without this the
            // closed hand stays as the app-wide override and hovering never shows the open one
            // again. The editor drops capture on its own more than the bars do: the document
            // reflows under the gesture, and opening the SketchPad or a context menu takes it.
            //
            // CURSOR ONLY. This must NOT clear _dragFloater/_dragObject: the drag state is read
            // across the whole of Editor_FloatDragMove, and the document edits in that path drop
            // capture RE-ENTRANTLY, so clearing here nulls the floater between the guard at the
            // top and ReanchorFloater at the bottom of the same call. The button-state guard in
            // the move handler and the release handler already own that state.
            Editor.LostMouseCapture += (_, _) => DragCursors.EndDrag();
            Editor.PreviewMouseRightButtonDown += Editor_ObjectRightPress;
            Editor.ContextMenuOpening += Editor_ContextMenuOpening;
            Editor.ContextMenuClosing += Editor_ContextMenuClosing;
        }

        /// <summary>The floatable object under a point: a placed image, or a recording chip. Walks up
        /// from the hit element because the click usually lands on something inside the chip.</summary>
        private static FrameworkElement? FloatableAt(object? source)
        {
            DependencyObject? d = source as DependencyObject;
            while (d != null)
            {
                if (d is Image img) return img;
                if (d is Border b && b.Tag is int && b.Child is Grid) return b;   // recording chip
                if (d is RichTextBox) break;
                // Same visual/logical boundary problem the chip click walk has: a few steps up, the
                // walk leaves the visual tree into the FlowDocument's ContentElements, which
                // VisualTreeHelper refuses.
                d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        /// <summary>The volume knob under a point, or null. Recognized by its string Tag - the chip
        /// itself tags with an int.</summary>
        private static Border? DialAt(object? source)
        {
            DependencyObject? d = source as DependencyObject;
            while (d != null)
            {
                if (d is Border b && (b.Tag as string) == DialTag) return b;
                if (d is RichTextBox) break;
                d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        /// <summary>The Floater an object is floating in, or null when it is still inline.</summary>
        private static Floater? FloaterOf(FrameworkElement el)
            => LogicalTreeHelper.GetParent(el) is BlockUIContainer bc ? bc.Parent as Floater : null;

        /// <summary>Usable width of the note's text column.</summary>
        private double ColumnWidth()
        {
            double w = Editor.ViewportWidth > 0 ? Editor.ViewportWidth : Editor.ActualWidth;
            return w > 0 ? w : 600;
        }

        // ---- drag to place ----

        private void Editor_FloatDragPress(object sender, MouseButtonEventArgs e)
        {
            // The volume knob comes first: it sits inside the chip, so left to the float check below
            // a drag on it would move the whole recording instead of turning it down.
            if (DialAt(e.OriginalSource) is Border dial)
            {
                _dragDial = dial;
                _dragStart = e.GetPosition(Editor);
                _dragFromVol = DictationPlayer.Volume;
                Editor.CaptureMouse();
                e.Handled = true;
                return;
            }

            // Resize handles live on the editor's adorner layer and tunnel through here first. They
            // are a different gesture on the same object, and they get first refusal.
            if (e.OriginalSource is ImageResizeAdorner) return;

            var el = FloatableAt(e.OriginalSource);
            if (el == null) return;

            // On a recording chip, only the play glyph plays. The rest of the chip is the grab
            // handle - the chip was previously one big button, which left nowhere to take hold of it.
            if (el is Border chip && ReferenceEquals(e.OriginalSource, ChipParts(chip).glyph)) return;

            var fl = FloaterOf(el);
            if (fl == null)
            {
                // Still inline. Do NOT float it here - a plain click would then rearrange the note.
                // Arm it instead, and let the move handler float it once the pointer has actually
                // travelled. The event is deliberately left unhandled so a click still selects the
                // image and shows its resize handles as before.
                _armedObject = el;
                _armedStart = e.GetPosition(Editor);
                return;
            }

            // Double-click is the SketchPad gesture, not a drag. It has to be answered HERE: this
            // handler claims every press on a floated object, so Editor_ImagePress - which owns the
            // double-click for inline images - never runs once an image has been moved. That is
            // exactly why double-clicking stopped opening the pad after the first drag.
            if (e.ClickCount == 2 && el is Image dbl)
            {
                if (Sketch.TryGetData(dbl, out var payload))
                    OpenSketchPadForEdit(dbl, SketchModel.Deserialize(payload));
                else OpenSketchPadForEditImage(dbl);
                e.Handled = true;
                return;
            }

            // Select the image OURSELVES. This handler claims the click (e.Handled below) so the
            // drag can start, which stops Editor_ImagePress from ever running - and that is what
            // silently cost a floated image its resize handles. A click still selects; a drag still
            // moves; the handles, once shown, tunnel through this handler via the adorner check.
            if (el is Image floated) SelectImage(floated);

            _dragObject = el;
            _dragFloater = fl;
            _dragStart = e.GetPosition(Editor);
            _dragFromX = CurrentLeft(fl, el);
            _dragBlockFloor = Editor.Document.Blocks.Count;
            Editor.CaptureMouse();
            // CaptureMouse hands the cursor to the RichTextBox, whose own IBeam then wins for the
            // whole drag - the object's Cursor stops being consulted the moment capture moves.
            // An override outranks both and is cleared on release.
            DragCursors.BeginDrag();
            // Claim the click so the editor does not also move the caret or start a selection, and
            // so the image/recording handlers registered after this one stay out of it.
            e.Handled = true;
        }

        /// <summary>Where a floater currently sits, as a left edge, whichever side it is pinned to.
        /// Dragging has to work in one coordinate space regardless of alignment.</summary>
        private double CurrentLeft(Floater fl, FrameworkElement el)
        {
            double boxW = el.ActualWidth + FloatGutter * 2;
            return fl.HorizontalAlignment == HorizontalAlignment.Right
                ? ColumnWidth() - fl.Margin.Right - boxW - FloatEdgePad
                : fl.Margin.Left;
        }

        private void Editor_FloatDragMove(object sender, MouseEventArgs e)
        {
            if (_dragDial != null && e.LeftButton == MouseButtonState.Pressed)
            {
                // Up is louder. 80px of travel covers the full range - short enough to be a flick,
                // long enough to land on a specific level.
                double dy = _dragStart.Y - e.GetPosition(Editor).Y;
                DictationPlayer.Volume = _dragFromVol + dy / 80.0;
                UpdateDial(_dragDial);
                e.Handled = true;
                return;
            }

            // An armed inline object becomes a float the moment the pointer travels far enough to
            // count as a drag. This is why there is no "Float" menu item any more: dragging an
            // object is the gesture, and the user never has to know the word.
            if (_dragFloater == null && _armedObject != null && e.LeftButton == MouseButtonState.Pressed)
            {
                Point p = e.GetPosition(Editor);
                if (Math.Abs(p.X - _armedStart.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(p.Y - _armedStart.Y) >= SystemParameters.MinimumVerticalDragDistance)
                {
                    var el = _armedObject;
                    _armedObject = null;
                    // Counted BEFORE Float(), so a paragraph this drag goes on to append is never
                    // mistaken for one the note already had.
                    _dragBlockFloor = Editor.Document.Blocks.Count;
                    Float(el);
                    if (FloaterOf(el) is Floater made)
                    {
                        _dragObject = el;
                        _dragFloater = made;
                        _dragStart = p;
                        _dragFromX = CurrentLeft(made, el);
                        Editor.CaptureMouse();
                        // An inline object only becomes a float once the pointer has travelled, so
                        // this is where a first-ever drag actually begins - without the override the
                        // RichTextBox's IBeam owns the cursor for the whole of it.
                        DragCursors.BeginDrag();
                    }
                }
            }

            if (_dragFloater == null || _dragObject == null || e.LeftButton != MouseButtonState.Pressed) return;

            Point now = e.GetPosition(Editor);
            double x = _dragFromX + (now.X - _dragStart.X);

            double col = ColumnWidth();
            double boxW = _dragObject.ActualWidth + FloatGutter * 2;
            double room = Math.Max(0, col - boxW - FloatEdgePad);

            // Snap to the column grid, then clamp - in that order, or the snap can push the object
            // back past the edge it was just clamped to.
            double step = col / FloatColumns;
            if (step > 0) x = Math.Round(x / step) * step;
            x = Math.Max(0, Math.Min(x, room));
            if (double.IsNaN(x) || double.IsInfinity(x)) x = 0;

            // The side is picked from where the object actually is, not from a menu: past the middle
            // of the column it pins right and text wraps down its left, otherwise the reverse. This
            // is what makes "drag it over there" produce the wrap you expected.
            bool right = x + boxW / 2 > col / 2;
            _dragFloater.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;

            // Top margin stays ZERO. It does not offset a float, it EXTENDS the rectangle it
            // reserves - so a top margin carves a hole above the object as well as around it, which
            // is what squeezed the table when this was tried. Vertical placement is the anchor
            // paragraph, below, and nothing else.
            // Margins also clamp at zero because Floater is a Block, and Block.Margin validates
            // non-negative (unlike FrameworkElement.Margin, which takes negatives happily) - a
            // negative threw ArgumentException mid-drag.
            _dragFloater.Margin = right
                ? new Thickness(0, 0, Math.Max(0, col - x - boxW - FloatEdgePad), 0)
                : new Thickness(x, 0, 0, 0);

            // Re-anchor LIVE, not on drop: the object then follows the cursor down the page in
            // paragraph steps and the text reflows around it as you move, so what you see while
            // dragging is what you get when you let go. ReanchorFloater no-ops when the target
            // paragraph has not changed, so this is cheap.
            ReanchorFloater(_dragFloater, now);
            e.Handled = true;
        }

        private void Editor_FloatDragRelease(object sender, MouseButtonEventArgs e)
        {
            if (_dragDial != null)
            {
                Editor.ReleaseMouseCapture();
                _dragDial = null;
                // Volume is a playback preference, not note content - deliberately no MarkDirty.
                App.SetSetting("DictationVolume", DictationPlayer.Volume.ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture));
                e.Handled = true;
                return;
            }

            _armedObject = null;   // a press that never became a drag was just a click

            // Before the early return, not after it: releasing the button always ends the grab,
            // whether or not this particular press turned into a float. Left below the return, a
            // drag whose floater went away mid-gesture left the closed hand overriding the cursor
            // app-wide, and hovering never showed the open hand again.
            DragCursors.EndDrag();   // hand the cursor back to whatever is under it

            if (_dragFloater == null) return;
            Editor.ReleaseMouseCapture();

            // Dropping INTO a table cell is handled here and not in the live re-anchor, because it
            // ends the float: doing it mid-drag would tear the object out from under the gesture on
            // the first frame the cursor crossed the table.
            Point at = e.GetPosition(Editor);
            var cell = CellOf(Editor.GetPositionFromPoint(at, true)?.Paragraph);
            if (cell != null && _dragObject != null)
            {
                DropIntoCell(_dragFloater, _dragObject, cell);
                TrimGrownAnchors(null);   // it lives in the table now; no anchor to keep
            }
            else
            {
                ReanchorFloater(_dragFloater, at);
                // Keep the anchor the float ended on, drop the rest of what the drag appended.
                TrimGrownAnchors(_dragFloater.Parent as Paragraph);
            }

            _dragFloater = null;
            _dragObject = null;
            MarkDirty();
            e.Handled = true;
        }

        // ---- context menu ----

        private void Editor_ObjectRightPress(object sender, MouseButtonEventArgs e)
        {
            _ctxObject = FloatableAt(e.OriginalSource);
        }

        /// <summary>
        /// Rebuilds the menu for whatever was right-clicked. Right-clicking a recording used to
        /// offer "Convert to list", "Link" and the document toggles, none of which mean anything to
        /// an audio clip, so the menu is now split three ways: object items, text items, and
        /// document-level toggles.
        /// </summary>
        private void Editor_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            bool onObject = _ctxObject != null;
            // Whitespace does not count: "convert to list" on a selected space is a no-op that looks
            // like a broken command.
            bool hasText = !Editor.Selection.IsEmpty && !string.IsNullOrWhiteSpace(Editor.Selection.Text);

            Visibility txt = onObject ? Visibility.Collapsed : Visibility.Visible;

            // Sharing is offered on recordings only. An image is already shareable by copying it,
            // but a recording chip is a marker whose audio lives in the database - there is no other
            // way to get the file out.
            bool onRecording = _ctxObject is Border rb && rb.Tag is int && rb.Child is Grid;
            RecSeparator.Visibility = onRecording ? Visibility.Visible : Visibility.Collapsed;
            RecEditMenuItem.Visibility = onRecording ? Visibility.Visible : Visibility.Collapsed;
            RecShareMenuItem.Visibility = onRecording ? Visibility.Visible : Visibility.Collapsed;

            // Converting to a list needs words to convert.
            ConvertListMenuItem.Visibility = !onObject && hasText ? Visibility.Visible : Visibility.Collapsed;
            LinkMenuItem.Visibility = txt;

            DocToggleSeparator.Visibility = txt;
            SyntaxHighlightMenuItem.Visibility = txt;
            WordWrapMenuItem.Visibility = txt;
            SpellCheckMenuItem.Visibility = txt;
            if (onObject) PreviewMenuItem.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Puts the Preview item back after the menu closes. Its visibility is owned by Preview.cs
        /// AND doubles as the F4 gate in Shortcuts.cs, so hiding it for one object right-click must
        /// not outlive that menu - otherwise right-clicking an image would silently disable F4 for
        /// the rest of the session.
        /// </summary>
        private void Editor_ContextMenuClosing(object sender, ContextMenuEventArgs e)
        {
            PreviewMenuItem.Visibility = _currentId >= 0 && _docKind != DocKind.None
                ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Re-fits a float's reserved box after its object was resized. Without this the
        /// text carries on wrapping around the old footprint.</summary>
        private void RefloatWidth(FrameworkElement el)
        {
            if (FloaterOf(el) is not Floater fl) return;
            double w = el.Width > 0 && !double.IsNaN(el.Width) ? el.Width : el.ActualWidth;
            if (w <= 0 || double.IsNaN(w)) return;
            fl.Width = w + FloatGutter * 2;

            // The right-hand margin was computed against the old width, so a right-pinned float
            // would drift sideways as it is resized. Re-clamp it.
            if (fl.HorizontalAlignment == HorizontalAlignment.Right)
            {
                double room = Math.Max(0, ColumnWidth() - fl.Width - FloatEdgePad);
                fl.Margin = new Thickness(0, 0, Math.Min(fl.Margin.Right, room), 0);
            }
        }

        /// <summary>The table cell a paragraph sits in, or null.</summary>
        private static TableCell? CellOf(Paragraph? target)
        {
            for (DependencyObject? d = target; d != null && d is not FlowDocument; d = LogicalTreeHelper.GetParent(d))
                if (d is TableCell tc) return tc;
            return null;
        }

        /// <summary>
        /// Drops a floated object into a table cell: it stops floating and becomes the cell's
        /// content, and the column widens to fit it.
        ///
        /// Floating inside a cell is not an option - a Floater there is clipped to the cell and
        /// renders to nothing - but a cell is a perfectly good home for an image, and widening the
        /// column is what anyone dragging a picture onto a table actually wants.
        /// </summary>
        private void DropIntoCell(Floater fl, FrameworkElement el, TableCell cell)
        {
            if (LogicalTreeHelper.GetParent(el) is not BlockUIContainer bc) return;

            double w = el.Width > 0 && !double.IsNaN(el.Width) ? el.Width : el.ActualWidth;

            DeselectImage();
            (fl.Parent as Paragraph)?.Inlines.Remove(fl);
            bc.Child = null;

            // Reuse the cell's first paragraph so a caption already typed there is kept.
            if (cell.Blocks.FirstBlock is not Paragraph para)
            {
                para = new Paragraph();
                cell.Blocks.Add(para);
            }
            para.Inlines.Add(new InlineUIContainer(el));
            el.Cursor = null;   // no longer draggable; it is cell content now

            FitColumnTo(cell, w);
        }

        /// <summary>Widens the column a cell belongs to so an object of the given width fits.</summary>
        private static void FitColumnTo(TableCell cell, double objectWidth)
        {
            if (objectWidth <= 0 || double.IsNaN(objectWidth)) return;
            if (cell.Parent is not TableRow row) return;
            if (row.Parent is not TableRowGroup group || group.Parent is not Table table) return;

            // A cell knows its row but not its column number, so count across, respecting spans.
            int index = 0;
            foreach (TableCell c in row.Cells)
            {
                if (ReferenceEquals(c, cell)) break;
                index += c.ColumnSpan;
            }

            // A table built by the editor may carry no TableColumn entries at all (all-automatic
            // widths). Nothing can be pinned until they exist, and the ones added ahead of the
            // target stay Auto, so the other columns keep sizing themselves.
            while (table.Columns.Count <= index) table.Columns.Add(new TableColumn());

            var col = table.Columns[index];
            double want = objectWidth + CellPad;
            double have = col.Width.IsAbsolute ? col.Width.Value : 0;
            // Only ever widen: shrinking a column the user has already sized is not our business.
            if (want > have) col.Width = new GridLength(want, GridUnitType.Pixel);
        }

        /// <summary>Breathing room either side of an image sitting in a table cell.</summary>
        private const double CellPad = 12;

        /// <summary>
        /// Turns the paragraph under the cursor into a paragraph a float can legally live in.
        ///
        /// A Floater inside a TableCell is CLIPPED TO THAT CELL, so it renders to nothing - dragging
        /// an image over the table at the top of a note made it disappear outright. Rather than
        /// refuse the drop, anchor to the nearest paragraph BEFORE the table, which is where the
        /// object visually was anyway.
        ///
        /// List items are left alone: a ListItem is a block container, not a clipping one, and a
        /// float beside a bullet list works - which is worth keeping, since notes are mostly lists.
        /// </summary>
        private Paragraph? ResolveAnchor(Paragraph? target)
        {
            if (target == null) return null;

            bool inCell = false;
            DependencyObject? d = target;
            while (d != null && d is not FlowDocument)
            {
                if (d is TableCell) { inCell = true; break; }
                d = LogicalTreeHelper.GetParent(d);
            }
            if (!inCell) return target;

            // Walk out to whichever top-level block the cell belongs to, then back up the document
            // for somewhere legal to sit.
            Block? top = null;
            for (DependencyObject? b = target; b != null; b = LogicalTreeHelper.GetParent(b))
                if (b is Block blk && Editor.Document.Blocks.Contains(blk)) { top = blk; break; }
            if (top == null) return null;

            for (Block? prev = top.PreviousBlock; prev != null; prev = prev.PreviousBlock)
                if (prev is Paragraph p) return p;
            return null;   // table is the first thing in the note; leave the anchor where it is
        }

        /// <summary>
        /// Makes sure there is a paragraph as far down as the cursor, by appending empty ones past
        /// the end of the note.
        ///
        /// Vertical position IS the anchor paragraph, so an object can only be dragged as far down
        /// as the text goes. In a note that is empty apart from the object there is exactly ONE
        /// paragraph, ReanchorFloater finds that same paragraph however far down you drag, and the
        /// object never moves - which reads as a broken drag rather than as a rule about anchors.
        /// Growing the document is what a person does by hand when they press Enter a few times to
        /// push a picture down the page; doing it for them is what makes the drag feel free.
        ///
        /// Whatever the drop does not end up needing is removed again on release (TrimGrownAnchors),
        /// so this never leaves blank lines lying around.
        /// </summary>
        private void GrowAnchorsTo(Point p)
        {
            var end = Editor.Document.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            if (end.IsEmpty || p.Y <= end.Bottom) return;

            // One empty line's height drives how many are needed, computed in ONE go. There is
            // deliberately no UpdateLayout() here - see the FailFast note on the release handler.
            double lineH = end.Height > 1 ? end.Height : 16;
            int need = (int)Math.Floor((p.Y - end.Bottom) / lineH);
            if (need <= 0) return;

            // Bounded. A fast drag past the bottom edge, or a bad rect, must not be able to append
            // thousands of paragraphs.
            need = Math.Min(need, 120);

            // BeginChange/EndChange, always. Adding blocks straight onto Document.Blocks edits the
            // TextContainer out from under the RichTextBox's TSF text store; the store then asks
            // for a character offset that no longer maps to a node and WPF answers with
            // Environment.FailFast("Unrecoverable system error") - a hard kill, no exception to
            // catch, exit code 0. A change block is what makes the edit atomic to that layer.
            Editor.BeginChange();
            try
            {
                for (int i = 0; i < need; i++) Editor.Document.Blocks.Add(new Paragraph());
            }
            finally { Editor.EndChange(); }
        }

        /// <summary>
        /// Takes back the empty paragraphs a downward drag added but the drop did not need.
        ///
        /// Only ever removes blocks ADDED DURING THIS DRAG - _dragBlockFloor is the count from
        /// before it started, so blank lines already in the note are the user's and are left alone.
        /// The float's own anchor is never removed, which is what holds it at its new depth.
        /// </summary>
        private void TrimGrownAnchors(Paragraph? anchor)
        {
            Editor.BeginChange();   // same TextContainer/TSF rule as GrowAnchorsTo
            try
            {
                while (Editor.Document.Blocks.Count > _dragBlockFloor
                       && Editor.Document.Blocks.LastBlock is Paragraph last
                       && last.Inlines.Count == 0
                       && !ReferenceEquals(last, anchor))
                    Editor.Document.Blocks.Remove(last);
            }
            finally { Editor.EndChange(); }
        }

        /// <summary>
        /// Moves a float's anchor to the paragraph under the cursor. This IS the vertical placement
        /// mechanism - a Floater reserves its column from wherever it is anchored, so moving it down
        /// the page means moving it to a later paragraph, not adding a top margin.
        /// </summary>
        private void ReanchorFloater(Floater? fl, Point p)
        {
            try
            {
                // Nullable on purpose. Both callers read _dragFloater after their own guard, and
                // this method runs on every mouse move through code that mutates the document -
                // so anything that clears the drag state re-entrantly lands here as a null. The
                // catch below would swallow the resulting NullReferenceException and quietly stop
                // re-anchoring for the rest of the gesture; this makes it an honest no-op instead.
                if (fl is null) return;
                if (fl.Parent is not Paragraph from) return;
                // Give the drag somewhere lower to land before asking what is under the cursor.
                GrowAnchorsTo(p);
                // snapToText so a drop in the empty space beside a line still finds that line.
                var target = ResolveAnchor(Editor.GetPositionFromPoint(p, true)?.Paragraph);
                if (target == null || ReferenceEquals(target, from)) return;
                // A float dropped onto its own content would be re-parented into itself.
                for (DependencyObject? d = target; d != null; d = LogicalTreeHelper.GetParent(d))
                    if (ReferenceEquals(d, fl)) return;

                // Atomic to the TSF text store, same rule as GrowAnchorsTo: re-parenting the
                // Floater is a TextContainer edit, and this one runs on EVERY mouse move.
                Editor.BeginChange();
                try
                {
                    from.Inlines.Remove(fl);
                    target.Inlines.Add(fl);
                }
                finally { Editor.EndChange(); }
            }
            catch { /* odd drop target (inside a table cell being edited); leave the anchor alone */ }
        }

        /// <summary>
        /// Re-applies the grab hand to every object that is ALREADY floated in a loaded note.
        ///
        /// Float() is the only place that sets the cursor, and it runs when an object is lifted
        /// out of the text - never again. An object floated in an earlier session comes back as a
        /// Floater straight out of the XamlPackage without passing through Float(), so nothing
        /// gives it a cursor. That went unnoticed while the cursor was Cursors.SizeAll, because
        /// "SizeAll" is a named value with a type converter and so survived the save as a string;
        /// a cursor loaded from a .cur stream has no such representation and does not, which is
        /// why the hand stopped appearing on images the moment the art replaced SizeAll.
        /// </summary>
        private static void ApplyFloatCursors(FlowDocument doc)
        {
            foreach (var fl in Floaters(doc))
                if (fl.Blocks.FirstBlock is BlockUIContainer { Child: FrameworkElement el })
                    el.Cursor = DragCursors.Open;
        }

        /// <summary>Every Floater in the document, at any nesting depth.</summary>
        private static IEnumerable<Floater> Floaters(FlowDocument doc)
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(doc);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                if (d is Floater f) yield return f;
                foreach (object child in LogicalTreeHelper.GetChildren(d))
                    if (child is DependencyObject dc) stack.Push(dc);
            }
        }

        /// <summary>Lifts an inline object out of the text flow into a draggable Floater.</summary>
        private void Float(FrameworkElement el)
        {
            if (LogicalTreeHelper.GetParent(el) is not InlineUIContainer iuc) return;
            if (iuc.Parent is not Paragraph para) return;

            // Measured BEFORE detaching: once the element is out of the tree ActualWidth reads 0,
            // and a Floater with no explicit width takes the whole column, which nothing can wrap
            // beside.
            double w = el.Width > 0 ? el.Width : el.ActualWidth;
            if (double.IsNaN(w) || w <= 0) w = 200;

            DeselectImage();   // resize handles adorn the old position and must not outlive the move

            // One change block around the whole lift. Removing the InlineUIContainer and adding the
            // Floater are two TextContainer edits; left unbatched the text store can observe the
            // document mid-swap, with the object parented to nothing, and FailFast on the offset.
            Editor.BeginChange();
            try
            {
            para.Inlines.Remove(iuc);
            iuc.Child = null;  // an element can only have one parent; detach before re-parenting

            var fl = new Floater(new BlockUIContainer(el))
            {
                // Object size PLUS the gutter on both sides - Padding comes out of Width, and
                // setting Width to the bare object size is what clipped the chip's duration.
                Width = w + FloatGutter * 2,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0),
                Padding = new Thickness(FloatGutter),
                BorderThickness = new Thickness(0),
            };

            // Anchored in the paragraph it already lived in, so the object stays where it was and
            // only the text around it reflows. Dragging re-homes it from there.
            para.Inlines.Add(fl);
            }
            finally { Editor.EndChange(); }
            el.Cursor = DragCursors.Open;   // say it is draggable
        }

        /// <summary>Puts a floated object back into the text flow, inline, at the caret.</summary>
        private void Unfloat(FrameworkElement el)
        {
            var fl = FloaterOf(el);
            if (fl?.Parent is not Paragraph para) return;
            if (LogicalTreeHelper.GetParent(el) is not BlockUIContainer bc) return;

            DeselectImage();

            para.Inlines.Remove(fl);
            bc.Child = null;

            // Back in at the start of the paragraph it was anchored to, which is where it visually
            // sat - not at the caret, which is wherever the user last typed.
            new InlineUIContainer(el, para.ContentStart);
            el.Cursor = null;   // back to the document's own cursor
        }
    }
}
