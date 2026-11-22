using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using KillerFamily.Controls;
using KillerNotes.Models;

namespace KillerNotes.Controls.Sketch
{
    internal sealed partial class SketchPadWindow
    {
        // ---- Drawing surface ----

        private Point Clamp(Point p) => new(
            Math.Max(0, Math.Min(_canvasW, p.X)),
            Math.Max(0, Math.Min(_canvasH, p.Y)));

        private void CanvasDown(object sender, MouseButtonEventArgs e)
        {
            if (_textBox != null) { CommitTextEntry(); e.Handled = true; return; }   // a click off the editor sets the label
            _canvas.Focus();
            if (_arcTarget != null) { _arcTarget = null; _canvas.ReleaseMouseCapture(); RenderCanvas(); e.Handled = true; return; }   // click solidifies the arc
            _wheelObj = null;   // a click ends the current opacity-scroll run (next scroll = fresh undo)
            var p = Clamp(e.GetPosition(_canvas));
            if (_tool == Tool.Text) { BeginTextEntry(p, null); e.Handled = true; return; }   // place a new label
            if (_tool == Tool.Bucket) { BucketFill(p); e.Handled = true; return; }   // single click, no drag
            if (_tool == Tool.Polygon)
            {
                if (e.ClickCount == 2 && _polyPts.Count >= 3) CommitPoly();
                else PolyClick(p);
                e.Handled = true;
                return;
            }
            if (_tool == Tool.Select)
            {
                if (e.ClickCount == 2 && HitPick(p) is { Kind: SketchKind.Text } tx) { BeginTextEntry(new Point(tx.X, tx.Y), tx); e.Handled = true; return; }   // double-click a label to re-edit it
                SelectDown(p); e.Handled = true; return;
            }

            _dragging = true;
            _start = p;
            _canvas.CaptureMouse();

            if (_tool == Tool.Eraser)
            {
                // Snapshot now, push only once something is actually erased: a click on empty
                // canvas must not add a no-op undo step or throw away the redo stack.
                _eraseSnapshot = SketchModel.CloneList(_objects);
                EraseStep(p);
            }
            else if (_tool == Tool.Pen)
            {
                _gesturePts = [p.X, p.Y];
                UpdatePreview(p);
            }
            else
            {
                UpdatePreview(p);
            }
            e.Handled = true;
        }

        private void CanvasMove(object sender, MouseEventArgs e)
        {
            var p = Clamp(e.GetPosition(_canvas));
            if (_tool == Tool.Eraser) ShowEraseCursor(p);   // ring tracks the pointer so the erase area is visible
            if (_arcTarget != null) { _sel = _arcTarget; CurveMove(p); return; }   // live bend follows the mouse
            if (_resizing) { ResizeMove(p); return; }
            if (_curving) { CurveMove(p); return; }
            if (_tool == Tool.Polygon) { if (PolyActive) PolyRubber(p); return; }
            if (!_dragging) return;
            if (_tool == Tool.Select) { SelectMove(p); return; }
            if (_tool == Tool.Eraser) { EraseStep(p); ShowEraseCursor(p); return; }
            if (_tool == Tool.Pen) { _gesturePts.Add(p.X); _gesturePts.Add(p.Y); }
            UpdatePreview(p);
        }

        private void CanvasUp(object sender, MouseButtonEventArgs e)
        {
            if (_resizing) { _resizing = false; _canvas.ReleaseMouseCapture(); return; }
            if (_curving) { _curving = false; _canvas.ReleaseMouseCapture(); return; }
            if (!_dragging) return;
            _dragging = false;
            _canvas.ReleaseMouseCapture();
            DragCursors.EndDrag();   // no-op unless the fist is ours (Select drag)
            var p = Clamp(e.GetPosition(_canvas));
            RemovePreview();
            if (_tool != Tool.Eraser && _tool != Tool.Select) CommitGesture(p);
        }

        // The capture went to something else mid-gesture, so no button-up will arrive. Finish the
        // gesture as a release at the pointer would (CanvasUp clears each flag before it lets go of
        // the capture, so this is a no-op on a normal release).
        private void EndGesture()
        {
            _resizing = false;
            _curving = false;
            if (_dragging)
            {
                _dragging = false;
                var p = Clamp(Mouse.GetPosition(_canvas));
                RemovePreview();
                if (_tool != Tool.Eraser && _tool != Tool.Select) CommitGesture(p);
            }
            DragCursors.EndDrag();
        }

