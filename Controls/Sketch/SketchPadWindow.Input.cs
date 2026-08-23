using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using KillerNotes.Models;

namespace KillerNotes.Controls
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
                PushUndo();
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
            var dlg = new KillerPDF.Controls.FileDialog(KillerPDF.Controls.FileDialogMode.Open)
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

        // Right-click: a menu whose items suit what's under the cursor - an object (delete / reorder /
        // duplicate, plus opacity reset for images) or empty canvas (add / paste image, clear).
        private void OnCanvasRightDown(object sender, MouseButtonEventArgs e)
        {
            if (_textBox != null) { CommitTextEntry(); e.Handled = true; return; }   // a right-click also sets an open label
            var hit = HitPick(Clamp(e.GetPosition(_canvas)));
            if (hit != null && _tool == Tool.Select) { _sel = hit; RenderCanvas(); }

            var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
            if (hit != null)
            {
                menu.Items.Add(Mi(L("Str_Sketch_Delete", "Delete"), 0xE74D, () => RemoveObject(hit)));
                menu.Items.Add(Mi(L("Str_Sketch_Front", "Bring to front"), 0, () => Reorder(hit, true)));
                menu.Items.Add(Mi(L("Str_Sketch_Back", "Send to back"), 0, () => Reorder(hit, false)));
                menu.Items.Add(Mi(L("Str_Sketch_Duplicate", "Duplicate"), 0xE8C8, () => Duplicate(hit)));
                if (hit.Kind is SketchKind.Line or SketchKind.Arrow)
                {
                    menu.Items.Add(Sep());
                    if (hit.Pts.Count >= 6) menu.Items.Add(Mi(L("Str_Sketch_Straighten", "Straighten line"), 0, () => Straighten(hit)));
                    else menu.Items.Add(Mi(L("Str_Sketch_Arc", "Arc line"), 0, () => StartArc(hit)));
                }
                if (hit.Kind == SketchKind.Image)
                {
                    menu.Items.Add(Sep());
                    menu.Items.Add(Mi(L("Str_Sketch_ResetOpacity", "Reset opacity"), 0, () => { PushUndo(); hit.Opacity = 1; _wheelObj = null; RenderCanvas(); }));
                }
                if (hit.Kind == SketchKind.Text)
                {
                    menu.Items.Add(Sep());
                    menu.Items.Add(Mi(L("Str_Sketch_EditText", "Edit text"), 0xE70F, () => BeginTextEntry(new Point(hit.X, hit.Y), hit)));
                    menu.Items.Add(Mi(hit.Bold ? L("Str_Sketch_Unbold", "Remove bold") : L("Str_Sketch_Bold", "Bold"), 0, () => { PushUndo(); hit.Bold = !hit.Bold; RenderCanvas(); }));
                }
            }
            else
            {
                menu.Items.Add(Mi(L("Str_Sketch_AddImageMenu", "Add image..."), 0xE91B, AddImageFromFile));
                if (Clipboard.ContainsImage()) menu.Items.Add(Mi(L("Str_Sketch_PasteImage", "Paste image"), 0xE77F, PasteImage));
                menu.Items.Add(Sep());
                var clear = Mi(L("Str_Sketch_Clear", "Clear all"), 0xE894, ClearAll);
                clear.IsEnabled = _objects.Count > 0;
                menu.Items.Add(clear);
            }
            menu.IsOpen = true;
            e.Handled = true;
        }

        private static MenuItem Mi(string text, int icon, Action onClick)
        {
            var mi = new MenuItem { Header = text };
            if (icon != 0) mi.Icon = char.ConvertFromUtf32(icon);
            mi.Click += (_, _) => onClick();
            return mi;
        }

        private static Separator Sep()
        {
            // The default menu separator renders as a bright white rule; give it a subtle themed line.
            var line = new FrameworkElementFactory(typeof(Border));
            line.SetValue(Border.HeightProperty, 1.0);
            line.SetValue(Border.MarginProperty, new Thickness(12, 4, 12, 4));
            line.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
            return new Separator { Template = new ControlTemplate(typeof(Separator)) { VisualTree = line } };
        }

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

        private void PasteImage()
        {
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource src) AddImage(src, null);
        }
    }
}
