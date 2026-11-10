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
            var bar = new DialogTitleBar();
            var close = (Button)bar.FindName("CloseButton");

            Assert.Equal(2, close.Margin.Right);
        });

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
        private static FrameworkElement Host(Window win)
        {
            double width = double.IsNaN(win.Width) ? 520 : win.Width;
            var content = (FrameworkElement)win.Content;
            win.Content = null;
            var host = new Grid { Width = width };
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
