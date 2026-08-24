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

namespace KillerNotes.Controls.Sketch
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
        private enum Tool { Select, Pen, Line, Arrow, Rect, Ellipse, Polygon, Bucket, Text, Eraser, Crop }

        private readonly Canvas _canvas;
        private readonly Action<IReadOnlyList<SketchObject>, int, int> _print;
        // Services.Sketch, QUALIFIED. This file's own namespace is KillerNotes.Controls.Sketch, so
        // a bare "Sketch" binds to that namespace and hides the Services class of the same name.
        private int _canvasW = Services.Sketch.CanvasW, _canvasH = Services.Sketch.CanvasH;   // live drawing size; grows with the window
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
        private Border _bevelLight = null!;
        private Border _bevelDark = null!;
        private FrameworkElement _closeBtn = null!;   // DialogChrome.CloseGlyph - a TextBlock, not a Border
        private Border? _grainBorder;
        private readonly Dictionary<Tool, Button> _toolBtns = [];
        private StackPanel _swatchRow = null!;
        private Button _undoBtn = null!, _redoBtn = null!, _widthBtn = null!;
        private Ellipse _widthDot = null!;
        private TextBlock _opacityText = null!;
        private Border _fillSquare = null!;

        private static readonly int[] Widths = [2, 4, 6, 10, 16];
        private static readonly int[] Alphas = [51, 102, 153, 204, 255];   // 20 / 40 / 60 / 80 / 100 %
        /// <summary>The pinned first swatch and the fallback ink. Follows the theme's TextBrush
        /// rather than being a fixed color: it was bone (#E3DAC9), picked when every canvas was
        /// dark, and on a light theme that is near-invisible on the paper. TextBrush is by
        /// definition the color that reads on the current surface - black on 98SE and Light,
        /// near-white on the dark palettes.</summary>
        private static Color DefaultPen =>
            Application.Current?.TryFindResource("TextBrush") is SolidColorBrush b ? b.Color : Colors.Black;
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
            // In the taskbar and Alt+Tab: the pads are free sibling windows now (no Owner), so
            // without a taskbar entry there was no way to switch between them and the main
            // window ("windows needs to register these windows in the taskbar", 2026-08-08).
            ShowInTaskbar = true;
            Background = Brushes.Transparent;
            Width = 900; Height = 720;
            MinWidth = 520; MinHeight = 460;
            // NOT Owner = owner. An owned window sits permanently ABOVE its owner, so the main
            // window could never be brought over an open dictation or sketch pad
            // (2026-08-08). The pads are free siblings: normal
            // z-order, click whichever should be on top. What Owner used to provide is done by
            // hand - centered over the main window at open, and closed with it.
            if (owner != null)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Rect r = owner.WindowState == WindowState.Maximized
                    ? SystemParameters.WorkArea
                    : new Rect(owner.Left, owner.Top,
                               owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width,
                               owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height);
                Left = r.Left + (r.Width - Width) / 2;
                Top = r.Top + (r.Height - Height) / 2;
                EventHandler ownerClosed = (_, _) => Close();
                owner.Closed += ownerClosed;
                Closed += (_, _) => owner.Closed -= ownerClosed;
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            // Text rendering, matching MainWindow.xaml:10 and FileDialog.xaml:16-17. This window set
            // neither, so its text fell back to Ideal formatting with default (grayscale) rendering
            // and came out soft next to every other window in the app.
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            // Fade in on open, like every other dialog in the app. Start transparent so the first
            // painted frame is already at zero rather than flashing the card at full opacity.
            Opacity = 0;
            Loaded += (_, _) => Anim.FadeIn(this);

            // 24 reaches past the 20px shadow halo onto the visible card edge + corners; on a
            // flat theme (halo 0) it must be thin or it swallows the caption - see the matching
            // logic in UpdateWindowCorners, which re-derives this on every theme change.
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(TryFindResource("UseDialogCaption") != null ? 4 : 24),
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
            // A capture lost to a dialog or an alt-tab never reaches CanvasUp, which would leave
            // the fist overriding the cursor app-wide for the rest of the session.
            _canvas.LostMouseCapture += (_, _) => DragCursors.EndDrag();
            _canvas.MouseLeftButtonUp += CanvasUp;
            _canvas.MouseRightButtonDown += OnCanvasRightDown;   // context menu (object vs empty canvas)
            _canvas.MouseWheel += OnWheel;                       // scroll over a placed image = its opacity
            _canvas.MouseLeave += (_, _) => HideEraseCursor();   // drop the eraser ring when the pointer leaves the canvas
            // The canvas carries an EXPLICIT size now rather than stretching to its parent: a
            // ScrollViewer measures its child with infinite space in the scrolling direction, so a
            // stretching canvas inside one collapses to nothing. GrowCanvasToViewport keeps the
            // old "resize the window, get more paper" behaviour by growing the logical size to the
            // viewport whenever the view is at 100%.
            SetCanvasSize(_canvasW, _canvasH);

            BuildUi();
            SetTool(Tool.Pen);
            UpdateUndoButtons();

            // Drag an image file (or a raw bitmap) anywhere onto the pad to drop it in at that point.
            AllowDrop = true;
            DragOver += OnDragOver;
            Drop += OnDrop;

            StateChanged += (_, _) => UpdateWindowCorners();
            Loaded += (_, _) =>
            {
                // Re-assert the card chrome (radius, halo, shadow) once real, then fit the
                // default height at ContextIdle - LOADED priority ran before layout numbers
                // were final and the fit shrank the pad to garbage (2026-08-08).
                UpdateWindowCorners();
                Dispatcher.BeginInvoke(new Action(FitDefaultHeightToRail),
                    System.Windows.Threading.DispatcherPriority.ContextIdle);
            };
            // Margins are Thickness values and cannot be resource references, so a live theme
            // switch re-applies them here; grain and the caption swap are resource-driven. The
            // rail's overflow STRUCTURE (arrows vs fades) stays from build - reopening the pad
            // after a cross-family switch picks the right one up.
            Action onThemeChanged = () =>
            {
                _flatChrome = TryFindResource("UseDialogCaption") != null;
                _contentGrid.Margin = _flatChrome ? new Thickness(3, 2, 0, 4) : new Thickness(16, 6, 16, 12);
                _railPanel?.Margin = new Thickness(0, 0, _flatChrome ? 2 : 8, 0);
                // The canvas pane's shadow follows the theme too - it was baked at build and a
                // pad opened flat stayed shadowless everywhere (2026-08-08).
                _frameShadow?.Effect = CardShadowOrNull();
                UpdateWindowCorners();   // radius + halo + shadow follow the new theme live
            };
            KillerNotes.Services.ThemeManager.ThemeChanged += onThemeChanged;
            Closed += (_, _) => KillerNotes.Services.ThemeManager.ThemeChanged -= onThemeChanged;
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
                    // Single-key tool switches, bare letters by preference. Safe: the pad has no text
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
                        case Key.C: SetTool(Tool.Crop); e.Handled = true; break;
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
            // FIT FIRST. The canvas used to be sized to the image's full pixel dimensions, so a
            // screenshot opened at 1:1 in a window clamped to the work area - you got the top-left
            // corner of it and no way to see or reach the rest (reported repeatedly, 2026-08-23).
            // Scaled down to fit, the whole image is on screen and the pad behaves like an editor
            // rather than a viewport.
            //
            // The DOWNSCALE IS REAL, not a view zoom: the drawing surface is 1:1 with the canvas
            // and Print round-trips at canvas size, so a view-only zoom would put annotations at
            // the wrong scale on the way back. An image already small enough is untouched, so
            // nothing that fits loses a single pixel.
            var fitted = FitForEditing(src);
            string? b64 = ToPngB64(fitted);
            if (b64 != null)
            {
                int w = Math.Max(1, fitted.PixelWidth), h = Math.Max(1, fitted.PixelHeight);
                _objects.Add(new SketchObject { Kind = SketchKind.Image, Img = b64, X = 0, Y = 0, W = w, H = h });
                ResizeCanvasTo(w, h);
            }
            RenderCanvas();
            UpdateUndoButtons();
        }

        // ── VIEW ZOOM ────────────────────────────────────────────────────────────────────
        //
        // A LayoutTransform on the canvas, not a RenderTransform: layout reflows so the scroller
        // gets a real extent to scroll, and the artwork re-rasterizes crisply instead of being
        // bitmap-stretched.
        //
        // Drawing coordinates need NO compensation. Every input path takes e.GetPosition(_canvas),
        // which resolves in the canvas's own untransformed space, so a stroke drawn at 400% lands
        // at the same model coordinates it would at 100%. That is the whole reason this is a view
        // zoom on the canvas and not a scale baked into the objects.
        private ScrollViewer _zoomHost = null!;
        private TextBlock _zoomReadout = null!;
        private double _zoom = 1.0;
        private const double ZoomMin = 0.1, ZoomMax = 8.0;

        private void ApplyZoom(double z)
        {
            _zoom = Math.Round(Math.Max(ZoomMin, Math.Min(ZoomMax, z)), 3);
            _canvas.LayoutTransform = _zoom == 1.0
                ? Transform.Identity
                : new System.Windows.Media.ScaleTransform(_zoom, _zoom);
            _zoomReadout?.Text = $"{Math.Round(_zoom * 100)}%";
            GrowCanvasToViewport();
        }

        // The canvas size that was ASKED for - by an opened image, a crop, or the default. The
        // viewport growth below measures from this rather than from the current size, so making
        // the window big and then small again returns to the requested size instead of leaving a
        // permanently oversized sheet with scrollbars on it.
        private int _canvasBaseW = Services.Sketch.CanvasW, _canvasBaseH = Services.Sketch.CanvasH;   // qualified, see _canvasW

        private void SetCanvasSize(int w, int h)
        {
            _canvasBaseW = _canvasW = Math.Max(1, w);
            _canvasBaseH = _canvasH = Math.Max(1, h);
            _canvas.Width = _canvasW;
            _canvas.Height = _canvasH;
        }

        /// <summary>At 100%, the logical canvas fills the viewport - the pad's original "a bigger
        /// window is a bigger sheet of paper" behaviour. Never smaller than the size that was
        /// asked for, so a window resize cannot crop artwork.</summary>
        private void GrowCanvasToViewport()
        {
            if (_zoomHost == null || _zoom != 1.0) return;
            double vw = _zoomHost.ViewportWidth, vh = _zoomHost.ViewportHeight;
            if (vw < 1 || vh < 1) return;
            int w = Math.Max(_canvasBaseW, (int)vw), h = Math.Max(_canvasBaseH, (int)vh);
            if (w == _canvasW && h == _canvasH) return;
            _canvasW = w; _canvasH = h;
            _canvas.Width = w; _canvas.Height = h;
            RenderCanvas();
        }

        /// <summary>Ctrl+wheel zooms. Without Ctrl the wheel keeps its existing meaning, which is
        /// the opacity scrub over a placed image, and otherwise the scroller's own scrolling.</summary>
        private void ZoomHost_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            // Multiplicative, so each notch is the same proportional step at every zoom level -
            // a fixed increment crawls when zoomed out and leaps when zoomed in.
            ApplyZoom(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1));
            e.Handled = true;
        }

        /// <summary>The magnifier button: 100% if the view is zoomed, otherwise fit the whole
        /// canvas into the viewport. One button covers both things you ever want from a zoom
        /// control, and the readout beside it says which state you are in.</summary>
        private void ToggleZoomFit()
        {
            if (_zoom != 1.0) { ApplyZoom(1.0); return; }
            if (_zoomHost == null || _canvasW < 1 || _canvasH < 1) return;
            double vw = _zoomHost.ViewportWidth, vh = _zoomHost.ViewportHeight;
            if (vw < 1 || vh < 1) return;
            ApplyZoom(Math.Min(vw / _canvasW, vh / _canvasH));
        }

        /// <summary>Scales an image down until it fits the working area with room for the pad's
        /// own chrome, leaving anything already small enough exactly as it is. High quality
        /// resampling, because this result IS the artwork from here on, not a preview.</summary>
        private static BitmapSource FitForEditing(BitmapSource src)
        {
            var area = SystemParameters.WorkArea;
            // Room for the toolbar, the button row, the borders and the shadow halo, plus a
            // margin so the window is not jammed against the screen edges.
            double maxW = Math.Max(320, area.Width - 220);
            double maxH = Math.Max(240, area.Height - 260);
            double scale = Math.Min(maxW / Math.Max(1, src.PixelWidth),
                                    maxH / Math.Max(1, src.PixelHeight));
            if (scale >= 1.0) return src;

            var t = new TransformedBitmap();
            t.BeginInit();
            t.Source = src;
            t.Transform = new System.Windows.Media.ScaleTransform(scale, scale);
            t.EndInit();
            t.Freeze();
            return t;
        }

        // Grow / shrink the window so its drawing canvas lands at (targetW, targetH), restoring a
        // reopened sketch to the size it was drawn. Deferred until layout has run so the canvas and
        // chrome sizes are current; clamped to the screen work area and the window minimums.
        private void ResizeCanvasTo(int targetW, int targetH)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (WindowState == WindowState.Maximized) return;   // don't fight a maximized window
                // Measured against the SCROLLER's viewport, not the canvas: the canvas is centered
                // in it and may be smaller, and counting that letterbox as chrome sized the window
                // larger every time an image was opened.
                double cw = _zoomHost?.ViewportWidth ?? _canvas.ActualWidth;
                double ch = _zoomHost?.ViewportHeight ?? _canvas.ActualHeight;
                if (cw < 1 || ch < 1) return;
                double chromeW = ActualWidth - cw, chromeH = ActualHeight - ch;   // borders, toolbar, buttons, shadow halo
                var area = SystemParameters.WorkArea;
                Width = Math.Max(MinWidth, Math.Min(area.Width, targetW + chromeW));
                Height = Math.Max(MinHeight, Math.Min(area.Height, targetH + chromeH));
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ---- Close fade ----
        // Cancel the first close, fade out, then close for real - the same pattern the main
        // window uses in Notes.EmptyState.cs, shared through Anim.FadeOutAndClose.
        private bool _closeFaded;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (Anim.FadeOutAndClose(this, ref _closeFaded)) { e.Cancel = true; return; }
            base.OnClosing(e);
        }

        // The family PaneShadow, identical to the one ApplyThemeElevation puts on the main
        // content pane (blur 16, depth 5, direction 270, opacity .60) so the SketchPad card sits
        // at the same elevation as the pane it floats over. ShadowDepth must NOT be 0: that
        // spreads an even halo on all four sides instead of casting downward.
        // Opacity follows the theme's PaneShadowOpacity, which is how 98SE stays flat.
        /// <summary>CardShadow, or NULL when the theme's PaneShadowOpacity is 0 - never attach
        /// an invisible effect object (see the note in UpdateWindowCorners).</summary>
        private static DropShadowEffect? CardShadowOrNull()
            => Application.Current.TryFindResource("PaneShadowOpacity") is double o && o > 0
                ? CardShadow() : null;

        private static DropShadowEffect CardShadow()
        {
            double opacity = Application.Current.TryFindResource("PaneShadowOpacity") is double o ? o : 0.60;
            return new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 16,
                ShadowDepth = 5,
                Direction = 270,
                Opacity = opacity,
                RenderingBias = RenderingBias.Quality
            };
        }

        /// <summary>The card's corner radius, from the theme rather than a hardcoded 7 - a
        /// square-cornered theme gets square corners, and its square bevel then meets the card
        /// edge instead of cutting across a rounded one.</summary>
        private static CornerRadius CardRadius() =>
            Application.Current.TryFindResource("WindowCornerRadius") is CornerRadius r ? r : new CornerRadius(7);

        // Rounded floating card normally; squared off and flush (no halo / shadow) when maximized.
        private void UpdateWindowCorners()
        {
            bool max = WindowState == WindowState.Maximized;
            bool flat = Application.Current.TryFindResource("UseDialogCaption") != null;
            _outerBorder.CornerRadius = max ? new CornerRadius(0) : CardRadius();
            // Halo 0 when maximized OR flat. 98SE declares DialogHaloMargin 0 - a flat window sits
            // FLUSH - but the pads hardcoded 20, leaving a phantom transparent band outside the
            // frame where shadow residue rendered ("the shadow came back in 98SE", 2026-08-08).
            // The dialogs honor the key, which is why THEY looked right on 98SE and the pads did
            // not. Normal themes keep 20: the pads' CardShadow needs more room than the dialogs'
            // themed 10px halo.
            _outerBorder.Margin = max || flat ? new Thickness(0) : new Thickness(20);
            // NULL, never an opacity-0 effect: an Effect object always costs an offscreen surface
            // and is one renderer quirk from ghosting; absence cannot ghost.
            _outerBorder.Effect = max || flat ? null : CardShadowOrNull();
            // The resize grab must TRACK the halo. 24 is sized to reach across the normal 20px
            // transparent halo onto the visible card edge - but on a flat theme the halo is 0,
            // so all 24px sit ON the window and swallow the entire 20px caption: no drag, no
            // close X ("i cant click the titlebar... cant even close it", 2026-08-08). 4 is the
            // Win98-style thin frame grab; the caption below it drags and the X clicks.
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(flat ? 4 : 24),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });
            _grainBorder?.CornerRadius = max ? new CornerRadius(0) : CardRadius();
            // The close X no longer has a corner to square off: it is a bare glyph now, not a
            // filled block hugging the window corner, so there is nothing here to follow the card.
        }
    }
}