        // ---- Select / move ----

        private void SelectDown(Point p)
        {
            _sel = HitPick(p);
            _movePushed = false;
            _lastMove = p;
            _dragging = _sel != null;
            if (_dragging)
            {
                _canvas.CaptureMouse();
                // Fist only once something is actually held. SetTool leaves Select on the plain
                // arrow deliberately: an open hand on hover would sit over the artwork you are
                // trying to click precisely, which is the opposite of what a select tool needs.
                DragCursors.BeginDrag();
            }
            RenderCanvas();
        }

        private void SelectMove(Point p)
        {
            if (_sel == null) return;
            if (!_movePushed) { PushUndo(); _movePushed = true; }
            Translate(_sel, p.X - _lastMove.X, p.Y - _lastMove.Y);
            _lastMove = p;
            RenderCanvas();
        }

        private void DeleteSelected()
        {
            if (_sel == null) return;
            PushUndo();
            _objects.Remove(_sel);
            _sel = null;
            RenderCanvas();
        }

        // Topmost object (backdrop excluded) whose geometry is under the click.
        private SketchObject? HitPick(Point p)
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                var o = _objects[i];
                if (o.Kind == SketchKind.Image && o.Backdrop) continue;
                if (SketchModel.HitTest(o, p, 4)) return o;
            }
            return null;
        }

        private static void Translate(SketchObject o, double dx, double dy)
        {
            if (o.Kind is SketchKind.Text or SketchKind.Image)
            {
                if (o.Backdrop) return;
                o.X += dx; o.Y += dy;
            }
            else
            {
                for (int i = 0; i + 1 < o.Pts.Count; i += 2) { o.Pts[i] += dx; o.Pts[i + 1] += dy; }
            }
        }

        // ---- Resize (corner handles on the selected object) ----

        private void ResizeStart(int handle)
        {
            if (_sel == null) return;
            _resizeOrig = _sel.Clone();
            _resizeOrigBounds = SketchModel.BoundsOf(_sel);
            var b = _resizeOrigBounds;
            _resizeAnchor = handle switch
            {
                0 => new Point(b.Right, b.Bottom),   // dragging TL, anchor BR
                1 => new Point(b.Left, b.Bottom),    // dragging TR, anchor BL
                2 => new Point(b.Left, b.Top),       // dragging BR, anchor TL
                _ => new Point(b.Right, b.Top),      // dragging BL, anchor TR
            };
            _resizeHandle = handle;
            _resizing = true;
            PushUndo();
            _canvas.CaptureMouse();
        }

        private void ResizeMove(Point p)
        {
            if (_resizeOrig == null || _sel == null) return;
            var b = _resizeOrigBounds;
            double ow = Math.Max(1, b.Width), oh = Math.Max(1, b.Height);
            double sx, sy;
            if (_sel.Kind == SketchKind.Image)
            {
                // Proportional: project the cursor onto the original diagonal so images never distort.
                var corner = _resizeHandle switch
                {
                    0 => new Point(b.Left, b.Top),
                    1 => new Point(b.Right, b.Top),
                    2 => new Point(b.Right, b.Bottom),
                    _ => new Point(b.Left, b.Bottom),
                };
                var d0 = corner - _resizeAnchor;
                var d1 = p - _resizeAnchor;
                double denom = Math.Max(1, d0.X * d0.X + d0.Y * d0.Y);
                double s = (d1.X * d0.X + d1.Y * d0.Y) / denom;
                s = Math.Max(8.0 / Math.Min(ow, oh), s);
                sx = sy = s;
            }
            else
            {
                sx = Math.Max(8.0 / ow, Math.Abs(p.X - _resizeAnchor.X) / ow);
                sy = Math.Max(8.0 / oh, Math.Abs(p.Y - _resizeAnchor.Y) / oh);
            }
            ScaleSelFromOrig(_resizeAnchor, sx, sy);
            RenderCanvas();
        }

        private void ScaleSelFromOrig(Point anchor, double sx, double sy)
        {
            var o = _sel!;
            var g = _resizeOrig!;
            if (o.Kind is SketchKind.Text or SketchKind.Image)
            {
                if (o.Backdrop) return;
                o.X = anchor.X + (g.X - anchor.X) * sx;
                o.Y = anchor.Y + (g.Y - anchor.Y) * sy;
                if (o.Kind == SketchKind.Image) { o.W = g.W * sx; o.H = g.H * sy; }
                else o.FontSize = Math.Max(4, g.FontSize * sy);
            }
            else
            {
                o.Pts.Clear();
                for (int i = 0; i + 1 < g.Pts.Count; i += 2)
                {
                    o.Pts.Add(anchor.X + (g.Pts[i] - anchor.X) * sx);
                    o.Pts.Add(anchor.Y + (g.Pts[i + 1] - anchor.Y) * sy);
                }
            }
        }

        // ---- Images (Add-image button + drag-and-drop) ----

        private void AddImageFromFile()
        {
            // The themed family picker, not Microsoft.Win32.OpenFileDialog - the Win32 one cannot be
            // themed at all and opened as a stock Explorer window in the middle of the app. Same
            // dialog and same filter the editor's Insert image button uses (Editor.Insert.cs).
            var dlg = new Controls.FileDialog(Controls.FileDialogMode.Open)
            {
                Title = L("Str_TT_Image", "Insert image"),
                Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff",
                CheckFileExists = true,
                ShowPreview = true,
            };
            if (dlg.ShowDialog(this) != true) return;
            var src = LoadBitmap(dlg.FileName);
            if (src != null) AddImage(src, null);
        }

        // Place an image as a movable object, scaled to fit (never upscaled) and centered on the drop
        // point (or the canvas), then select it so it can be dragged into position right away.
        private void AddImage(BitmapSource src, Point? at)
        {
            string? b64 = ToPngB64(src);
            if (b64 == null) return;
            double maxW = _canvasW * 0.7, maxH = _canvasH * 0.7;
            double iw = Math.Max(1, src.PixelWidth), ih = Math.Max(1, src.PixelHeight);
            double scale = Math.Min(Math.Min(maxW / iw, maxH / ih), 1.0);
            double w = iw * scale, h = ih * scale;
            double cx = at?.X ?? _canvasW / 2.0, cy = at?.Y ?? _canvasH / 2.0;
            double x = Math.Max(0, Math.Min(_canvasW - w, cx - w / 2));
            double y = Math.Max(0, Math.Min(_canvasH - h, cy - h / 2));
            var o = new SketchObject { Kind = SketchKind.Image, Img = b64, X = x, Y = y, W = w, H = h };
            PushUndo();
            _objects.Add(o);
            _sel = o;
            SetTool(Tool.Select);
            RenderCanvas();
        }

        private static BitmapSource? LoadBitmap(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        private static string? ToPngB64(BitmapSource src)
        {
            try
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(src));
                using var ms = new MemoryStream();
                enc.Save(ms);
                return Convert.ToBase64String(ms.ToArray());
            }
            catch { return null; }
        }

        private static bool HasImageData(IDataObject d)
        {
            if (d.GetDataPresent(DataFormats.Bitmap)) return true;
            if (d.GetData(DataFormats.FileDrop) is string[] files)
                foreach (var f in files)
                    if (Array.IndexOf(ImgExts, System.IO.Path.GetExtension(f).ToLowerInvariant()) >= 0) return true;
            return false;
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = HasImageData(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            var at = Clamp(e.GetPosition(_canvas));
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                // Import every dropped image (a multi-select drag brings them all), cascaded so they
                // don't land in a single stack.
                double off = 0;
                foreach (var f in files)
                    if (Array.IndexOf(ImgExts, System.IO.Path.GetExtension(f).ToLowerInvariant()) >= 0)
                    {
                        var src = LoadBitmap(f);
                        if (src != null) { AddImage(src, new Point(at.X + off, at.Y + off)); off += 18; }
                    }
            }
            else if (e.Data.GetData(DataFormats.Bitmap) is BitmapSource bs)
            {
                AddImage(bs, at);
            }
            e.Handled = true;
        }

        // ---- Scroll-to-opacity + right-click menu ----

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (_textBox != null) return;   // ignore the wheel while a label is being typed
            var img = ImageUnder(Clamp(e.GetPosition(_canvas)));
            if (img == null) return;   // not over a placed image - leave the wheel alone
            if (!ReferenceEquals(_wheelObj, img)) { PushUndo(); _wheelObj = img; }
            double cur = img.Opacity <= 0 ? 1.0 : img.Opacity;
            cur += e.Delta > 0 ? 0.08 : -0.08;
            img.Opacity = Math.Max(0.1, Math.Min(1.0, cur));
            RenderCanvas();
            e.Handled = true;
        }

        private SketchObject? ImageUnder(Point p)
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                var o = _objects[i];
                if (o.Kind == SketchKind.Image && !o.Backdrop && SketchModel.BoundsOf(o).Contains(p)) return o;
            }
            return null;
        }

        // Right-click: a card that suits what's under the cursor - an object (reorder / duplicate,
        // plus what that kind of object can do, with Delete below) or empty canvas (add / paste
        // image, with Clear all below).
        private void OnCanvasContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            bool keyboard = e.CursorLeft < 0;
            if (_textBox != null)
            {
                if (!keyboard) { CommitTextEntry(); e.Handled = true; }
                return;
            }
            var hit = keyboard ? _sel : HitPick(Clamp(Mouse.GetPosition(_canvas)));
            if (hit != null && _tool == Tool.Select) { _sel = hit; RenderCanvas(); }

            var menu = BuildCanvasCard(hit, hit == null && ClipboardHasImage());
            _canvas.ContextMenu = menu;
            Point at = hit != null ? SketchModel.BoundsOf(hit).BottomLeft
                : new Point((_zoomHost.HorizontalOffset + 8) / _zoom, (_zoomHost.VerticalOffset + 8) / _zoom);
            CardBuilder.Open(menu, _canvas, keyboard, new Rect(at, new Size(0, 0)));
            e.Handled = true;
        }

        /// <summary>
        /// The right-click card, on the family card rules (Shell/NoteCards.cs). On an object it is a
        /// card for one thing: its kind in the header with Delete as the header's icon button, then
        /// what that kind can do first and Front, Back and Duplicate after, every row full. On empty
        /// canvas it is one row: Add image, Paste image and Clear all, the destructive one last.
        /// Every tile keeps the shortcut the canvas binds for it (SketchBind in the constructor and
        /// Widgets). canPaste is the clipboard answer, asked by the caller so a test never has to
        /// touch the real clipboard.
        /// </summary>
        internal ContextMenu BuildCanvasCard(SketchObject? hit, bool canPaste)
        {
            var menu = KillerNotes.Shell.NoteCards.Menu();
            static CardTile Tile(string id, string glyph, string caption, string tip, string keys, Action run, bool enabled = true) =>
                new(glyph, caption, keys, run, false, enabled) { Id = id, Hint = CardBuilder.Tip(tip, keys) };
            if (hit != null)
            {
                menu.Items.Add(KillerNotes.Shell.NoteCards.Header(menu, KindName(hit.Kind), null,
                    L("Str_Sketch_Delete", "Delete"), "Delete", () => RemoveObject(hit)));
                var tiles = new List<CardTile>();
                if (hit.Kind is SketchKind.Line or SketchKind.Arrow)
                {
                    // A line that has already been arced (more than its two end points) offers
                    // to straighten; a straight one offers to arc. Never both, like the keys.
                    if (hit.Pts.Count >= 6)
                        tiles.Add(Tile("straighten", "\uE738", L("Str_Sketch_Straighten", "Straighten line"), L("Str_Sketch_Straighten", "Straighten line"), "Ctrl+Shift+L", () => Straighten(hit)));
                    else
                        tiles.Add(Tile("arc", "\uE7AD", L("Str_Sketch_Arc", "Arc line"), L("Str_Sketch_Arc", "Arc line"), "Ctrl+Shift+A", () => StartArc(hit)));
                }
                if (hit.Kind == SketchKind.Image)
                    tiles.Add(Tile("resetopacity", "\uE72C", L("Str_Sketch_ResetOpacity", "Reset opacity"), L("Str_Sketch_ResetOpacity", "Reset opacity"), "Ctrl+Shift+O",
                        () => { PushUndo(); hit.Opacity = 1; _wheelObj = null; RenderCanvas(); }));
                if (hit.Kind == SketchKind.Text)
                {
                    tiles.Add(Tile("edittext", "\uE70F", L("Str_Sketch_EditText", "Edit text"), L("Str_Sketch_EditText", "Edit text"), "F10",
                        () => BeginTextEntry(new Point(hit.X, hit.Y), hit)));
                    // Bold is an on/off tile, filled while the text is bold.
                    tiles.Add(Tile("bold", "\uE8DD", L("Str_Sketch_Bold", "Bold"),
                        hit.Bold ? L("Str_Sketch_Unbold", "Remove bold") : L("Str_Sketch_Bold", "Bold"), "Ctrl+B",
                        () => { PushUndo(); hit.Bold = !hit.Bold; RenderCanvas(); }) with { Checked = hit.Bold });
                }
                tiles.Add(Tile("front", "\uE74A", L("Str_SketchCard_Front", "Front"), L("Str_Sketch_Front", "Bring to front"), "Ctrl+Shift+Up", () => Reorder(hit, true)));
                tiles.Add(Tile("back", "\uE74B", L("Str_SketchCard_Back", "Back"), L("Str_Sketch_Back", "Send to back"), "Ctrl+Shift+Down", () => Reorder(hit, false)));
                tiles.Add(Tile("duplicate", "\uE8C8", L("Str_Sketch_Duplicate", "Duplicate"), L("Str_Sketch_Duplicate", "Duplicate"), "Ctrl+D", () => Duplicate(hit)));
                menu.Items.Add(KillerNotes.Shell.NoteCards.Grid(menu, tiles));
            }
            else
            {
                menu.Items.Add(KillerNotes.Shell.NoteCards.Header(menu, L("Str_Sketch_Title", "SketchPad")));
                // Paste stays in its place when the clipboard holds no image, dimmed, and so does
                // Clear all on an empty canvas, so the card never changes shape.
                menu.Items.Add(KillerNotes.Shell.NoteCards.Grid(menu, new[]
                {
                    Tile("addimage", "\uE91B", L("Str_Sketch_AddImageMenu", "Add image..."), L("Str_Sketch_AddImageMenu", "Add image..."), "I", AddImageFromFile),
                    Tile("pasteimage", "\uE77F", L("Str_Sketch_PasteImage", "Paste image"), L("Str_Sketch_PasteImage", "Paste image"), "Ctrl+V", PasteImage, canPaste),
                    Tile("clearall", "\uE894", L("Str_Sketch_Clear", "Clear all"), L("Str_Sketch_Clear", "Clear all"), "F8", ClearAll, _objects.Count > 0),
                }));
            }
            return menu;
        }

        /// <summary>The card's title: what kind of thing was right-clicked. Rectangles, ellipses
        /// and polygons share one name; the menu does the same for all three.</summary>
        private static string KindName(SketchKind kind) => kind switch
        {
            SketchKind.Freehand => L("Str_SketchCard_Drawing", "Drawing"),
            SketchKind.Line => L("Str_SketchCard_Line", "Line"),
            SketchKind.Arrow => L("Str_SketchCard_Arrow", "Arrow"),
            SketchKind.Text => L("Str_SketchCard_Text", "Text"),
            SketchKind.Image => L("Str_SketchCard_Image", "Image"),
            _ => L("Str_Ctx_GraphShape", "Shape"),
        };


        private void RemoveObject(SketchObject o)
        {
            if (!_objects.Contains(o)) return;
            PushUndo();
            _objects.Remove(o);
            if (ReferenceEquals(_sel, o)) _sel = null;
            RenderCanvas();
        }

        private void Reorder(SketchObject o, bool toFront)
        {
            int i = _objects.IndexOf(o);
            if (i < 0) return;
            PushUndo();
            _objects.RemoveAt(i);
            if (toFront) _objects.Add(o); else _objects.Insert(0, o);
            RenderCanvas();
        }

        private void Duplicate(SketchObject o)
        {
            PushUndo();
            var c = o.Clone();
            Translate(c, 14, 14);
            _objects.Add(c);
            _sel = c;
            SetTool(Tool.Select);
            RenderCanvas();
        }

        // Another program holding the clipboard open makes WPF throw rather than answer; to the
        // pad that is the same as there being no image to paste.
        private static bool ClipboardHasImage()
        {
            try { return Clipboard.ContainsImage(); }
            catch { return false; }
        }

        private void PasteImage()
        {
            BitmapSource? src = null;
            try { if (Clipboard.ContainsImage()) src = Clipboard.GetImage(); }
            catch { /* clipboard busy - nothing to paste */ }
            if (src != null) AddImage(src, null);
        }
    }
}
