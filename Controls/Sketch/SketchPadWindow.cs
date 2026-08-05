using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Controls
{
    // SketchPad (BACKLOG: SketchPad - "mini MS Paint"). A themed, MODELESS companion window in the
    // KillerPDF dialog chrome: a floating rounded card with a soft drop shadow and a single red
    // rounded close button (double-click the title bar to maximize). Keep it open alongside the
    // notepad and switch freely. It draws a list of SketchObjects (SketchModel) - freehand, line,
    // arrow, rectangle, ellipse, a paint bucket, with fill + fill opacity, stroke width, an eraser,
    // and undo/redo - scaled to fill via a Viewbox. "Print to note" stamps the drawing inline at the
    // note's caret WITHOUT closing. Glyph codepoints pass as ints (char.ConvertFromUtf32) so no Segoe
    // MDL2 private-use characters live in the source; the shape/bucket icons are drawn as WPF shapes.
    internal sealed partial class SketchPadWindow : Window
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
    }
}
