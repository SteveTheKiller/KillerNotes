using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// KillerNotes' own window-chrome helpers for its floating windows (SketchPad, Dictation, the
// Whisper model dialog, GraphWindow, the color picker). The family DialogChrome.cs is the kit's
// file and stays byte-identical to KillerUI; these are the pieces it does not ship:
//   - the grain under a caption band,
//   - the 5px raised window frame and the inset that keeps content off it,
//   - the corner resize grip,
namespace KillerNotes.Controls
{
    internal static class DialogFrame
    {
        internal static Border TitleGrain()
        {
            var grain = new Border { IsHitTestVisible = false };
            grain.SetResourceReference(Border.BackgroundProperty, "GrainTileBrush");
            grain.SetResourceReference(UIElement.OpacityProperty, "GrainOpacity");
            return grain;
        }

        internal static Grid GrainedTitle(UIElement content)
        {
            var host = new Grid();
            host.Children.Add(TitleGrain());
            host.Children.Add(content);
            return host;
        }

        /// <summary>
        /// THE window frame - the 5px sizing border, built once and dropped into any window's root
        /// Grid as its last child. Callers also set their content's Margin to WindowFramePadding so
        /// the frame occupies its own space instead of painting over the caption and the footer.
        ///
        /// Structure is a raised double bevel wrapping a face, exactly what Win32 draws for
        /// EDGE_RAISED on a sizing border:
        ///
        ///     outer ring   FrameOuterLight top/left   FrameOuterDark bottom/right
        ///     face         WindowFrameBrush, inset by WindowFrameMargin
        ///     inner ring   FrameInnerLight top/left   FrameInnerDark bottom/right
        ///
        /// Each ring MUST be inset past the one outside it. A Border draws its edge INSIDE its own
        /// bounds, so rings with no margin all anchor to the same pixel and simply paint over each
        /// other.
        ///
        /// Every key is transparent at zero thickness by default, so a window that adds this on a
        /// flat theme draws nothing and looks exactly as it did.
        /// </summary>
        public static UIElement WindowFrame()
        {
            var host = new Grid { IsHitTestVisible = false };
            host.Children.Add(Ring("WindowFrameBrush", "WindowFrameThickness", "WindowFrameMargin"));
            host.Children.Add(Ring("FrameInnerLightBrush", "FrameInnerLightThickness", "FrameInnerMargin"));
            host.Children.Add(Ring("FrameInnerDarkBrush", "FrameInnerDarkThickness", "FrameInnerMargin"));
            host.Children.Add(Ring("FrameOuterLightBrush", "FrameOuterLightThickness", null));
            host.Children.Add(Ring("FrameOuterDarkBrush", "FrameOuterDarkThickness", null));
            return host;
        }

        private static Border Ring(string brushKey, string thicknessKey, string? marginKey)
        {
            var b = new Border { IsHitTestVisible = false };
            b.SetResourceReference(Border.BorderBrushProperty, brushKey);
            b.SetResourceReference(Border.BorderThicknessProperty, thicknessKey);
            if (marginKey != null) b.SetResourceReference(FrameworkElement.MarginProperty, marginKey);
            return b;
        }

        /// <summary>Holds a window's content off the edge by the frame's width. Zero on a theme
        /// with no frame, so nothing moves.</summary>
        public static void InsetForFrame(FrameworkElement content) =>
            content.SetResourceReference(FrameworkElement.MarginProperty, "WindowFramePadding");

        internal static void SharpenTitleIcon(Image icon)
        {
            var frames = BitmapDecoder.Create(new Uri("pack://application:,,,/Resources/kn-icon.ico"),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames;
            icon.Source = frames[0];
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.NearestNeighbor);
        }

        // Classic grip in the bottom-right corner - a REAL handle: pressing it starts an
        // OS-driven bottom-right corner resize. ONE builder for every resizable pad, so the grips
        // cannot drift apart. TWO looks in one 18x18 slot, each shown by its own visibility key:
        // the family's six 2x2 dots, or the Win98 diagonal beveled bands on a flat theme.
        public static UIElement ResizeGrip(Window owner)
        {
            var c = new System.Windows.Controls.Canvas
            {
                Width = 18, Height = 18,
                Background = System.Windows.Media.Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.SizeNWSE,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 3, 3),
            };
            var dots = new System.Windows.Controls.Canvas { Width = 18, Height = 18, IsHitTestVisible = false };
            dots.SetResourceReference(UIElement.VisibilityProperty, "GripDotsVisibility");
            void Dot(double x, double y)
            {
                var d = new System.Windows.Shapes.Ellipse { Width = 2.4, Height = 2.4 };
                d.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "MutedTextBrush");
                System.Windows.Controls.Canvas.SetLeft(d, x);
                System.Windows.Controls.Canvas.SetTop(d, y);
                dots.Children.Add(d);
            }
            Dot(15, 6);
            Dot(10.5, 10.5); Dot(15, 10.5);
            Dot(6, 15); Dot(10.5, 15); Dot(15, 15);
            c.Children.Add(dots);

            // Diagonal bands: half-pixel centers so a 1px line lands on one row, each band a
            // dark line with a light one under it - the beveled hatch, not plain strokes.
            var hatch = new System.Windows.Controls.Canvas { Width = 18, Height = 18, IsHitTestVisible = false };
            hatch.SetResourceReference(UIElement.VisibilityProperty, "GripLinesVisibility");
            void Band(double off, string brushKey)
            {
                var l = new System.Windows.Shapes.Line { X1 = 16.5, Y1 = off, X2 = off, Y2 = 16.5, StrokeThickness = 1 };
                l.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brushKey);
                hatch.Children.Add(l);
            }
            Band(5.5, "BevelDarkBrush");  Band(6.5, "BevelLightBrush");
            Band(9.5, "BevelDarkBrush");  Band(10.5, "BevelLightBrush");
            Band(13.5, "BevelDarkBrush"); Band(14.5, "BevelLightBrush");
            c.Children.Add(hatch);

            c.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (owner.WindowState == WindowState.Maximized) return;
                var hwnd = new System.Windows.Interop.WindowInteropHelper(owner).Handle;
                if (hwnd == IntPtr.Zero) return;
                ReleaseCapture();
                SendMessage(hwnd, 0x00A1 /* WM_NCLBUTTONDOWN */, (IntPtr)17 /* HTBOTTOMRIGHT */, IntPtr.Zero);
            };
            return c;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
    }
}
