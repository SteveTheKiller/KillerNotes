using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace KillerNotes.Controls
{
    // ============================================================
    // Corner-handle resize adorner. Accent-colored frame + 4 corner
    // squares; dragging a corner sets Image.Width (Stretch=Uniform
    // keeps the aspect), clamped between 40 DIPs and the image's
    // natural size - display-only, the bitmap is never resampled.
    // ============================================================
    internal sealed class ImageResizeAdorner(Image img, Func<double> maxWidth) : Adorner(img)
    {
        private const double Handle = 7;    // visible square
        private const double HitPad = 16;   // invisible hit target around each corner

        private readonly Image _img = img;
        private readonly Func<double> _maxWidth = maxWidth;   // live cap (pane width while wrap is on)
        private bool _dragging;
        private int _corner = -1;           // 0 TL, 1 TR, 2 BL, 3 BR
        private Point _start;
        private double _startWidth;
        private double _pendingW;           // target width during a drag; applied once on release
        private bool _previewing;           // a RenderTransform preview is live (no reflow yet)

        public event Action? Resized;
        public event Action? DismissRequested;

        // While a resize is previewed (RenderTransform only, no layout change yet), draw the frame and
        // handles at the scaled size so the box tracks the image; 1.0 when not previewing.
        private double PreviewScale => _previewing && _startWidth > 0 ? _pendingW / _startWidth : 1.0;

        private Point[] Corners()
        {
            double s = PreviewScale;
            double w = _img.ActualWidth * s, h = _img.ActualHeight * s;
            return [new Point(0, 0), new Point(w, 0), new Point(0, h), new Point(w, h)];
        }

        private int CornerAt(Point p)
        {
            var c = Corners();
            for (int i = 0; i < 4; i++)
                if (Math.Abs(p.X - c[i].X) <= HitPad / 2 && Math.Abs(p.Y - c[i].Y) <= HitPad / 2)
                    return i;
            return -1;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var accent = Application.Current.TryFindResource("PrimaryBrush") as Brush
                         ?? Brushes.MediumPurple;
            double s = PreviewScale;
            dc.DrawRectangle(null, new Pen(accent, 1.5),
                new Rect(0, 0, _img.ActualWidth * s, _img.ActualHeight * s));
            foreach (var c in Corners())
            {
                // Transparent square = the generous hit target; accent square = the visual.
                dc.DrawRectangle(Brushes.Transparent, null,
                    new Rect(c.X - HitPad / 2, c.Y - HitPad / 2, HitPad, HitPad));
                dc.DrawRectangle(accent, null,
                    new Rect(c.X - Handle / 2, c.Y - Handle / 2, Handle, Handle));
            }
        }

        // A drag reference that does NOT move while the inline image reflows mid-resize: the adorner
        // layer covers the editor and stays put, so the mouse delta can't feed back on itself (which
        // made the image lurch to the side). CornerAt still uses adorner-local coords (0,0 .. w,h).
        private UIElement ResizeRef => AdornerLayer.GetAdornerLayer(AdornedElement) ?? (UIElement)AdornedElement;

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            _corner = CornerAt(e.GetPosition(this));
            if (_corner < 0) { DismissRequested?.Invoke(); return; }

            _dragging = true;
            _start = e.GetPosition(ResizeRef);
            _startWidth = _img.ActualWidth;
            _pendingW = _startWidth;
            _previewing = false;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging)
            {
                int c = CornerAt(e.GetPosition(this));
                Cursor = c switch
                {
                    0 or 3 => Cursors.SizeNWSE,
                    1 or 2 => Cursors.SizeNESW,
                    _      => null,
                };
                return;
            }

            // Right-side corners grow with +dx, left-side with -dx.
            double dx = e.GetPosition(ResizeRef).X - _start.X;
            if (_corner is 0 or 2) dx = -dx;

            // Cap at the natural size (never upscale-blur) and, while word wrap is on, at the
            // editor pane width so an image can't be dragged wider than the wrap edge - past
            // there it would clip with no horizontal scroll to reach it. (2026-07-22)
            // Natural = the bitmap's PIXEL width, never ImageSource.Width. Width is DIPs scaled
            // by the file's DPI metadata: a photo stamped 300dpi reports a Width a third of its
            // pixels, so the cap sat far below the real resolution - the first drag snapped the
            // image down to that false cap and nothing could grow it back (demo mode,
            // 2026-08-08). PixelWidth is the true no-upscale limit.
            double natural = _img.Source is System.Windows.Media.Imaging.BitmapSource bmp
                ? bmp.PixelWidth
                : (_img.Source?.Width ?? double.MaxValue);
            double cap = Math.Min(natural, _maxWidth());
            double newW = Math.Max(40, Math.Min(cap, _startWidth + dx));

            // Preview only: scale the image visually with a RenderTransform (aspect-locked, anchored
            // top-left). This changes NOTHING about layout, so neighbors don't reflow and a side-by-side
            // image can't hop around mid-drag. The real Width - and the single reflow - lands on release.
            _pendingW = newW;
            _previewing = true;
            double scale = newW / Math.Max(1, _startWidth);
            if (_img.RenderTransform is ScaleTransform st) { st.ScaleX = scale; st.ScaleY = scale; }
            else { _img.RenderTransformOrigin = new Point(0, 0); _img.RenderTransform = new ScaleTransform(scale, scale); }
            InvalidateVisual();   // redraw the frame + handles at the previewed size (the box tracks the image)
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            _corner = -1;
            ReleaseMouseCapture();
            if (_previewing)
            {
                _previewing = false;
                _img.RenderTransform = null;                        // drop the preview scale
                _img.ClearValue(FrameworkElement.MaxWidthProperty); // manual size takes over from the auto-fit cap
                _img.Width = _pendingW;                             // the one and only reflow
            }
            InvalidateVisual();   // redraw the box at the committed (actual) size
            Resized?.Invoke();
            e.Handled = true;
        }
    }
}
