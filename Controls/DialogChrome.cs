using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

// Shared title-bar chrome for the floating dialogs (1.2.0).
//
// WHY THIS FILE EXISTS. The wordmark and the close X were hand-rolled once per dialog, and three
// copies drifted into three different windows:
//   - SketchPad  wordmark + "SketchPad" subtitle, and a 46x36 close button that filled SOLID RED
//                with a white glyph on hover (the Windows caption treatment).
//   - Databases  the wordmark, no subtitle - its title went in the BODY as a heading instead.
//   - Dictation  no wordmark at all, just the word "Dictation", and a third close-button build.
// Each carried a comment claiming to be the family rule, and they contradicted each other.
//
// The rule, settled: WORDMARK + SUBTITLE to its right, and a GLYPH-ONLY close that reddens on
// hover. No filled block - that is the OS caption treatment and reads wrong on a floating card,
// which is what the Dictation copy always said. Everything builds its caption from here now, so
// the next dialog cannot drift into a fourth variant.
namespace KillerNotes.Controls
{
    internal static class DialogChrome
    {
        private static object? Res(string key) => Application.Current?.TryFindResource(key);

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
        /// other - which is how this first shipped, measuring 3px instead of 5 with the inner ring
        /// invisible.
        ///
        /// Every key is transparent at zero thickness by default, so a window that adds this on a
        /// flat theme draws nothing and looks exactly as it did, so every other window can take
        /// the same treatment. (2026-08-07)
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

        /// <summary>
        /// "KillerNotes <subtitle>" in the wordmark face, over a blurred dark copy of itself.
        ///
        /// ChromeTextBrush and not TextBrush for the "Killer" half: a caption band is a dark
        /// gradient on several themes, and TextBrush is the color calibrated for the CONTENT
        /// surface (black on 98SE), which vanished against it.
        /// </summary>
        public static UIElement Wordmark(string subtitle)
        {
            // BOTH captions are ALWAYS built and swapped by WordmarkVisibility /
            // PlainTitleVisibility - the same DynamicResource pair MainWindow.xaml binds. The
            // comment here CLAIMED that while the code still picked one at build time with a C#
            // "if" on UseDialogCaption: a pad opened under a wordmark theme then kept the
            // typewriter logotype painted over 98SE's caption after a live theme switch, on top
            // of the plain title (2026-08-08). Now the code matches the claim.
            var host = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };

            // -- Flat caption: icon + bold plain title, the SAME keys the main window's caption
            //    uses (TitleIconSize, TitleIconMargin, ChromeFontFamily, ChromeTextBrush).
            var flatRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            flatRow.SetResourceReference(UIElement.VisibilityProperty, "PlainTitleVisibility");
            var icon = new Image
            {
                Source = new System.Windows.Media.Imaging.BitmapImage(
                    new System.Uri("pack://application:,,,/Resources/kn-icon.png")),
                VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            icon.SetResourceReference(FrameworkElement.WidthProperty, "TitleIconSize");
            icon.SetResourceReference(FrameworkElement.HeightProperty, "TitleIconSize");
            icon.SetResourceReference(FrameworkElement.MarginProperty, "TitleIconMargin");
            flatRow.Children.Add(icon);
            var plain = new TextBlock
            {
                Text = subtitle.Length > 0 ? "KillerNotes - " + subtitle : "KillerNotes",
                FontSize = 11,
                FontWeight = FontWeights.Bold,   // matches MainWindow.xaml:116 exactly
                VerticalAlignment = VerticalAlignment.Center,
            };
            plain.SetResourceReference(TextBlock.FontFamilyProperty, "ChromeFontFamily");
            plain.SetResourceReference(TextBlock.ForegroundProperty, "ChromeTextBrush");
            flatRow.Children.Add(plain);
            host.Children.Add(flatRow);

            // -- Wordmark caption: shadow copy first, offset a pixel and blurred, so the mark
            //    lifts off the band.
            var wf = Res("WordmarkFont") as FontFamily;
            var mark = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            mark.SetResourceReference(UIElement.VisibilityProperty, "WordmarkVisibility");
            var shadowInk = new SolidColorBrush(Color.FromArgb(0xD8, 0, 0, 0));
            var shadow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, 2, 0, 0) };
            if (Res("IconShadowOpacity") is double sop) shadow.Opacity = sop;
            shadow.Effect = new BlurEffect { Radius = 3 };
            shadow.Children.Add(Text(wf, subtitle, "", "", "", shadowInk));
            mark.Children.Add(shadow);

            // The subtitle takes ChromeTextBrush too, NOT MutedTextBrush. Muted is a color mixed
            // against the CONTENT surface; on a theme that paints a real caption band (98SE's green)
            // it lands dark-on-dark and the subtitle disappeared while "KillerNotes" beside it
            // stayed perfectly readable. Chrome text is the band's own color by definition. The
            // subtitle still reads as secondary because it is lighter weight and a size down.
            mark.Children.Add(Text(wf, subtitle, "ChromeTextBrush", "AccentLogo", "ChromeTextBrush"));
            host.Children.Add(mark);

