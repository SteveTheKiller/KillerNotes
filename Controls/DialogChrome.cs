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
        /// flat theme draws nothing and looks exactly as it did. (Steve, 2026-08-07: "we need to
        /// repeat the whole process for all the other windows".)
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
        /// gradient on several themes, and TextBrush is the colour calibrated for the CONTENT
        /// surface (black on 98SE), which vanished against it.
        /// </summary>
        public static UIElement Wordmark(string subtitle)
        {
            // BOTH captions are built and their Visibility is bound to WordmarkVisibility /
            // PlainTitleVisibility - exactly what MainWindow.xaml does. It used to be a C# "if" on
            // UseDialogCaption, which is evaluated ONCE when the window is constructed: a dialog
            // opened under one theme kept that theme's caption after a live theme switch while the
            // main window swapped, which is why the two fonts disagreed. DynamicResource makes it
            // reactive, so the dialogs and the main window can no longer drift.
            if (Res("UseDialogCaption") != null)
            {
                // Icon + bold title, built from the SAME keys the main window's caption uses
                // (TitleIconSize, TitleIconMargin, ChromeFontFamily, ChromeTextBrush).
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };

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
                row.Children.Add(icon);

                var plain = new TextBlock
                {
                    Text = subtitle.Length > 0 ? "KillerNotes - " + subtitle : "KillerNotes",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,   // matches MainWindow.xaml:116 exactly
                    VerticalAlignment = VerticalAlignment.Center,
                };
                plain.SetResourceReference(TextBlock.FontFamilyProperty, "ChromeFontFamily");
                plain.SetResourceReference(TextBlock.ForegroundProperty, "ChromeTextBrush");
                row.Children.Add(plain);
                return row;
            }

            var wf = Res("WordmarkFont") as FontFamily;
            var host = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };

            // Shadow copy first, offset a pixel and blurred, so the mark lifts off the band.
            var shadowInk = new SolidColorBrush(Color.FromArgb(0xD8, 0, 0, 0));
            var shadow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(1, 2, 0, 0) };
            if (Res("IconShadowOpacity") is double sop) shadow.Opacity = sop;
            shadow.Effect = new BlurEffect { Radius = 3 };
            shadow.Children.Add(Text(wf, subtitle, "", "", "", shadowInk));
            host.Children.Add(shadow);

            // The subtitle takes ChromeTextBrush too, NOT MutedTextBrush. Muted is a colour mixed
            // against the CONTENT surface; on a theme that paints a real caption band (98SE's green)
            // it lands dark-on-dark and the subtitle disappeared while "KillerNotes" beside it
            // stayed perfectly readable. Chrome text is the band's own colour by definition. The
            // subtitle still reads as secondary because it is lighter weight and a size down.
            host.Children.Add(Text(wf, subtitle, "ChromeTextBrush", "AccentLogo", "ChromeTextBrush"));
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

        /// <summary>
        /// The family close X: the GLYPH reddens on hover, nothing fills.
        ///
        /// A TextBlock, not a Button. A Button with Background="Transparent" still carries WPF's
        /// default template, which paints the system highlight behind it on hover - that is the
        /// grey block no brush or theme reaches.
        ///
        /// PreviewMouseLeftButtonDown, NOT MouseLeftButtonUp: the caption band under this handles
        /// ButtonDown to drag the window, DragMove() runs a modal loop, and the matching ButtonUp
        /// then never arrives here - which is how a close X ends up doing nothing. Tunnelling gets
        /// it first, and marking it handled stops a drag starting on a click aimed at the X.
        /// </summary>

        public static FrameworkElement CloseGlyph(string tooltip, Action onClose)
        {
            // A real Button on the shared ChromeCloseButton style, not a bare TextBlock: that style
            // carries the caption face and the raised bevel, so on a Win98-style theme this reads as
            // an actual caption button instead of a glyph floating on the band. On every other theme
            // CaptionButtonBrush is transparent and the margin is 0, so it looks exactly as it did.
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
            // EXACTLY the main window's caption button: same size keys, same strip margin, and
            // vertically CENTRED in the band. It used to be top-aligned on the card with its own
            // AboutClose* keys, which is why it had no gap above it and never matched however many
            // times the margin was nudged. The centring is what produces the 1px band above and
            // below, because CaptionButtonHeight is shorter than TitleBarHeight.
            glyph.SetResourceReference(FrameworkElement.WidthProperty, "CaptionButtonWidth");
            glyph.SetResourceReference(FrameworkElement.HeightProperty, "CaptionButtonHeight");
            // DialogCaptionButtonsMargin, not CaptionButtonsMargin. The main window's key carries a
            // TOP inset because the window frame overlay paints over the first few pixels of its
            // band; a dialog band has no such overlay, so that inset just pushed the X down and it
            // sat visibly low. The dialog key is the same RIGHT inset with the top zeroed, which is
            // all a vertically-centred button needs. (Steve, 2026-08-07.)
            glyph.SetResourceReference(FrameworkElement.MarginProperty, "DialogCaptionButtonsMargin");
            glyph.PreviewMouseLeftButtonDown += (_, e) => { e.Handled = true; onClose(); };
            return glyph;
        }
    }
}
