using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using KillerFamily.Controls;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class FamilyMenuAppearanceTests
    {
        [Fact]
        public void FontsActionUsesTheLiveMenuFont() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            var app = Application.Current;
            var menu = (ContextMenu)window.FindName("ThemeMenu");
            bool local = app.Resources.Keys.Cast<object>().Contains("MenuFontFamily");
            var previous = app.FindResource("MenuFontFamily");
            var target = new Button();
            var owner = new Window { Content = target, Width = 160, Height = 80, ShowInTaskbar = false };
            owner.Show();
            try
            {
                ((Button)window.FindName("ThemeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                menu.IsOpen = false;
                menu.PlacementTarget = target;
                menu.Placement = PlacementMode.Bottom;
                menu.CustomPopupPlacementCallback = null;
                menu.IsOpen = true;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                string name = (string)app.FindResource("Str_Fonts_Open");
                var label = menu.Items.OfType<Button>().SelectMany(button => Descendants(button))
                    .OfType<TextBlock>().Single(text => text.Text == name);
                Assert.Equal(((FontFamily)previous).Source, label.FontFamily.Source);

                app.Resources["MenuFontFamily"] = new FontFamily("Arial");
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.Equal("Arial", label.FontFamily.Source);
            }
            finally
            {
                menu.IsOpen = false;
                owner.Close();
                if (local) app.Resources["MenuFontFamily"] = previous;
                else app.Resources.Remove("MenuFontFamily");
            }
        });

        // Family rule: the theme rows, accent pills, Film Grain and Fonts sit 8px from the card's
        // outer edge on every side. One device pixel of rounding is allowed at a fractional scale.
        [Fact]
        public void ThemeMenuRowsAndPillsSitEightPixelsFromEveryCardEdge() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            var menu = (ContextMenu)window.FindName("ThemeMenu");
            var target = new Button();
            var owner = new Window { Content = target, Width = 160, Height = 80, ShowInTaskbar = false };
            owner.Show();
            try
            {
                ((Button)window.FindName("ThemeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                menu.IsOpen = false;
                menu.PlacementTarget = target;
                menu.Placement = PlacementMode.Bottom;
                menu.CustomPopupPlacementCallback = null;
                menu.IsOpen = true;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                menu.UpdateLayout();
                var all = VisualDescendants(menu).OfType<FrameworkElement>().Where(e => e.IsVisible && e.ActualWidth > 0).ToList();
                var card = all.First(e => e.Margin == new Thickness(22, 18, 22, 26));
                Rect Bounds(FrameworkElement e) => e.TransformToAncestor(card).TransformBounds(new Rect(e.RenderSize));
                var dotStyle = (Style)Application.Current.FindResource("AccentDot");
                var parts = new List<Rect>();
                int pills = 0, fonts = 0;
                foreach (var e in all)
                {
                    if (e is ToggleButton toggle && toggle.Template?.FindName("bd", toggle) is FrameworkElement hover) parts.Add(Bounds(hover));
                    else if (e is Border dot && dot.Style == dotStyle) { parts.Add(Bounds(dot)); pills++; }
                    else if (e is Button button && button.Style == Application.Current.FindResource("MenuRowButton")) { parts.Add(Bounds(button)); fonts++; }
                }
                double left = parts.Min(r => r.Left), top = parts.Min(r => r.Top);
                double right = card.ActualWidth - parts.Max(r => r.Right), bottom = card.ActualHeight - parts.Max(r => r.Bottom);
                double pixel = 1 / VisualTreeHelper.GetDpi(card).DpiScaleX;
                string measured = $"L{left:0.##} T{top:0.##} R{right:0.##} B{bottom:0.##}, {pills} pills, {fonts} fonts rows";
                Assert.True(pills == 8 && fonts == 1, measured);
                foreach (double gap in new[] { left, top, right, bottom })
                    Assert.True(Math.Abs(gap - 8) <= pixel + 0.01, measured);
            }
            finally
            {
                menu.IsOpen = false;
                owner.Close();
            }
        });

        // Measured from rendered pixels in three themes: every ring's ink centers on its label's first
        // capital within half a pixel, the divider has the same gap to the hover box on each side, and
        // the hover rows sit 8px from every card edge. Set LANG_MENU_SHOTS to a folder to keep the renders.
        [Theory]
        [InlineData(Theme.TrashPolka)]
        [InlineData(Theme.Dark)]
        [InlineData(Theme.SE98)]
        public void LanguageMenuRingsDividerAndEdgesMeasureRightInPixels(Theme theme) => CardColorBehaviorTests.WithShell((window, shellHost) =>
        {
            var app = Application.Current;
            app.Resources.MergedDictionaries[0] = ThemeHarness.Create(theme, Accent.Red);
            var menu = (ContextMenu)window.FindName("LangMenu");
            var target = new Button();
            // The menu inherits layout rounding from its button; the stand-in window rounds as MainWindow does.
            var owner = new Window { Content = target, Width = 160, Height = 80, ShowInTaskbar = false, UseLayoutRounding = window.UseLayoutRounding };
            owner.Show();
            try
            {
                ((Button)window.FindName("LangButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                menu.IsOpen = false;
                menu.PlacementTarget = target;
                menu.Placement = PlacementMode.Bottom;
                menu.CustomPopupPlacementCallback = null;
                menu.IsOpen = true;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                foreach (var element in VisualDescendants(menu).OfType<FrameworkElement>().Prepend(menu))
                    element.BeginAnimation(UIElement.OpacityProperty, null);
                var card = VisualDescendants(menu).OfType<FrameworkElement>().First(e => e.Margin == new Thickness(22, 18, 22, 26));
                menu.Opacity = 1; card.Opacity = 1;
                menu.UpdateLayout();
                string? shots = Environment.GetEnvironmentVariable("LANG_MENU_SHOTS");
                Rect In(FrameworkElement e, FrameworkElement root) => e.TransformToAncestor(root).TransformBounds(new Rect(e.RenderSize));
                var radios = VisualDescendants(menu).OfType<RadioButton>().Where(r => r.GroupName == "LangGroup" && r.IsVisible).ToList();
                Assert.NotEmpty(radios);
                Border HoverBox(RadioButton r) => (Border)r.Template.FindName("bd", r);
                double split = radios.Select(r => In(r, menu).Left).Distinct().Average();
                var left = radios.Where(r => In(r, menu).Left < split).ToList();
                var right = radios.Where(r => In(r, menu).Left >= split).ToList();

                var hover = (Brush)menu.FindResource("RowHoverBrush");
                HoverBox(left[left.Count - 1]).Background = hover; HoverBox(right[0]).Background = hover;
                menu.UpdateLayout();
                if (shots != null) SavePng(Render(menu), System.IO.Path.Combine(shots, $"notes-{theme}-look.png"));
                HoverBox(left[left.Count - 1]).ClearValue(Border.BackgroundProperty); HoverBox(right[0]).ClearValue(Border.BackgroundProperty);

                var rings = radios.Select(r => (FrameworkElement)((FrameworkElement)r.Template.FindName("ring", r)).Parent).ToList();
                var labels = radios.Select(r => VisualDescendants(r).OfType<ContentPresenter>().First(cp => cp.Content == r.Content)).ToList();
                menu.UpdateLayout(); byte[] full = Pixels(Render(menu), out int width);
                rings.ForEach(r => r.Opacity = 0); menu.UpdateLayout(); byte[] noRing = Pixels(Render(menu), out _);
                rings.ForEach(r => r.Opacity = 1); labels.ForEach(l => l.Opacity = 0);
                menu.UpdateLayout(); byte[] noLabel = Pixels(Render(menu), out _);
                labels.ForEach(l => l.Opacity = 1); menu.UpdateLayout();
                double worst = 0; int rows = 0;
                for (int i = 0; i < radios.Count; i++)
                {
                    var text = VisualDescendants(radios[i]).OfType<TextBlock>().First(t => t.Text.Length > 0);
                    // Only plain capitals with nothing above or below give a clean cap-height box.
                    if ("ABDEFHIKLMNPRSTUVWXZ".IndexOf(text.Text[0]) < 0) continue;
                    var glyph = new FormattedText(text.Text.Substring(0, 1), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, 1.0);
                    Rect name = In(text, menu), ringBox = In(rings[i], menu);
                    double? ring = InkCenter(full, noRing, width, new Rect(ringBox.Left - 1, ringBox.Top - 3, ringBox.Width + 2, ringBox.Height + 6));
                    double? cap = InkCenter(full, noLabel, width, new Rect(name.Left, name.Top - 4, glyph.WidthIncludingTrailingWhitespace, name.Height + 8));
                    Assert.True(ring != null && cap != null, "No ink for " + text.Text);
                    worst = Math.Max(worst, Math.Abs(ring!.Value - cap!.Value)); rows++;
                }
                Assert.True(rows > 0 && worst <= 0.5, $"Rings sit {worst:0.#}px off their capitals over {rows} rows.");

                var divider = VisualDescendants(menu).OfType<Border>().First(b => b.Width == 1 && b.ActualHeight > 20);
                divider.Background = new SolidColorBrush(Color.FromRgb(0, 255, 0)); divider.OpacityMask = null; divider.Opacity = 1;
                menu.UpdateLayout();
                int y = (int)(In(left[0], menu).Top + In(left[0], menu).Height / 2);
                byte[] line = Pixels(Render(menu), out _);
                var magenta = new SolidColorBrush(Color.FromRgb(255, 0, 255));
                radios.ForEach(r => HoverBox(r).Background = magenta); menu.UpdateLayout();
                var gaps = Render(menu);
                if (shots != null) SavePng(gaps, System.IO.Path.Combine(shots, $"notes-{theme}-gaps.png"));
                byte[] boxes = Pixels(gaps, out _);
                bool Green(byte[] p, int x) { int i = (y * width + x) * 4; return p[i + 1] > 200 && p[i + 2] < 80 && p[i] < 80; }
                bool Magenta(byte[] p, int x) { int i = (y * width + x) * 4; return p[i + 2] > 200 && p[i] > 200 && p[i + 1] < 80; }
                int lineLeft = Enumerable.Range(0, width).First(x => Green(line, x));
                int lineRight = Enumerable.Range(0, width).Last(x => Green(line, x));
                int leftEnd = Enumerable.Range(0, lineLeft).Last(x => Magenta(boxes, x));
                int rightStart = Enumerable.Range(lineRight + 1, width - lineRight - 1).First(x => Magenta(boxes, x));
                int gapLeft = lineLeft - leftEnd - 1, gapRight = rightStart - lineRight - 1;
                Assert.True(gapLeft == gapRight && gapLeft >= 6, $"Divider gaps are {gapLeft}px left and {gapRight}px right.");

                var bounds = radios.Select(r => In(HoverBox(r), card)).ToList();
                double l = bounds.Min(r => r.Left), t = bounds.Min(r => r.Top);
                double rr = card.ActualWidth - bounds.Max(r => r.Right), b = card.ActualHeight - bounds.Max(r => r.Bottom);
                double pixel = 1 / VisualTreeHelper.GetDpi(card).DpiScaleX;
                string measured = $"{theme}: rings within {worst:0.#}px over {rows} rows, divider {gapLeft}/{gapRight}px, edges L{l:0.##} T{t:0.##} R{rr:0.##} B{b:0.##}";
                foreach (double gap in new[] { l, t, rr, b })
                    Assert.True(Math.Abs(gap - 8) <= pixel + 0.01, measured);
                if (shots != null) System.IO.File.AppendAllText(System.IO.Path.Combine(shots, "notes-measured.txt"), measured + Environment.NewLine);
            }
            finally
            {
                menu.IsOpen = false;
                owner.Close();
            }
        });

        // Theme rows and Film Grain, measured the same way: each ring or box centers on its label's
        // first capital within half a pixel, in three themes. LANG_MENU_SHOTS keeps the render.
        [Theory]
        [InlineData(Theme.TrashPolka)]
        [InlineData(Theme.Dark)]
        [InlineData(Theme.SE98)]
        public void ThemeMenuRingsCenterOnTheirLabelsInPixels(Theme theme) => CardColorBehaviorTests.WithShell((window, _) =>
        {
            Application.Current.Resources.MergedDictionaries[0] = ThemeHarness.Create(theme, Accent.Red);
            var menu = (ContextMenu)window.FindName("ThemeMenu");
            var target = new Button();
            var owner = new Window { Content = target, Width = 160, Height = 80, ShowInTaskbar = false, UseLayoutRounding = window.UseLayoutRounding };
            owner.Show();
            try
            {
                ((Button)window.FindName("ThemeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                menu.IsOpen = false;
                menu.PlacementTarget = target;
                menu.Placement = PlacementMode.Bottom;
                menu.CustomPopupPlacementCallback = null;
                menu.IsOpen = true;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                foreach (var element in VisualDescendants(menu).OfType<FrameworkElement>().Prepend(menu))
                    element.BeginAnimation(UIElement.OpacityProperty, null);
                menu.Opacity = 1;
                menu.UpdateLayout();
                string? shots = Environment.GetEnvironmentVariable("LANG_MENU_SHOTS");
                if (shots != null) SavePng(Render(menu), System.IO.Path.Combine(shots, $"notes-{theme}-theme.png"));
                var toggles = VisualDescendants(menu).OfType<ToggleButton>().Where(t => t.IsVisible && t.Template?.FindName("ring", t) is FrameworkElement).ToList();
                double worst = RingAlignment(menu, toggles, out int rows);
                Assert.True(rows >= 2 && toggles.Any(t => t is CheckBox), $"{theme}: measured {rows} rows.");
                Assert.True(worst <= 0.5, $"{theme}: rings sit {worst:0.#}px off their capitals over {rows} rows.");
                if (shots != null) System.IO.File.AppendAllText(System.IO.Path.Combine(shots, "notes-measured.txt"), $"{theme} theme menu: rings within {worst:0.#}px over {rows} rows" + Environment.NewLine);
            }
            finally
            {
                menu.IsOpen = false;
                owner.Close();
            }
        });

        // Renders the menu three ways (whole, rings hidden, labels hidden) and returns the worst gap
        // between a ring's ink center and its label's first capital.
        private static double RingAlignment(FrameworkElement menu, IList<ToggleButton> toggles, out int rows)
        {
            Rect In(FrameworkElement e) => e.TransformToAncestor(menu).TransformBounds(new Rect(e.RenderSize));
            var rings = toggles.Select(t => (FrameworkElement)((FrameworkElement)t.Template.FindName("ring", t)).Parent).ToList();
            var labels = toggles.Select(t => VisualDescendants(t).OfType<ContentPresenter>().First(cp => cp.Content == t.Content)).ToList();
            menu.UpdateLayout(); byte[] full = Pixels(Render(menu), out int width);
            rings.ForEach(r => r.Opacity = 0); menu.UpdateLayout(); byte[] noRing = Pixels(Render(menu), out _);
            rings.ForEach(r => r.Opacity = 1); labels.ForEach(l => l.Opacity = 0);
            menu.UpdateLayout(); byte[] noLabel = Pixels(Render(menu), out _);
            labels.ForEach(l => l.Opacity = 1); menu.UpdateLayout();
            double worst = 0; rows = 0;
            for (int i = 0; i < toggles.Count; i++)
            {
                var text = VisualDescendants(labels[i]).OfType<TextBlock>().FirstOrDefault(t => t.Text.Length > 0);
                if (text == null || "ABDEFHIKLMNPRSTUVWXZ".IndexOf(text.Text[0]) < 0) continue;
                var glyph = new FormattedText(text.Text.Substring(0, 1), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, 1.0);
                Rect name = In(text), ringBox = In(rings[i]);
                double? ring = InkCenter(full, noRing, width, new Rect(ringBox.Left - 1, ringBox.Top - 3, ringBox.Width + 2, ringBox.Height + 6));
                double? cap = InkCenter(full, noLabel, width, new Rect(name.Left, name.Top - 4, glyph.WidthIncludingTrailingWhitespace, name.Height + 8));
                Assert.True(ring != null && cap != null, "No ink for " + text.Text);
                worst = Math.Max(worst, Math.Abs(ring!.Value - cap!.Value)); rows++;
            }
            return worst;
        }

        // The vertical center of the pixels that differ between two renders inside a box.
        private static double? InkCenter(byte[] a, byte[] b, int width, Rect box)
        {
            int height = a.Length / 4 / width;
            int x0 = Math.Max(0, (int)Math.Floor(box.Left)), x1 = Math.Min(width - 1, (int)Math.Ceiling(box.Right));
            int y0 = Math.Max(0, (int)Math.Floor(box.Top)), y1 = Math.Min(height - 1, (int)Math.Ceiling(box.Bottom));
            double top = double.NaN, bottom = double.NaN;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = (y * width + x) * 4;
                    if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) <= 90) continue;
                    if (double.IsNaN(top)) top = y;
                    bottom = y + 1; break;
                }
            return double.IsNaN(top) ? null : (top + bottom) / 2;
        }

        private static System.Windows.Media.Imaging.RenderTargetBitmap Render(FrameworkElement element)
        {
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),
                (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element); bitmap.Freeze();
            return bitmap;
        }

        private static byte[] Pixels(System.Windows.Media.Imaging.BitmapSource bitmap, out int width)
        {
            width = bitmap.PixelWidth;
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            return pixels;
        }

        private static void SavePng(System.Windows.Media.Imaging.BitmapSource bitmap, string path)
        {
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(path); encoder.Save(stream);
        }

        private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var descendant in VisualDescendants(child)) yield return descendant;
            }
        }

        [Fact]
        public void LightColorPickerCaptionHasNoTextShadow() => CardColorBehaviorTests.WithShell((_, _) =>
        {
            var app = Application.Current;
            app.Resources.MergedDictionaries[0] = ThemeHarness.Create(Theme.Light, Accent.Blue);
            var picker = new ColorPickerDialog(null, Colors.Red);
            try
            {
                var caption = Descendants((DependencyObject)picker.Content).OfType<TextBlock>()
                    .Single(text => text.Text == (string)app.FindResource("Str_Dlg_PickColor"));
                Assert.Null(caption.Effect);
            }
            finally { picker.Close(); }
        });

        [Fact]
        public void MultiNoteCardFitsItsContentWithoutClippingThePalette() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            long first = NoteStore.Create("A");
            long second = NoteStore.Create("B");
            var target = new Button();
            var owner = new Window { Content = target, Width = 160, Height = 80, ShowInTaskbar = false };
            owner.Show();
            var menu = (ContextMenu)window.FindName("NoteCard");
            try
            {
                host.ShowNoteCard(new[] { first, second }, target, false);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                menu.UpdateLayout();
                Assert.InRange(menu.ActualWidth, 280, 403.99);
                // 1.7.1: the palette sits in the Color tile's flyout, and every tile fits the card's face.
                var face = (FrameworkElement)menu.Template.FindName("MenuRoot", menu);
                var palette = CardTileHelper.Swatches(menu);
                Assert.Equal(11, palette.Children.OfType<Button>().Count());
                foreach (var tile in Shell.NoteCards.Tiles(menu))
                {
                    Assert.InRange(tile.TranslatePoint(new Point(), face).X, 0, face.ActualWidth);
                    Assert.InRange(tile.TranslatePoint(new Point(tile.ActualWidth, 0), face).X, 0, face.ActualWidth);
                }
            }
            finally { menu.IsOpen = false; owner.Close(); }
        });

        [Fact]
        public void UnpickedDarkCardSegmentsUseReadableNeutralTextAndOnlyThePickedChoiceUsesAccent() => CardColorBehaviorTests.WithShell((_, _) =>
        {
            var app = Application.Current;
            var choice = new RadioButton { Content = "Rendered", IsChecked = false, Style = (Style)app.FindResource("CardSegment") };
            var track = new Border { Background = (Brush)app.FindResource("MenuHoverBrush"), Child = choice };
            track.Measure(new Size(200, 40));
            track.Arrange(new Rect(0, 0, 200, 40));
            track.UpdateLayout();
            var text = ((SolidColorBrush)choice.Foreground).Color;
            var fill = ((SolidColorBrush)track.Background).Color;

            Assert.Equal(text.R, text.G);
            Assert.Equal(text.G, text.B);
            Assert.True(ColorContrast.Contrast(text, fill) >= 4.5);
            choice.IsChecked = true;
            Assert.Equal(((SolidColorBrush)app.FindResource("PrimaryBrush")).Color, ((SolidColorBrush)choice.Foreground).Color);
        });
        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