            return host;
        }

        private static TextBlock Text(FontFamily? wf, string subtitle,
                                      string killerKey, string notesKey, string subKey, Brush? flat = null)
        {
            // Sizes come from the SAME two keys the main window's wordmark uses. They were
            // hardcoded 15 / 19.5 / 18 against the main window's 17 / 22, which is why a dialog
            // caption never matched it on any theme that shows the wordmark.
            var tb = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            tb.SetResourceReference(TextBlock.FontSizeProperty, "TitleWordmarkSize");
            if (wf != null) tb.FontFamily = wf;

            var killer = new Run("Killer");
            var notes = new Run("Notes") { FontWeight = FontWeights.Bold };
            notes.SetResourceReference(TextElement.FontSizeProperty, "TitleWordmarkBoldSize");
            var sub = new Run(subtitle.Length > 0 ? "  " + subtitle : "");
            sub.SetResourceReference(TextElement.FontSizeProperty, "TitleWordmarkSize");

            if (flat != null) { killer.Foreground = flat; notes.Foreground = flat; sub.Foreground = flat; }
            else
            {
                killer.SetResourceReference(TextElement.ForegroundProperty, killerKey);
                notes.SetResourceReference(TextElement.ForegroundProperty, notesKey);
                sub.SetResourceReference(TextElement.ForegroundProperty, subKey);
            }

            tb.Inlines.Add(killer);
            tb.Inlines.Add(notes);
            tb.Inlines.Add(sub);
            return tb;
        }

        // ---- Shared corner resize grip (SketchPad, Dictation) ----
        // Classic grip in the bottom-right corner - a REAL handle: pressing it starts an
        // OS-driven bottom-right corner resize. The windows' own resize border lives out in
        // the transparent shadow halo (easy to miss), so the visible grip is the reliable way.
        // ONE builder for every resizable pad, so the grips cannot drift apart: SketchPad had
        // its own copy and Dictation had nothing (2026-08-08). TWO looks in one 18x18
        // slot, each shown by its own visibility key - the family's six 2x2 dots, or the Win98
        // diagonal beveled bands on a flat theme.
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
            hatch.SetResourceReference(UIElement.VisibilityProperty, "GripHatchVisibility");
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

        /// <summary>
        /// The family close X: the GLYPH reddens on hover, nothing fills.
        ///
        /// A TextBlock, not a Button. A Button with Background="Transparent" still carries WPF's
        /// default template, which paints the system highlight behind it on hover - that is the
        /// gray block no brush or theme reaches.
        ///
        /// PreviewMouseLeftButtonDown, NOT MouseLeftButtonUp: the caption band under this handles
        /// ButtonDown to drag the window, DragMove() runs a modal loop, and the matching ButtonUp
        /// then never arrives here - which is how a close X ends up doing nothing. Tunnelling gets
        /// it first, and marking it handled stops a drag starting on a click aimed at the X.
        /// </summary>
        public static FrameworkElement CloseGlyph(string tooltip, Action onClose)
        {
            // A real Button on the shared close style, not a bare TextBlock: it carries the caption
            // face and the raised bevel, so on a Win98-style theme this reads as an actual caption
            // button instead of a glyph floating on the band. On every other theme
            // CaptionButtonBrush is transparent and the margin is 0, so it looks exactly as it did.
            // ChromeCloseButton: these X's sit at the caption's corner end, so the hover block
            // rounds with the card's top-right corner like every dialog caption
            // (2026-08-08); DialogCloseButton's all-round block is only for floating X's.
            var glyph = new Button
            {
                // NO Content. ChromeCloseButton's template draws the glyph itself - one definition
                // in Controls.xaml for the whole app - so setting content here would only be a
                // seventh copy to drift out of step.
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,          // Transparent, not null: null is not hit-testable
                Cursor = Cursors.Hand,
                ToolTip = tooltip,
            };
            if (Application.Current?.TryFindResource("ChromeCloseButton") is Style s) glyph.Style = s;
            // DialogCloseWidth/Height, the DIALOG-band size keys (ThemeManager), vertically
            // CENTERED in the band. The main window's CaptionButtonWidth/Height (44x36) belongs
            // to the 36px main bar; in a 28px dialog band it overflowed and the hover block
            // smothered the card's rounded corner (2026-08-08). On a flat theme the keys
            // resolve to the real caption-button size, so 98SE is unchanged.
            glyph.SetResourceReference(FrameworkElement.WidthProperty, "DialogCloseWidth");
            glyph.SetResourceReference(FrameworkElement.HeightProperty, "DialogCloseHeight");
            // DialogCaptionButtonsMargin, not CaptionButtonsMargin. The main window's key carries a
            // TOP inset because the window frame overlay paints over the first few pixels of its
            // band; a dialog band has no such overlay, so that inset just pushed the X down and it
            // sat visibly low. The dialog key is the same RIGHT inset with the top zeroed, which is
            // all a vertically-centered button needs. (2026-08-07)
            glyph.SetResourceReference(FrameworkElement.MarginProperty, "DialogCaptionButtonsMargin");
            glyph.PreviewMouseLeftButtonDown += (_, e) => { e.Handled = true; onClose(); };
            return glyph;
        }
    }
}
