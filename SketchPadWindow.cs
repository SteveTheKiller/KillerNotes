using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using Microsoft.Win32;

namespace KillerNotes
{
    // SketchPad (BACKLOG: SketchPad - "mini MS Paint"). A themed, MODELESS companion window in the
    // KillerPDF dialog chrome: a floating rounded card with a soft drop shadow and a single red
    // rounded close button (double-click the title bar to maximize). Keep it open alongside the
    // notepad and switch freely. It draws a list of SketchObjects (SketchModel) - freehand, line,
    // arrow, rectangle, ellipse, a paint bucket, with fill + fill opacity, stroke width, an eraser,
    // and undo/redo - scaled to fill via a Viewbox. "Print to note" stamps the drawing inline at the
    // note's caret WITHOUT closing. Glyph codepoints pass as ints (char.ConvertFromUtf32) so no Segoe
    // MDL2 private-use characters live in the source; the shape/bucket icons are drawn as WPF shapes.
    internal sealed class SketchPadWindow : Window
    {
        private enum Tool { Select, Pen, Line, Arrow, Rect, Ellipse, Polygon, Bucket, Text, Eraser }

        private readonly Canvas _canvas;
        private readonly Action<IReadOnlyList<SketchObject>, int, int> _print;
        private int _canvasW = Sketch.CanvasW, _canvasH = Sketch.CanvasH;   // live drawing size; grows with the window
        private readonly List<SketchObject> _objects = [];
        private readonly Stack<List<SketchObject>> _undo = new();
        private readonly Stack<List<SketchObject>> _redo = new();

        private Tool _tool = Tool.Pen;
        private Color _penColor = DefaultPen;
        private double _penWidth = 3;
        private bool _fill;
        private byte _fillAlpha = 0x66;   // ~40%, cycled by the opacity button

        // In-progress gesture state.
        private bool _dragging;
        private Point _start;
        private List<double> _gesturePts = [];
        private UIElement? _previewEl;

        // Eraser cursor ring: shows the erase radius under the pointer while the eraser is active.
        private Ellipse? _eraseCursor;

        // Polygon-in-progress state (multi-click; not part of _objects until committed).
        private readonly List<Point> _polyPts = [];
        private Polyline? _polyLine;
        private Line? _polyRubber;
        private Ellipse? _polySnap;
        private const double PolySnapPx = 10;
        private bool PolyActive => _polyPts.Count > 0;

        // Select / move state (Tool.Select): the grabbed object and the last drag point.
        private SketchObject? _sel;
        private Point _lastMove;
        private bool _movePushed;
        private static readonly string[] ImgExts = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff"];

        // Resize state: dragging a corner handle scales _sel from a snapshot about the opposite corner.
        private bool _resizing;
        private int _resizeHandle;
        private Point _resizeAnchor;
        private SketchObject? _resizeOrig;
        private Rect _resizeOrigBounds;

        // Scroll-wheel-over-image opacity: coalesce a run of ticks on one image into a single undo.
        private SketchObject? _wheelObj;

        // Dragging a line/arrow's round control handle bends it (quadratic bezier control point).
        private bool _curving;

        // "Arc line" live mode: after the menu pick, mouse movement bends the line until a click sets it.
        private SketchObject? _arcTarget;

        // Text tool: an inline TextBox overlays the canvas while a label is typed. _textEditTarget is
        // the existing object being re-edited (null when placing a new label); _textAt is its origin.
        private TextBox? _textBox;
        private SketchObject? _textEditTarget;
        private Point _textAt;

        // UI refs.
        private Border _outerBorder = null!;
        private Border _closeBtn = null!;
        private Border? _grainBorder;
        private readonly Dictionary<Tool, Button> _toolBtns = [];
        private StackPanel _swatchRow = null!;
        private Button _undoBtn = null!, _redoBtn = null!, _widthBtn = null!;
        private Ellipse _widthDot = null!;
        private TextBlock _opacityText = null!;
        private Border _fillSquare = null!;

        private static readonly int[] Widths = [2, 4, 6, 10, 16];
        private static readonly int[] Alphas = [51, 102, 153, 204, 255];   // 20 / 40 / 60 / 80 / 100 %
        private static readonly Color DefaultPen = (Color)ColorConverter.ConvertFromString("#E3DAC9");   // bone - warm off-white, reads well on the dark canvas
        private const double EraseRadius = 11;

        private static SolidColorBrush R(string key) => (SolidColorBrush)Application.Current.Resources[key];
        private static string L(string key, string fallback) => Application.Current.TryFindResource(key) as string ?? fallback;

        public SketchPadWindow(Window? owner, Action<IReadOnlyList<SketchObject>, int, int> print)
        {
            _print = print;
            Title = "KillerNotes - SketchPad";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;                 // rounded card + shadow halo need a transparent window
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            Background = Brushes.Transparent;
            Width = 900; Height = 720;
            MinWidth = 520; MinHeight = 460;
            Owner = owner;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;

            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(24),   // wide enough to reach past the 20px shadow halo onto the visible card edge + corners
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });

            // No fixed size: the canvas stretches to fill the frame, so resizing the window enlarges the
            // actual drawing area (1:1 pixels) rather than scaling a fixed canvas. _canvasW/H track it.
            _canvas = new Canvas();
            _canvas.SetResourceReference(Panel.BackgroundProperty, "PaneBrush");   // DynamicResource so it follows a live theme change
            _canvas.MouseLeftButtonDown += CanvasDown;
            _canvas.MouseMove += CanvasMove;
            _canvas.MouseLeftButtonUp += CanvasUp;
            _canvas.MouseRightButtonDown += OnCanvasRightDown;   // context menu (object vs empty canvas)
            _canvas.MouseWheel += OnWheel;                       // scroll over a placed image = its opacity
            _canvas.MouseLeave += (_, _) => HideEraseCursor();   // drop the eraser ring when the pointer leaves the canvas
            _canvas.SizeChanged += (_, _) =>
            {
                _canvasW = Math.Max(1, (int)_canvas.ActualWidth);
                _canvasH = Math.Max(1, (int)_canvas.ActualHeight);
            };

            BuildUi();
            SetTool(Tool.Pen);
            UpdateUndoButtons();

            // Drag an image file (or a raw bitmap) anywhere onto the pad to drop it in at that point.
            AllowDrop = true;
            DragOver += OnDragOver;
            Drop += OnDrop;

