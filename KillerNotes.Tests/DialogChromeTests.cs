using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The chrome around KillerNotes' own windows, built with the real theme dictionaries: the
    /// floating windows keep their card on every theme but 98SE, every caption close button
    /// shows its X and clears the frame, and the Backup footer's buttons never sit on each other.
    /// </summary>
    [Collection("Application resources")]
    public sealed class DialogChromeTests
    {
        public static TheoryData<int, string> FloatingWindows()
        {
            var data = new TheoryData<int, string>();
            foreach (var theme in new[] { Theme.Cyanotic, Theme.Black, Theme.Light, Theme.SE98 })
                foreach (string window in new[] { "Dictation", "Sketch", "Graph" })
                    data.Add((int)theme, window);
            return data;
        }

        [Theory]
        [MemberData(nameof(FloatingWindows))]
        public void FloatingWindowsKeepTheirCardOnEveryThemeButNinetyEight(int themeId, string window) => WithTheme((Theme)themeId, "en-US", () =>
        {
            Window win = window switch
            {
                "Dictation" => new KillerNotes.Controls.Dictation.DictationWindow(null, _ => { }, (_, _, _, _, _) => null),
                "Sketch" => new KillerNotes.Controls.Sketch.SketchPadWindow(null, (_, _, _) => { }),
                _ => new GraphWindow(_ => { }, _ => { }, -1, "Graph"),
            };
            var card = Assert.IsAssignableFrom<Border>(win.Content);
            bool flat = (Theme)themeId == Theme.SE98;

            // Every theme defines UseDialogCaption, as false unless it is 98SE; reading the key's
            // presence as "flat" took the halo, shadow, corners and edge off every theme.
            Assert.Equal(flat ? 0 : 20, card.Margin.Left);
            Assert.Equal(flat, card.CornerRadius.TopLeft == 0);
            Assert.Equal(flat, card.Effect == null);
            if (!flat) Assert.True(card.BorderThickness.Left > 0, "The card lost its edge.");
        });

        [Theory]
        [InlineData((int)Theme.Cyanotic)]
        [InlineData((int)Theme.SE98)]
        public void TheGraphCloseButtonShowsItsX(int themeId) => WithTheme((Theme)themeId, "en-US", () =>
        {
            var win = new GraphWindow(_ => { }, _ => { }, -1, "Graph");
            FrameworkElement content = Host(win);
            var close = Descendants(content).OfType<Button>()
                .Single(b => ReferenceEquals(b.Style, Application.Current.TryFindResource("ChromeCloseButton")));

            Assert.Equal("", close.Content);
        });

        [Fact]
        public void TheNinetyEightDialogCloseClearsTheWindowFrame() => WithTheme(Theme.SE98, "en-US", () =>
        {
            Rect close = CloseBox(new PasswordDialog("Heading", "Detail", "OK"));

            // Inside the 3 px frame and 2 px in from the caption band's end, like ConfirmDialog.
            Assert.Equal(400 - 3 - 2, close.Right, 1);
        });

        [Fact]
        public void TheDialogCloseXIsTheFamilySizeLikeEveryOtherWindow() => WithTheme(Theme.Cyanotic, "en-US", () =>
        {
            var bar = new DialogTitleBar();
            var close = (Button)bar.FindName("CloseButton");

            Assert.Equal((double)Application.Current.FindResource("AboutCloseWidth"), close.Width);
            Assert.Equal((double)Application.Current.FindResource("AboutCloseHeight"), close.Height);
        });

        // The shared title bar (Password, Tags, Input, Databases) put its X flush in the corner on
        // the card themes while ConfirmDialog's sat 14 px in, so two dialogs from the same app
        // disagreed. Laid out side by side, the X has to land in the same place on both, 98SE
        // included, where the shared bar's caption band ran edge to edge over the frame.
        [Theory]
        [InlineData((int)Theme.Cyanotic)]
        [InlineData((int)Theme.Black)]
        [InlineData((int)Theme.Light)]
        [InlineData((int)Theme.Dark)]
        [InlineData((int)Theme.SE98)]
        public void TheTitleBarCloseSitsWhereConfirmDialogPutsIt(int themeId) => WithTheme((Theme)themeId, "en-US", () =>
        {
            Rect confirm = CloseBox(new ConfirmDialog("Heading", "Detail", "OK"));
            Rect password = CloseBox(new PasswordDialog("Heading", "Detail", "OK"));

            Assert.Equal(confirm.Width, password.Width, 1);
            Assert.Equal(confirm.Right, password.Right, 1);
            Assert.Equal(confirm.Top + confirm.Height / 2, password.Top + password.Height / 2, 1);

            Rect confirmIcon = IconBox(new ConfirmDialog("Heading", "Detail", "OK"));
            Rect passwordIcon = IconBox(new PasswordDialog("Heading", "Detail", "OK"));
            Assert.Equal(confirmIcon.Left, passwordIcon.Left, 1);
            Assert.Equal(confirmIcon.Top, passwordIcon.Top, 1);
        });

        // The 98SE close is a 16x14 Win98 caption button with a 2 px bevel all round. Its X used
        // to fill the whole face, jammed against the left highlight and the bottom shadow. The
        // glyph's ink, not its line box, has to sit inside the face and centered on the button.
        // Fixed offsets depend on display scaling. Center the visible ink at every DPI.
        public static TheoryData<int, double> CloseGlyphScales()
        {
            var data = new TheoryData<int, double>();
            foreach (var theme in new[] { Theme.SE98, Theme.Cyanotic, Theme.Dark, Theme.Light })
                foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 }) data.Add((int)theme, dpi);
            return data;
        }

        [Theory]
        [MemberData(nameof(CloseGlyphScales))]
        public void TheCloseGlyphIsCenteredInItsButton(int themeId, double dpi) => WithTheme((Theme)themeId, "en-US", () =>
        {
            var win = new PasswordDialog("Heading", "Detail", "OK") { Width = 400 };
            FrameworkElement content = Host(win, dpi);
            var style = Application.Current.TryFindResource("OverlayCloseButton");
            var close = Descendants(content).OfType<Button>().First(b => ReferenceEquals(b.Style, style));
            var text = Descendants(close).OfType<TextBlock>().First();
            var ft = new FormattedText(text.Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black,
                null, TextOptions.GetTextFormattingMode(text), VisualTreeHelper.GetDpi(text).PixelsPerDip);
            Point origin = text.TransformToAncestor(close).Transform(new Point(0, 0));
            Rect ink = ft.BuildGeometry(origin).Bounds;
            double w = close.ActualWidth, h = close.ActualHeight;

            Assert.InRange(ink.Left + ink.Width / 2 - w / 2, -0.5, 0.5);
            Assert.InRange(ink.Top + ink.Height / 2 - h / 2, -0.5, 0.5);
            if ((Theme)themeId != Theme.SE98) return;
            // Half a pixel clear of the 2 px bevel on every side, so no edge pixel touches it.
            Assert.True(ink.Left >= 2.5 && ink.Right <= w - 2.5, $"The X {ink} runs into the bevel of a {w}x{h} button.");
            Assert.True(ink.Top >= 2.5 && ink.Bottom <= h - 2.5, $"The X {ink} runs into the bevel of a {w}x{h} button.");
        });

        // On the card themes the close button has no visible face, so the X reads against the
        // card's corner: its ink sits as far in from the right edge as down from the top edge.
        public static TheoryData<int, string, double> CloseCornerScales()
        {
            var data = new TheoryData<int, string, double>();
            foreach (var theme in new[] { Theme.Cyanotic, Theme.Dark, Theme.Light })
                foreach (string dialog in new[] { "Confirm", "Password" })
                    foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 }) data.Add((int)theme, dialog, dpi);
            return data;
        }

        [Theory]
        [MemberData(nameof(CloseCornerScales))]
        public void TheCardCloseXSitsInTheCorner(int themeId, string dialog, double dpi) => WithTheme((Theme)themeId, "en-US", () =>
        {
            Window win = dialog == "Confirm" ? new ConfirmDialog("Heading", "Detail", "OK") : new PasswordDialog("Heading", "Detail", "OK");
            win.Width = 400;
            FrameworkElement content = Host(win, dpi);
            var style = Application.Current.TryFindResource("OverlayCloseButton");
            var close = Descendants(content).OfType<Button>().First(b => ReferenceEquals(b.Style, style));
            DependencyObject cardObj = close;
            while (cardObj is not PickerBorder) cardObj = VisualTreeHelper.GetParent(cardObj);
            var card = (FrameworkElement)cardObj;
            var text = Descendants(close).OfType<TextBlock>().First();
            var ft = new FormattedText(text.Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black,
                null, TextOptions.GetTextFormattingMode(text), VisualTreeHelper.GetDpi(text).PixelsPerDip);
            Rect ink = ft.BuildGeometry(text.TransformToAncestor(card).Transform(new Point(0, 0))).Bounds;

            double fromTop = ink.Top + ink.Height / 2;
            double fromRight = card.ActualWidth - (ink.Left + ink.Width / 2);
            Assert.InRange(fromRight - fromTop, -1, 1);
        });

        // Steve's standing rule: no drop shadow anywhere on a button, under the face, behind the
        // caption or behind an icon. Every button style in the app's dictionaries is rendered on
        // three themes and any DropShadowEffect in its tree fails the test, so no kit sync and no
        // new style can bring one back.
        [Theory]
        [InlineData((int)Theme.Dark)]
        [InlineData((int)Theme.Cyanotic)]
        [InlineData((int)Theme.Light)]
        public void NoButtonStyleCastsAShadow(int themeId) => WithTheme((Theme)themeId, "en-US", () =>
        {
            var offenders = new List<string>();
            foreach (var dict in Application.Current.Resources.MergedDictionaries)
                foreach (object key in dict.Keys)
                {
                    if (dict[key] is not Style style || !typeof(System.Windows.Controls.Primitives.ButtonBase).IsAssignableFrom(style.TargetType)) continue;
                    var button = (Control)Activator.CreateInstance(style.TargetType)!;
                    button.Style = style;
                    if (button is ContentControl cc) cc.Content = "Unlock";
                    button.Width = 120; button.Height = 32;
                    var host = new Grid();
                    host.Children.Add(button);
                    host.Measure(new Size(200, 60));
                    host.Arrange(new Rect(0, 0, 200, 60));
                    host.UpdateLayout();
                    foreach (var e in new[] { (DependencyObject)button }.Concat(Descendants(button)).OfType<UIElement>())
                        if (e.Effect is System.Windows.Media.Effects.DropShadowEffect or System.Windows.Media.Effects.BlurEffect)
                            offenders.Add(key + ": " + e.GetType().Name + "#" + (e as FrameworkElement)?.Name);
                }
            Assert.Empty(offenders);
        });

        // The render test above sees a button at rest; a shadow set only on hover or press hides from
        // it. This reads every button style in the app's XAML, triggers included.
        [Theory]
        [InlineData("Controls/Controls.xaml")]
        [InlineData("Controls/AppStyles.xaml")]
        [InlineData("Shell/MainWindow.xaml")]
        public void NoButtonStyleHidesAShadowInATrigger(string file)
        {
            string root = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(root, "KillerNotes.csproj"))) root = Path.GetDirectoryName(root)!;
            var doc = System.Xml.Linq.XDocument.Load(Path.Combine(root, file));
            var types = new[] { "Button", "ToggleButton", "RadioButton", "CheckBox", "RepeatButton" };
            var shadowed = doc.Descendants()
                .Where(e => e.Name.LocalName == "Style" && types.Contains(((string?)e.Attribute("TargetType") ?? "").Split(':').Last()))
                .Where(st => st.Descendants().Any(d => d.Name.LocalName is "DropShadowEffect" or "BlurEffect"
                    || d.Attributes().Any(at => ((string)at).Contains("TextStroke") || ((string)at).Contains("IconShadowOpacity"))))
                .Select(st => (string?)st.Attribute(System.Xml.Linq.XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) ?? "(implicit)")
                .ToList();
            Assert.Empty(shadowed);
        }

        private static Rect IconBox(Window win)
        {
            win.Width = 400;
            FrameworkElement content = Host(win);
            var icon = Descendants(content).OfType<AppIcon>().First();
            DependencyObject card = icon;
            while (card is not PickerBorder) card = VisualTreeHelper.GetParent(card);
            return icon.TransformToAncestor((Visual)card).TransformBounds(new Rect(icon.RenderSize));
        }

        private static Rect CloseBox(Window win)
        {
            win.Width = 400;
            FrameworkElement content = Host(win);
            var style = Application.Current.TryFindResource("OverlayCloseButton");
            var close = Descendants(content).OfType<Button>().First(b => ReferenceEquals(b.Style, style));
            // Measured against the visible card, not the window content: ConfirmDialog's content IS
            // the card, while the shared bar's dialogs wrap it in a halo-margined template.
            DependencyObject card = close;
            while (card is not PickerBorder) card = VisualTreeHelper.GetParent(card);
            return close.TransformToAncestor((Visual)card).TransformBounds(new Rect(close.RenderSize));
        }

        [Theory]
        [InlineData("en-US", (int)Theme.Cyanotic)]
        [InlineData("de-DE", (int)Theme.Cyanotic)]
        [InlineData("en-US", (int)Theme.SE98)]
        [InlineData("ru-RU", (int)Theme.SE98)]
        public void TheBackupFooterButtonsNeverOverlap(string language, int themeId) => WithTheme((Theme)themeId, language, () =>
        {
            var win = new BackupDialog(Path.Combine(Path.GetTempPath(), "kn-backup-footer-test.db"));
            FrameworkElement content = Host(win);
            var buttons = new[] { "BackupNowBtn", "TestRestoreBtn", "RestoreBtn", "DoneBtn" }
                .Select(n => (Button)win.FindName(n))
                .Select(b => (Name: b.Name, Box: b.TransformToAncestor(content).TransformBounds(new Rect(b.RenderSize))))
                .ToList();

            for (int i = 0; i < buttons.Count; i++)
            {
                Assert.True(buttons[i].Box.Right <= content.ActualWidth, $"{buttons[i].Name} runs past the dialog.");
                for (int j = i + 1; j < buttons.Count; j++)
                    Assert.False(buttons[i].Box.IntersectsWith(buttons[j].Box) && Rect.Intersect(buttons[i].Box, buttons[j].Box) is { Width: > 0, Height: > 0 },
                        $"{buttons[i].Name} {buttons[i].Box} overlaps {buttons[j].Name} {buttons[j].Box}.");
            }
        });

        // ---- harness ----

        private static void WithTheme(Theme theme, string language, Action body) => Sta.RunApplication(() =>
        {
            ResourceDictionary themeDict = ThemeHarness.Create(theme, ThemeHarness.FirstRunAccent(theme));
            var merged = Application.Current.Resources.MergedDictionaries;
            var saved = merged.ToList();
            merged.Clear();
            merged.Add(themeDict);
            foreach (string p in new[] { "Controls/Controls.xaml", $"Strings/{language}.xaml", "Controls/AppStyles.xaml", "Controls/OverlayScrollViewer.xaml" })
                merged.Add(new ResourceDictionary { Source = new Uri("/KillerNotes;component/" + p, UriKind.Relative) });
            try { body(); }
            finally
            {
                merged.Clear();
                foreach (var d in saved) merged.Add(d);
            }
        });

        /// <summary>Lays a window's content out at the window's width without showing it, carrying
        /// the text settings the content would inherit from the window.</summary>
        private static FrameworkElement Host(Window win, double dpi = 1.0)
        {
            double width = double.IsNaN(win.Width) ? 520 : win.Width;
            var content = (FrameworkElement)win.Content;
            win.Content = null;
            var host = new Grid { Width = width };
            VisualTreeHelper.SetRootDpi(host, new DpiScale(dpi, dpi));
            foreach (var dp in new DependencyProperty[] { TextElement.ForegroundProperty, TextElement.FontFamilyProperty,
                         TextElement.FontSizeProperty, TextOptions.TextFormattingModeProperty })
                host.SetValue(dp, win.GetValue(dp));
            host.Children.Add(content);
            host.Measure(new Size(width, double.IsNaN(win.Height) ? double.PositiveInfinity : win.Height));
            host.Arrange(new Rect(host.DesiredSize));
            host.UpdateLayout();
            return content;
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (DependencyObject d in Descendants(child)) yield return d;
            }
        }
    }
}