            StateChanged += (_, _) => UpdateWindowCorners();
            KeyDown += (_, e) =>
            {
                if (_textBox != null) return;   // the inline text editor owns the keyboard while a label is open
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                if (e.Key == Key.Escape) { if (_arcTarget != null) { _arcTarget = null; _canvas.ReleaseMouseCapture(); RenderCanvas(); e.Handled = true; } else if (PolyActive) { CancelPoly(); e.Handled = true; } else Close(); }
                else if (PolyActive && e.Key == Key.Enter && _polyPts.Count >= 3) { CommitPoly(); e.Handled = true; }
                else if (PolyActive && e.Key == Key.Back) { PolyBackspace(); e.Handled = true; }
                else if (e.Key == Key.Delete && _sel != null) { DeleteSelected(); e.Handled = true; }
                else if (ctrl && e.Key == Key.V) { PasteImage(); e.Handled = true; }
                else if (ctrl && e.Key == Key.Z && !shift) { Undo(); e.Handled = true; }
                else if (ctrl && (e.Key == Key.Y || (e.Key == Key.Z && shift))) { Redo(); e.Handled = true; }
                else if (ctrl && e.Key == Key.Enter) { _print(_objects, _canvasW, _canvasH); e.Handled = true; }   // Print to note
                else if (!ctrl)
                {
                    // Single-key tool switches (Steve prefers bare letters). Safe: the pad has no text
                    // field except the inline label editor, which is guarded above (returns early).
                    switch (e.Key)
                    {
                        case Key.V: SetTool(Tool.Select); e.Handled = true; break;
                        case Key.P: SetTool(Tool.Pen); e.Handled = true; break;
                        case Key.L: SetTool(Tool.Line); e.Handled = true; break;
                        case Key.A: SetTool(Tool.Arrow); e.Handled = true; break;
                        case Key.R: SetTool(Tool.Rect); e.Handled = true; break;
                        case Key.O: SetTool(Tool.Ellipse); e.Handled = true; break;
                        case Key.G: SetTool(Tool.Polygon); e.Handled = true; break;
                        case Key.B: SetTool(Tool.Bucket); e.Handled = true; break;
                        case Key.T: SetTool(Tool.Text); e.Handled = true; break;
                        case Key.E: SetTool(Tool.Eraser); e.Handled = true; break;
                        case Key.I: AddImageFromFile(); e.Handled = true; break;
                    }
                }
            };
        }

        /// <summary>Load an existing sketch's objects into the pad (double-click a printed sketch).
        /// Cloned so the pad's edits don't touch the caller's list, and history is reset. When the
        /// drawn canvas size is known (the placed bitmap's pixel size), the window grows or shrinks
        /// so the sketch reopens at the size it was made.</summary>
        public void LoadObjects(IReadOnlyList<SketchObject> objs, int canvasW = 0, int canvasH = 0)
        {
            _objects.Clear();
            _objects.AddRange(SketchModel.CloneList(objs));
            _undo.Clear();
            _redo.Clear();
            _sel = null;
            if (canvasW > 0 && canvasH > 0) ResizeCanvasTo(canvasW, canvasH);
            RenderCanvas();
            UpdateUndoButtons();
        }

        /// <summary>Open a plain (non-sketch) image in the pad as a full-canvas drawable layer, so it
        /// can be annotated and Printed back over the original. The canvas is sized to the image so
        /// Print round-trips at the same pixel size. History is reset, like LoadObjects.</summary>
        public void LoadImageAsBackdrop(BitmapSource src)
        {
            _objects.Clear();
            _undo.Clear();
            _redo.Clear();
            _sel = null;
            string? b64 = ToPngB64(src);
            if (b64 != null)
            {
                int w = Math.Max(1, src.PixelWidth), h = Math.Max(1, src.PixelHeight);
                _objects.Add(new SketchObject { Kind = SketchKind.Image, Img = b64, X = 0, Y = 0, W = w, H = h });
                ResizeCanvasTo(w, h);
            }
            RenderCanvas();
            UpdateUndoButtons();
        }

        // Grow / shrink the window so its drawing canvas lands at (targetW, targetH), restoring a
        // reopened sketch to the size it was drawn. Deferred until layout has run so the canvas and
        // chrome sizes are current; clamped to the screen work area and the window minimums.
        private void ResizeCanvasTo(int targetW, int targetH)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (WindowState == WindowState.Maximized) return;   // don't fight a maximized window
                double cw = _canvas.ActualWidth, ch = _canvas.ActualHeight;
                if (cw < 1 || ch < 1) return;
                double chromeW = ActualWidth - cw, chromeH = ActualHeight - ch;   // borders, toolbar, buttons, shadow halo
                var area = SystemParameters.WorkArea;
                Width = Math.Max(MinWidth, Math.Min(area.Width, targetW + chromeW));
                Height = Math.Max(MinHeight, Math.Min(area.Height, targetH + chromeH));
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static DropShadowEffect CardShadow()
            => new() { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.55 };

        // Rounded floating card normally; squared off and flush (no halo / shadow) when maximized.
        private void UpdateWindowCorners()
        {
            bool max = WindowState == WindowState.Maximized;
            _outerBorder.CornerRadius = new CornerRadius(max ? 0 : 7);
            _outerBorder.Margin = max ? new Thickness(0) : new Thickness(20);
            _outerBorder.Effect = max ? null : CardShadow();
            if (_grainBorder != null) _grainBorder.CornerRadius = new CornerRadius(max ? 0 : 7);
            _closeBtn.CornerRadius = new CornerRadius(0, max ? 0 : 7, 0, 0);
        }

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
            if (_dragging) _canvas.CaptureMouse();
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
            var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff", CheckFileExists = true };
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

        // Live preview element for the in-progress gesture (never part of _objects).
        private void UpdatePreview(Point cur)
        {
            RemovePreview();
            UIElement? el = _tool switch
            {
                Tool.Pen => SketchModel.BuildElement(new SketchObject
                {
                    Kind = SketchKind.Freehand, Color = SketchModel.HexOf(_penColor), Width = _penWidth,
                    Pts = [.._gesturePts],
                }),
                Tool.Line or Tool.Arrow or Tool.Rect or Tool.Ellipse => SketchModel.BuildElement(TempShape(cur)),
                _ => null,
            };
            if (el != null) { el.IsHitTestVisible = false; _previewEl = el; _canvas.Children.Add(el); }
        }

        private void RemovePreview()
        {
            if (_previewEl != null) { _canvas.Children.Remove(_previewEl); _previewEl = null; }
        }

        private SketchObject TempShape(Point end)
        {
            var o = new SketchObject
            {
                Color = SketchModel.HexOf(_penColor), Width = _penWidth,
                Pts = [_start.X, _start.Y, end.X, end.Y],
                Kind = _tool switch
                {
                    Tool.Line => SketchKind.Line,
                    Tool.Arrow => SketchKind.Arrow,
                    Tool.Rect => SketchKind.Rect,
                    Tool.Ellipse => SketchKind.Ellipse,
                    _ => SketchKind.Line,
                },
            };
            if (o.Kind is SketchKind.Rect or SketchKind.Ellipse) o.Fill = FillHex();
            return o;
        }

        private void CommitGesture(Point end)
        {
            SketchObject? obj = null;
            if (_tool == Tool.Pen)
            {
                if (_gesturePts.Count >= 4)
                    obj = new SketchObject
                    {
                        Kind = SketchKind.Freehand, Color = SketchModel.HexOf(_penColor),
                        Width = _penWidth, Pts = [.._gesturePts],
                    };
            }
            else if ((end - _start).Length >= 3)
            {
                obj = TempShape(end);
            }
            if (obj != null) { PushUndo(); _objects.Add(obj); }
            RenderCanvas();
        }

        private void EraseStep(Point p)
        {
            var res = SketchModel.EraseAt(_objects, p, EraseRadius);
            _objects.Clear();
            _objects.AddRange(res);
            RenderCanvas();
        }

        // Eraser ring: a thin outline the size of the erase area, drawn under the pointer so the edges
        // of what the eraser will remove are visible. Hit-test-invisible so it never blocks a drag.
        private Ellipse MakeEraseCursor()
        {
            var ring = new Ellipse
            {
                Width = EraseRadius * 2,
                Height = EraseRadius * 2,
                StrokeThickness = 1,
                Fill = null,
                IsHitTestVisible = false,
            };
            ring.SetResourceReference(Shape.StrokeProperty, "TextBrush");   // follows the live theme
            return ring;
        }

        private void ShowEraseCursor(Point p)
        {
            _eraseCursor ??= MakeEraseCursor();
            if (!_canvas.Children.Contains(_eraseCursor)) _canvas.Children.Add(_eraseCursor);
            Canvas.SetLeft(_eraseCursor, p.X - EraseRadius);
            Canvas.SetTop(_eraseCursor, p.Y - EraseRadius);
        }

        private void HideEraseCursor()
        {
            if (_eraseCursor != null && _canvas.Children.Contains(_eraseCursor)) _canvas.Children.Remove(_eraseCursor);
        }

        // Paint bucket: a raster flood fill from the click point (MS Paint style). Rasterize the current
        // drawing, flood the connected region the click lands in - bounded by whatever strokes/shapes
        // surround it - and add just that region as a fill layer behind the lines. An OPEN scribble
        // leaks out to the canvas edge exactly like a real bucket; enclose the area to contain the fill.
        private void BucketFill(Point p)
        {
            int w = _canvasW, h = _canvasH;
            int cx = Math.Max(0, Math.Min(w - 1, (int)p.X));
            int cy = Math.Max(0, Math.Min(h - 1, (int)p.Y));

            var src = SketchModel.RenderObjects(_objects, w, h);
            var straight = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            var px = new int[w * h];
            straight.CopyPixels(px, w * 4, 0);

            int fillInt = unchecked((_fillAlpha << 24) | (_penColor.R << 16) | (_penColor.G << 8) | _penColor.B);
            int target = px[cy * w + cx];
            if (ColorClose(target, fillInt)) return;   // the region is already this color

            var region = new int[w * h];
            var stack = new Stack<int>();
            stack.Push(cy * w + cx);
            int minx = w, miny = h, maxx = -1, maxy = -1;
            bool any = false;
            while (stack.Count > 0)
            {
                int idx = stack.Pop();
                if (region[idx] != 0 || !ColorClose(px[idx], target)) continue;
                region[idx] = fillInt;
                any = true;
                int x = idx % w, y = idx / w;
                if (x < minx) minx = x; if (x > maxx) maxx = x;
                if (y < miny) miny = y; if (y > maxy) maxy = y;
                if (x > 0) stack.Push(idx - 1);
                if (x < w - 1) stack.Push(idx + 1);
                if (y > 0) stack.Push(idx - w);
                if (y < h - 1) stack.Push(idx + w);
            }
            if (!any) return;

            int cw = maxx - minx + 1, ch = maxy - miny + 1;
            var crop = new int[cw * ch];
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                    crop[y * cw + x] = region[(miny + y) * w + (minx + x)];
            var bmp = BitmapSource.Create(cw, ch, 96, 96, PixelFormats.Bgra32, null, crop, cw * 4);
            bmp.Freeze();
            string? b64 = ToPngB64(bmp);
            if (b64 == null) return;

            // Sit the fill behind the strokes (but above any backdrop) so the lines stay as its edges.
            PushUndo();
            int at = 0;
            while (at < _objects.Count && _objects[at].Kind == SketchKind.Image && _objects[at].Backdrop) at++;
            _objects.Insert(at, new SketchObject { Kind = SketchKind.Image, Img = b64, X = minx, Y = miny, W = cw, H = ch });
            RenderCanvas();
        }

        private static bool ColorClose(int a, int b)
        {
            const int tol = 48;
            return Math.Abs(((a >> 24) & 0xFF) - ((b >> 24) & 0xFF)) <= tol
                && Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF)) <= tol
                && Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF)) <= tol
                && Math.Abs((a & 0xFF) - (b & 0xFF)) <= tol;
        }

        // ---- Polygon (multi-click: click each corner, click the first / double-click / Enter to close) ----

        private void PolyClick(Point p)
        {
            if (_polyPts.Count == 0)
            {
                _polyLine = new Polyline { Stroke = new SolidColorBrush(_penColor), StrokeThickness = _penWidth, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
                _polyLine.Points.Add(p);
                _polyRubber = new Line
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(0x88, _penColor.R, _penColor.G, _penColor.B)),
                    StrokeThickness = Math.Max(1, _penWidth / 2), StrokeDashArray = [4, 3],
                    IsHitTestVisible = false, X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y,
                };
                _polySnap = new Ellipse { Width = 14, Height = 14, StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
                _polySnap.SetResourceReference(Shape.StrokeProperty, "PrimaryBrush");
                Canvas.SetLeft(_polySnap, p.X - 7); Canvas.SetTop(_polySnap, p.Y - 7);
                _canvas.Children.Add(_polyLine);
                _canvas.Children.Add(_polyRubber);
                _canvas.Children.Add(_polySnap);
                _polyPts.Add(p);
                return;
            }
            if (_polyPts.Count >= 3 && (p - _polyPts[0]).Length <= PolySnapPx) { CommitPoly(); return; }
            _polyPts.Add(p);
            _polyLine!.Points.Add(p);
            _polyRubber!.X1 = p.X; _polyRubber.Y1 = p.Y;
        }

        private void PolyRubber(Point p)
        {
            if (_polyRubber == null) return;
            _polyRubber.X2 = p.X; _polyRubber.Y2 = p.Y;
            if (_polySnap != null)
                _polySnap.Visibility = _polyPts.Count >= 3 && (p - _polyPts[0]).Length <= PolySnapPx
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CommitPoly()
        {
            var pts = new List<Point>(_polyPts);
            ResetPoly();
            while (pts.Count >= 2 && (pts[^1] - pts[^2]).Length < 3) pts.RemoveAt(pts.Count - 1);
            if (pts.Count < 3) { RenderCanvas(); return; }
            var o = new SketchObject { Kind = SketchKind.Polygon, Color = SketchModel.HexOf(_penColor), Width = _penWidth };
            foreach (var pt in pts) { o.Pts.Add(pt.X); o.Pts.Add(pt.Y); }
            if (_fill) o.Fill = FillHex();
            PushUndo();
            _objects.Add(o);
            RenderCanvas();
        }

        private void CancelPoly()
        {
            if (!PolyActive) return;
            ResetPoly();
            RenderCanvas();
        }

        private void PolyBackspace()
        {
            if (_polyPts.Count == 0) return;
            if (_polyPts.Count == 1) { CancelPoly(); return; }
            _polyPts.RemoveAt(_polyPts.Count - 1);
            _polyLine!.Points.RemoveAt(_polyLine.Points.Count - 1);
            var last = _polyPts[^1];
            _polyRubber!.X1 = last.X; _polyRubber.Y1 = last.Y;
        }

        private void ResetPoly()
        {
            if (_polyLine != null) _canvas.Children.Remove(_polyLine);
            if (_polyRubber != null) _canvas.Children.Remove(_polyRubber);
            if (_polySnap != null) _canvas.Children.Remove(_polySnap);
            _polyPts.Clear();
            _polyLine = null; _polyRubber = null; _polySnap = null;
        }

        private void RenderCanvas()
        {
            _canvas.Children.Clear();
            foreach (var o in _objects)
            {
                if (ReferenceEquals(o, _textEditTarget)) continue;   // its inline editor is open; don't draw the original underneath
                _canvas.Children.Add(SketchModel.BuildElement(o));
            }
            if (_sel != null && _objects.Contains(_sel))
            {
                var b = SketchModel.BoundsOf(_sel);
                if (!b.IsEmpty)
                {
                    var box = new Rectangle
                    {
                        Width = b.Width + 8, Height = b.Height + 8, StrokeThickness = 1.2,
                        StrokeDashArray = [4, 3], IsHitTestVisible = false, Fill = null,
                    };
                    box.SetResourceReference(Shape.StrokeProperty, "PrimaryBrush");
                    Canvas.SetLeft(box, b.X - 4); Canvas.SetTop(box, b.Y - 4);
                    _canvas.Children.Add(box);
                    if (_arcTarget == null)   // hide handles while a click-to-solidify arc is in progress
                    {
                        // Corner handles: drag to scale (images stay proportional, shapes free).
                        AddHandle(b.Left, b.Top, 0, Cursors.SizeNWSE);
                        AddHandle(b.Right, b.Top, 1, Cursors.SizeNESW);
                        AddHandle(b.Right, b.Bottom, 2, Cursors.SizeNWSE);
                        AddHandle(b.Left, b.Bottom, 3, Cursors.SizeNESW);
                        // A line/arrow also gets a round "bend" handle at its control point (or midpoint).
                        if (_sel.Kind is SketchKind.Line or SketchKind.Arrow) AddCurveHandle(_sel);
                    }
                }
            }
            // Keep the eraser ring on top after a re-render while the pointer is over the canvas.
            if (_tool == Tool.Eraser && _eraseCursor != null && _canvas.IsMouseOver && !_canvas.Children.Contains(_eraseCursor))
                _canvas.Children.Add(_eraseCursor);
            _previewEl = null;   // cleared with the children; gesture handlers re-add if needed
        }

        private void AddHandle(double x, double y, int index, Cursor cursor)
        {
            var h = new Rectangle { Width = 10, Height = 10, Stroke = Brushes.White, StrokeThickness = 1, Cursor = cursor };
            h.SetResourceReference(Shape.FillProperty, "PrimaryBrush");
            Canvas.SetLeft(h, x - 5); Canvas.SetTop(h, y - 5);
            h.MouseLeftButtonDown += (_, e) => { ResizeStart(index); e.Handled = true; };
            _canvas.Children.Add(h);
        }

        private void AddCurveHandle(SketchObject o)
        {
            double hx, hy;
            if (o.Pts.Count >= 6) { hx = o.Pts[2]; hy = o.Pts[3]; }
            else { hx = (o.Pts[0] + o.Pts[^2]) / 2; hy = (o.Pts[1] + o.Pts[^1]) / 2; }
            var h = new Ellipse { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 1, Cursor = Cursors.Hand };
            h.SetResourceReference(Shape.FillProperty, "PrimaryBrush");
            Canvas.SetLeft(h, hx - 6); Canvas.SetTop(h, hy - 6);
            h.MouseLeftButtonDown += (_, e) => { CurveStart(Clamp(e.GetPosition(_canvas))); e.Handled = true; };
            _canvas.Children.Add(h);
        }

        // "Arc line": enter live-bend mode. Select the line, hide its handles, and let mouse movement
        // shape the curve (CanvasMove -> CurveMove) until a click solidifies it (CanvasDown). PushUndo
        // now so the whole bend is a single undo step.
        private void StartArc(SketchObject o)
        {
            PushUndo();
            _sel = o;
            SetTool(Tool.Select);
            _arcTarget = o;
            RenderCanvas();
            // Capture the mouse so the live bend tracks - and the solidify click registers - no matter
            // where the cursor is, even off the canvas or off the window. Deferred so the closing context
            // menu releases its own capture first.
            Dispatcher.BeginInvoke(new Action(() => { if (_arcTarget != null) _canvas.CaptureMouse(); }),
                System.Windows.Threading.DispatcherPriority.Input);
        }

        // "Straighten line": drop the control point, back to a plain two-point line/arrow.
        private void Straighten(SketchObject o)
        {
            if (o.Pts.Count < 6) return;
            PushUndo();
            double x0 = o.Pts[0], y0 = o.Pts[1], x1 = o.Pts[^2], y1 = o.Pts[^1];
            o.Pts.Clear();
            o.Pts.Add(x0); o.Pts.Add(y0); o.Pts.Add(x1); o.Pts.Add(y1);
            RenderCanvas();
        }

        private void CurveStart(Point p)
        {
            if (_sel == null) return;
            PushUndo();
            _curving = true;
            _canvas.CaptureMouse();
            CurveMove(p);
        }

        private void CurveMove(Point p)
        {
            if (_sel == null || _sel.Kind is not (SketchKind.Line or SketchKind.Arrow)) return;
            var o = _sel;
            double x0 = o.Pts[0], y0 = o.Pts[1];
            double x1 = o.Pts[^2], y1 = o.Pts[^1];
            o.Pts.Clear();
            o.Pts.Add(x0); o.Pts.Add(y0);
            o.Pts.Add(p.X); o.Pts.Add(p.Y);
            o.Pts.Add(x1); o.Pts.Add(y1);
            RenderCanvas();
        }

        private string? FillHex()
            => _fill ? SketchModel.HexOf(Color.FromArgb(_fillAlpha, _penColor.R, _penColor.G, _penColor.B)) : null;

        // ---- Text tool ----

        // Open an inline TextBox over the canvas: a new label at `at`, or `existing` re-edited in
        // place (its original is hidden by RenderCanvas until the edit commits). Enter or a click
        // elsewhere commits; Esc cancels; an empty commit places nothing (or deletes the object).
        private void BeginTextEntry(Point at, SketchObject? existing)
        {
            if (_textBox != null) CommitTextEntry();
            _textAt = at;
            _textEditTarget = existing;
            if (existing != null) { _sel = null; RenderCanvas(); }   // hide the original while its editor is open

            var color = existing != null ? ColorOf(existing.Color) : _penColor;
            var tb = new TextBox
            {
                Text = existing?.Text ?? "",
                FontSize = existing?.FontSize ?? 24,
                FontWeight = (existing?.Bold ?? false) ? FontWeights.Bold : FontWeights.Normal,
                Foreground = new SolidColorBrush(color),
                CaretBrush = new SolidColorBrush(color),
                Background = R("PaneBrush"),               // matches the canvas, so only the accent border reads
                BorderBrush = R("PrimaryBrush"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                AcceptsReturn = true,                      // Shift+Enter adds a line; Enter commits
                MinWidth = 24,
            };
            tb.PreviewKeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { CommitTextEntry(); ke.Handled = true; }
                else if (ke.Key == Key.Escape) { CancelTextEntry(); ke.Handled = true; }
            };
            tb.LostKeyboardFocus += (_, _) => CommitTextEntry();

            _textBox = tb;
            Canvas.SetLeft(tb, at.X); Canvas.SetTop(tb, at.Y);
            _canvas.Children.Add(tb);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                tb.Focus();
                if (existing != null) tb.SelectAll(); else tb.CaretIndex = tb.Text.Length;
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        // Bank the typed label: update the edited object, drop it if emptied, or add a new one.
        private void CommitTextEntry()
        {
            if (_textBox == null) return;
            var tb = _textBox;
            _textBox = null;
            _canvas.Children.Remove(tb);
            string text = tb.Text.Replace("\r\n", "\n").Trim();
            var target = _textEditTarget;
            _textEditTarget = null;

            SketchObject? result = null;
            if (target != null)
            {
                if (text.Length == 0) { PushUndo(); _objects.Remove(target); }
                else if (text != target.Text) { PushUndo(); target.Text = text; result = target; }
                else result = target;
            }
            else if (text.Length > 0)
            {
                PushUndo();
                result = new SketchObject { Kind = SketchKind.Text, Text = text, X = _textAt.X, Y = _textAt.Y, Color = SketchModel.HexOf(_penColor) };
                _objects.Add(result);
            }

            _sel = result;
            if (result != null) SetTool(Tool.Select);   // land in Select so the new label can be nudged / resized
            RenderCanvas();
        }

        // Abandon the edit; the original (if any) comes back untouched.
        private void CancelTextEntry()
        {
            if (_textBox == null) return;
            var tb = _textBox;
            _textBox = null;
            _textEditTarget = null;
            _canvas.Children.Remove(tb);
            RenderCanvas();
        }

        private static Color ColorOf(string hex)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return DefaultPen; }
        }

        // ---- Undo / redo ----

        private void PushUndo()
        {
            _undo.Push(SketchModel.CloneList(_objects));
            _redo.Clear();
            UpdateUndoButtons();
        }

        private void Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Push(SketchModel.CloneList(_objects));
            var prev = _undo.Pop();
            _objects.Clear(); _objects.AddRange(prev);
            _sel = null;
            RenderCanvas(); UpdateUndoButtons();
        }

        private void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Push(SketchModel.CloneList(_objects));
            var next = _redo.Pop();
            _objects.Clear(); _objects.AddRange(next);
            _sel = null;
            RenderCanvas(); UpdateUndoButtons();
        }

        private void ClearAll()
        {
            if (_objects.Count == 0) return;
            PushUndo();
            _objects.Clear();
            _sel = null;
            RenderCanvas();
        }

        private void UpdateUndoButtons()
        {
            _undoBtn.IsEnabled = _undo.Count > 0;
            _redoBtn.IsEnabled = _redo.Count > 0;
        }

        // ---- Tool / color / width / fill / opacity state ----

        private void SetTool(Tool t)
        {
            if (PolyActive && t != Tool.Polygon) CancelPoly();
            if (t != Tool.Select && _sel != null) { _sel = null; RenderCanvas(); }
            _tool = t;
            foreach (var kv in _toolBtns)
            {
                if (kv.Key == t) kv.Value.SetResourceReference(Control.BackgroundProperty, "RowSelectedBrush");
                else kv.Value.ClearValue(Control.BackgroundProperty);
            }
            _canvas.Cursor = t == Tool.Pen ? Cursors.Pen : t == Tool.Text ? Cursors.IBeam : t == Tool.Select ? Cursors.Arrow : Cursors.Cross;
            if (t != Tool.Eraser) HideEraseCursor();
        }

        private void PickColor(Color c)
        {
            _penColor = c;
            if (_tool == Tool.Eraser) SetTool(Tool.Pen);
        }

        private void CycleWidth()
        {
            int i = Array.IndexOf(Widths, (int)_penWidth);
            _penWidth = Widths[(i + 1) % Widths.Length];
            SetWidthDot();
        }

        private void SetWidthDot()
        {
            double d = Math.Max(3, Math.Min(18, _penWidth));
            _widthDot.Width = d; _widthDot.Height = d;
        }

        private void CycleOpacity()
        {
            int i = Array.IndexOf(Alphas, _fillAlpha);
            if (i < 0) i = 1;
            _fillAlpha = (byte)Alphas[(i + 1) % Alphas.Length];
            _opacityText.Text = OpacityLabel();
        }

        private string OpacityLabel() => $"{(int)Math.Round(_fillAlpha / 255.0 * 100)}%";

        private void ToggleFill()
        {
            _fill = !_fill;
            if (_fill) _fillSquare.SetResourceReference(Border.BackgroundProperty, "PrimaryBrush");
            else { _fillSquare.ClearValue(Border.BackgroundProperty); _fillSquare.Background = Brushes.Transparent; }
        }

        // ---- Layout ----

        private void BuildUi()
        {
            // Floating rounded card with a soft drop shadow, matching the KillerPDF dialog chrome. The
            // 20px transparent halo (Margin) is the room the shadow renders into; squared off and flush
            // when maximized (UpdateWindowCorners).
            _outerBorder = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(20),
                Effect = CardShadow(),
            };
            _outerBorder.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            _outerBorder.SetResourceReference(Border.BackgroundProperty, "BackgroundBrush");
            var root = new Grid();
            // Film grain over the window background, same treatment as the rest of the app.
            if (Application.Current.TryFindResource("GrainTileBrush") is Brush grain)
            {
                double grainOp = Application.Current.TryFindResource("GrainOpacity") is double go ? go : 0.12;
                _grainBorder = new Border { Background = grain, Opacity = grainOp, IsHitTestVisible = false, CornerRadius = new CornerRadius(7) };
                root.Children.Add(_grainBorder);
            }
            var grid = new Grid { Margin = new Thickness(16, 6, 16, 12) };   // tight top - minimal forehead above the title
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // title
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // tools
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });   // canvas fills
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // buttons
            root.Children.Add(grid);

            // Red close button flush in the card's top-right corner (rounded only on that corner), so
            // it hugs the window edge like KillerPDF's dialogs rather than floating as a pill.
            _closeBtn = CloseButton(L("Str_Sketch_Close", "Close (Esc)"));
            _closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
            _closeBtn.VerticalAlignment = VerticalAlignment.Top;
            root.Children.Add(_closeBtn);

            // Resize-grip dots in the bottom-right corner - press them to start a corner resize.
            root.Children.Add(BuildResizeGrip());

            _outerBorder.Child = root;
            Content = _outerBorder;

            BuildTitleBar(grid);
            BuildToolBar(grid);
            BuildCanvas(grid);
            BuildButtons(grid);
        }

        private void BuildTitleBar(Grid grid)
        {
            var titleBar = new Grid { Margin = new Thickness(0, 0, 0, 8), Background = Brushes.Transparent };
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleBar.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ChangedButton != MouseButton.Left) return;
                if (e.ClickCount == 2) ToggleMaximize();
                else DragMove();
            };

            var wf = Application.Current.TryFindResource("WordmarkFont") as FontFamily;
            var mark = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var shadowInk = new SolidColorBrush(Color.FromArgb(0xD8, 0, 0, 0));
            var shadow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, 2, 0, 0) };
            if (Application.Current.TryFindResource("IconShadowOpacity") is double sop) shadow.Opacity = sop;
            shadow.Effect = new BlurEffect { Radius = 3 };
            shadow.Children.Add(WordmarkText(wf, "", "", "", shadowInk));
            mark.Children.Add(shadow);
            mark.Children.Add(WordmarkText(wf, "TextBrush", "PrimaryBrush", "MutedTextBrush"));
            Grid.SetColumn(mark, 0);
            titleBar.Children.Add(mark);

            // (The red close button lives at the card's top-right corner - added in BuildUi. Reserve the
            // right column so the wordmark never runs under it.)
            var spacer = new Border { Width = 48, Background = Brushes.Transparent };
            Grid.SetColumn(spacer, 1);
            titleBar.Children.Add(spacer);

            Grid.SetRow(titleBar, 0);
            grid.Children.Add(titleBar);
        }

        private void BuildToolBar(Grid grid)
        {
            // Top strip (row 1): the color palette on the left, undo / redo / clear on the right. The
            // drawing tools live in the left rail (BuildToolRail), MS-Paint style.
            var top = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   // palette
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                        // actions

            var palette = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _swatchRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            palette.Children.Add(_swatchRow);
            palette.Children.Add(ActionButton(Glyph(0xE790), L("Str_Sketch_MoreColors", "Custom color..."), OpenColorPicker));
            Grid.SetColumn(palette, 0);
            top.Children.Add(palette);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            _undoBtn = ActionButton(Glyph(0xE7A7), L("Str_Sketch_Undo", "Undo (Ctrl+Z)"), Undo);
            _redoBtn = ActionButton(Glyph(0xE7A6), L("Str_Sketch_Redo", "Redo (Ctrl+Y)"), Redo);
            actions.Children.Add(_undoBtn);
            actions.Children.Add(_redoBtn);
            actions.Children.Add(Separator());
            actions.Children.Add(ActionButton(Glyph(0xE894), L("Str_Sketch_Clear", "Clear all"), ClearAll));
            Grid.SetColumn(actions, 1);
            top.Children.Add(actions);

            RebuildSwatches();
            Grid.SetRow(top, 1);
            grid.Children.Add(top);
        }

        // MS-Paint-style vertical tool rail (left of the canvas). Grouped draw / brush / shapes /
        // content, with thin rules between; the eraser sits directly under the brush-size button.
        private UIElement BuildToolRail()
        {
            var rail = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 8, 0) };
            void Add(FrameworkElement b) { b.Width = 42; rail.Children.Add(b); }   // uniform rail width (matches the opacity button)

            Add(ToolButton(IconSelect(), L("Str_Sketch_Select", "Select / move (V) - click an object, drag to move it, Delete removes it"), Tool.Select));
            Add(ToolButton(Glyph(0xE70F), L("Str_Sketch_Pen", "Pen - freehand (P)"), Tool.Pen));
            Add(ToolButton(IconLine(), L("Str_Sketch_Line", "Line - straight (L)"), Tool.Line));
            Add(ToolButton(IconArrow(), L("Str_Sketch_Arrow", "Arrow - line with an arrowhead (A)"), Tool.Arrow));
            rail.Children.Add(RailSeparator());
            Add(WidthButton());
            Add(ToolButton(Glyph(0xE75C), L("Str_Sketch_Eraser", "Eraser (E) - brush over ink, or touch a shape to remove it"), Tool.Eraser));
            rail.Children.Add(RailSeparator());
            Add(ToolButton(IconRect(), L("Str_Sketch_Rect", "Rectangle (R)"), Tool.Rect));
            Add(ToolButton(IconEllipse(), L("Str_Sketch_Ellipse", "Ellipse (O)"), Tool.Ellipse));
            Add(ToolButton(IconPolygon(), L("Str_Sketch_Polygon", "Polygon (G) - click each corner; click the first point or double-click to close (Backspace undoes a point, Esc cancels)"), Tool.Polygon));
            Add(OpacityButton());
            Add(FillButton());
            rail.Children.Add(RailSeparator());
            Add(ToolButton(IconBucket(), L("Str_Sketch_Bucket", "Paint bucket (B) - click inside a closed area to flood-fill it"), Tool.Bucket));
            Add(ToolButton(IconText(), L("Str_Sketch_Text", "Text (T) - click to place a label, type, Enter to set it (Shift+Enter for a new line)"), Tool.Text));
            Add(ActionButton(IconImage(), L("Str_Sketch_AddImage", "Add an image (I) - drag one onto the pad, or Ctrl+V to paste"), AddImageFromFile));
            return rail;
        }

        // Horizontal 1px rule between rail groups, centered under the 42px buttons.
        private static Border RailSeparator()
        {
            var b = new Border { Width = 22, Height = 1, Margin = new Thickness(10, 4, 0, 5), HorizontalAlignment = HorizontalAlignment.Left };
            b.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
            return b;
        }

        private void BuildCanvas(Grid grid)
        {
            // Canvas + its own film grain, so the drawing surface reads as textured paper (not a flat
            // fill) like the rest of the app. Grain sits at SCREEN resolution ON TOP of the Viewbox
            // (never inside it) and spans the whole frame, so the Uniform letterbox is textured to
            // match. It only dresses the live surface; flattened objects (SketchModel) carry no grain.
            // Row 2 = the tool rail (left) + the drawing surface (fills the rest).
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                        // tool rail
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   // canvas

            var rail = BuildToolRail();
            Grid.SetColumn(rail, 0);
            row.Children.Add(rail);

            var canvasStack = new Grid();
            canvasStack.Children.Add(_canvas);   // fills the frame 1:1 and grows with the window
            if (Application.Current.TryFindResource("GrainTileBrush") is Brush canvasGrain)
            {
                double cop = Application.Current.TryFindResource("GrainOpacity") is double cg ? cg : 0.12;
                canvasStack.Children.Add(new Border { Background = canvasGrain, Opacity = cop, IsHitTestVisible = false });
            }
            var frame = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), ClipToBounds = true,
                Child = canvasStack,
            };
            frame.SetResourceReference(Border.BackgroundProperty, "PaneBrush");
            frame.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
            Grid.SetColumn(frame, 1);
            row.Children.Add(frame);

            Grid.SetRow(row, 2);
            grid.Children.Add(row);
        }

        private void BuildButtons(Grid grid)
        {
            // Bottom action row, centered: Copy to clipboard (the same flattened image Print makes),
            // then Print to note (stamps the drawing inline at the caret, keeps the pad open; Ctrl+Enter).
            // The title-bar X closes the pad.
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };

            var copy = new Button { Content = L("Str_Sketch_CopyImage", "Copy to clipboard"), MinWidth = 110, Height = 30, Margin = new Thickness(0, 0, 8, 0), Style = Application.Current.TryFindResource("OutlineButton") as Style };
            Tip(copy, L("Str_Sketch_CopyImageTip", "Copy the drawing to the clipboard as an image"));
            copy.Click += (_, _) => CopyToClipboard();
            actions.Children.Add(copy);

            var print = new Button { Content = L("Str_Btn_CalcPrint", "Print to note"), MinWidth = 110, Height = 30, IsDefault = true, Style = Application.Current.TryFindResource("OutlineButton") as Style };
            Tip(print, L("Str_Sketch_Print", "Print to note (Ctrl+Enter)"));
            print.Click += (_, _) => _print(_objects, _canvasW, _canvasH);
            actions.Children.Add(print);

            Grid.SetRow(actions, 3);
            grid.Children.Add(actions);
        }

        // Copy the current drawing to the Windows clipboard as an image (the same flattened bitmap
        // Print produces). No-op on an empty canvas.
        private void CopyToClipboard()
        {
            if (_objects.Count == 0) return;
            try
            {
                Clipboard.SetImage(SketchModel.RenderObjects(_objects, _canvasW, _canvasH));
            }
            catch { /* clipboard busy - nothing to do */ }
        }

        // Classic diagonal grip dots in the bottom-right corner - a REAL handle: pressing it starts a
        // bottom-right corner resize through the OS. The window's own resize border lives out in the
        // transparent shadow halo (easy to miss), so grabbing the visible dots is the reliable way.
        private UIElement BuildResizeGrip()
        {
            var c = new Canvas
            {
                Width = 18, Height = 18, Background = Brushes.Transparent, Cursor = Cursors.SizeNWSE,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 3, 3),
            };
            void Dot(double x, double y)
            {
                var d = new Ellipse { Width = 2.4, Height = 2.4 };
                d.SetResourceReference(Shape.FillProperty, "MutedTextBrush");
                Canvas.SetLeft(d, x); Canvas.SetTop(d, y);
                c.Children.Add(d);
            }
            Dot(15, 6);
            Dot(10.5, 10.5); Dot(15, 10.5);
            Dot(6, 15); Dot(10.5, 15); Dot(15, 15);
            c.MouseLeftButtonDown += (_, e) => { StartCornerResize(); e.Handled = true; };
            return c;
        }

        // Kick off an OS-driven bottom-right corner resize (from the grip dots).
        private void StartCornerResize()
        {
            if (WindowState == WindowState.Maximized) return;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            ReleaseCapture();
            SendMessage(hwnd, 0x00A1 /* WM_NCLBUTTONDOWN */, (IntPtr)17 /* HTBOTTOMRIGHT */, IntPtr.Zero);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private void RebuildSwatches()
        {
            _swatchRow.Children.Clear();
            _swatchRow.Children.Add(Swatch(DefaultPen));   // bone (the default pen) pinned first, ahead of the user swatches
            foreach (var c in ColorPickerDialog.UserSwatches())
                _swatchRow.Children.Add(Swatch(c));
        }

        private void OpenColorPicker()
        {
            var picker = new ColorPickerDialog(this, _penColor);
            bool ok = picker.ShowDialog() == true;
            RebuildSwatches();   // Replace / Reset in the picker may have edited the shared swatches
            if (ok) PickColor(picker.SelectedColor);
        }

        // ---- Widget builders ----

        private static Style? Surface() => Application.Current.TryFindResource("SurfaceButton") as Style;

        private static void Tip(FrameworkElement fe, string tip)
        {
            fe.ToolTip = tip;
            ToolTipService.SetInitialShowDelay(fe, 350);
            ToolTipService.SetShowDuration(fe, 12000);
            ToolTipService.SetShowOnDisabled(fe, true);
        }

        private static TextBlock Glyph(int codepoint) => new()
        {
            Text = char.ConvertFromUtf32(codepoint),
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private Button ToolButton(UIElement content, string tooltip, Tool tool)
        {
            var b = new Button
            {
                Width = 34, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6),
                Content = content, Style = Surface(),
            };
            Tip(b, tooltip);
            b.Click += (_, _) => SetTool(tool);
            _toolBtns[tool] = b;
            return b;
        }

        private static Button ActionButton(UIElement content, string tooltip, Action onClick)
        {
            var b = new Button
            {
                Width = 34, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6),
                Content = content, Style = Surface(),
            };
            Tip(b, tooltip);
            b.Click += (_, _) => onClick();
            return b;
        }

        private static Border Separator()
        {
            var b = new Border { Width = 1, Height = 22, Margin = new Thickness(3, 3, 7, 3) };
            b.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
            return b;
        }

        private Button FillButton()
        {
            _fillSquare = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(2), BorderThickness = new Thickness(1.5), Background = Brushes.Transparent };
            _fillSquare.SetResourceReference(Border.BorderBrushProperty, "TextBrush");
            var b = new Button
            {
                Width = 34, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6),
                Content = _fillSquare, Style = Surface(),
            };
            Tip(b, L("Str_Sketch_Fill", "Fill shapes (rectangle / ellipse)"));
            b.Click += (_, _) => ToggleFill();
            return b;
        }

        private Button OpacityButton()
        {
            _opacityText = new TextBlock { Text = OpacityLabel(), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _opacityText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var b = new Button
            {
                Width = 42, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6),
                Content = _opacityText, Style = Surface(),
            };
            Tip(b, L("Str_Sketch_Opacity", "Fill / bucket opacity (click to cycle)"));
            b.Click += (_, _) => CycleOpacity();
            return b;
        }

        private Button WidthButton()
        {
            _widthDot = new Ellipse { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _widthDot.SetResourceReference(Shape.FillProperty, "TextBrush");
            SetWidthDot();
            _widthBtn = new Button
            {
                Width = 34, Height = 28, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 6),
                Content = _widthDot, Style = Surface(),
            };
            Tip(_widthBtn, L("Str_Sketch_Width", "Brush size (click to cycle)"));
            _widthBtn.Click += (_, _) => CycleWidth();
            return _widthBtn;
        }

        private static Viewbox IconWrap(UIElement shape) => new() { Width = 17, Height = 17, Child = shape, Stretch = Stretch.Uniform };

        private static UIElement IconLine()
        {
            var l = new Line { X1 = 2, Y1 = 14, X2 = 14, Y2 = 2, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            l.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            return IconWrap(l);
        }

        // Line shaft + a solid filled arrowhead, so the arrow tool reads clearly apart from the line.
        private static UIElement IconArrow()
        {
            var g = new Grid { Width = 17, Height = 17 };
            var shaft = new Line { X1 = 2, Y1 = 15, X2 = 11, Y2 = 6, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            shaft.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            var head = new Polygon();
            foreach (var p in new[] { new Point(15.5, 1.5), new Point(8, 4), new Point(13, 9) }) head.Points.Add(p);
            head.SetResourceReference(Shape.FillProperty, "TextBrush");
            g.Children.Add(shaft);
            g.Children.Add(head);
            return g;
        }

        private static UIElement IconRect()
        {
            var r = new Rectangle { Width = 14, Height = 10, StrokeThickness = 2 };
            r.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            return IconWrap(r);
        }

        private static UIElement IconEllipse()
        {
            var el = new Ellipse { Width = 14, Height = 12, StrokeThickness = 2 };
            el.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            return IconWrap(el);
        }

        private static UIElement IconPolygon()
        {
            var pg = new Polygon { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            foreach (var p in new[] { new Point(8.5, 1), new Point(16, 6.5), new Point(13, 15.5), new Point(4, 15.5), new Point(1, 6.5) }) pg.Points.Add(p);
            pg.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            return IconWrap(pg);
        }

        private static UIElement IconSelect()
        {
            var r = new Rectangle { Width = 13, Height = 11, StrokeThickness = 1.6, StrokeDashArray = [2, 2], Fill = null };
            r.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            return IconWrap(r);
        }

        private static UIElement IconImage()
        {
            var c = new Canvas { Width = 16, Height = 16 };
            var frame = new Rectangle { Width = 16, Height = 13, RadiusX = 1.5, RadiusY = 1.5, StrokeThickness = 1.4, Fill = null };
            frame.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            Canvas.SetLeft(frame, 0); Canvas.SetTop(frame, 1.5);
            var mtn = new Polygon();
            foreach (var p in new[] { new Point(1, 13), new Point(6, 7.5), new Point(9.5, 11), new Point(11.5, 9), new Point(15, 13) }) mtn.Points.Add(p);
            mtn.SetResourceReference(Shape.FillProperty, "TextBrush");
            var sun = new Ellipse { Width = 3, Height = 3 };
            sun.SetResourceReference(Shape.FillProperty, "TextBrush");
            Canvas.SetLeft(sun, 3); Canvas.SetTop(sun, 4);
            c.Children.Add(frame); c.Children.Add(mtn); c.Children.Add(sun);
            return IconWrap(c);
        }

        // Text tool marker: a bold "A", the mini-Paint convention for a text tool.
        private static UIElement IconText()
        {
            var t = new TextBlock
            {
                Text = "A", FontSize = 16, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            return t;
        }

        private static UIElement IconBucket()
        {
            // A proper paint bucket: wire handle, tapered body, and accent-colored paint in the opening.
            var c = new Canvas { Width = 17, Height = 17 };
            var handle = new System.Windows.Shapes.Path { StrokeThickness = 1.4, Data = Geometry.Parse("M5,5.5 C5,1.5 12,1.5 12,5.5") };
            handle.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            var body = new System.Windows.Shapes.Path { StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round, Data = Geometry.Parse("M2.5,6 L4.3,15.2 Q8.5,16.6 12.7,15.2 L14.5,6 Z") };
            body.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            body.SetResourceReference(Shape.FillProperty, "MutedTextBrush");
            var rim = new Ellipse { Width = 12, Height = 3.4, StrokeThickness = 1.2 };
            rim.SetResourceReference(Shape.StrokeProperty, "TextBrush");
            rim.SetResourceReference(Shape.FillProperty, "PrimaryBrush");
            Canvas.SetLeft(rim, 2.5); Canvas.SetTop(rim, 4.3);
            c.Children.Add(handle);
            c.Children.Add(body);
            c.Children.Add(rim);
            return IconWrap(c);
        }

        private Border Swatch(Color c)
        {
            var b = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(c), Margin = new Thickness(0, 0, 6, 0),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };
            b.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
            Tip(b, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
            b.MouseLeftButtonUp += (_, _) => PickColor(c);
            return b;
        }

        // The three wordmark runs. Pass a flat brush for the drop-shadow copy, or resource keys for the
        // real one so its colors follow a live theme change (DynamicResource).
        private static TextBlock WordmarkText(FontFamily? wf, string killerKey, string notesKey, string subKey, Brush? flat = null)
        {
            var tb = new TextBlock { FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            if (wf != null) tb.FontFamily = wf;
            var killer = new Run("Killer");
            var notes = new Run("Notes") { FontSize = 19.5, FontWeight = FontWeights.Bold };
            var sub = new Run("  " + L("Str_Sketch_Title", "SketchPad")) { FontSize = 18 };
            if (flat != null) { killer.Foreground = flat; notes.Foreground = flat; sub.Foreground = flat; }
            else
            {
                killer.SetResourceReference(TextElement.ForegroundProperty, killerKey);
                notes.SetResourceReference(TextElement.ForegroundProperty, notesKey);
                sub.SetResourceReference(TextElement.ForegroundProperty, subKey);
            }
            tb.Inlines.Add(killer); tb.Inlines.Add(notes); tb.Inlines.Add(sub);
            return tb;
        }

        private void ToggleMaximize()
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        // Red close button in the card's top-right corner, KillerPDF ChromeCloseButton spec: 46x36,
        // red glyph at rest, solid red fill + white glyph on hover, rounded ONLY on the top-right
        // (0,7,0,0) so the fill hugs the window corner. Handles its own press so the title bar's
        // DragMove doesn't fire when clicked. Corner squares off when maximized (UpdateWindowCorners).
        private Border CloseButton(string tooltip)
        {
            var glyph = new TextBlock
            {
                Text = char.ConvertFromUtf32(0xE8BB),
                FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "DangerRed");
            var bd = new Border
            {
                Width = 46, Height = 36, CornerRadius = new CornerRadius(0, 7, 0, 0),
                Background = Brushes.Transparent, Child = glyph, Cursor = Cursors.Hand,
            };
            Tip(bd, tooltip);
            bd.MouseEnter += (_, _) => { bd.Background = R("DangerRed"); glyph.Foreground = Brushes.White; };
            bd.MouseLeave += (_, _) =>
            {
                bd.Background = Brushes.Transparent;
                glyph.SetResourceReference(TextBlock.ForegroundProperty, "DangerRed");
            };
            bd.MouseLeftButtonDown += (_, e) => e.Handled = true;   // don't let the title bar start a drag
            bd.MouseLeftButtonUp += (_, _) => Close();
            return bd;
        }
    }
}
