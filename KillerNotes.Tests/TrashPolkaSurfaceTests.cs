using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    // Trash Polka: every pane, card and dropdown carries the ink piece, never the flat theme fill. Each surface
    // here showed plain gray or black. Set KN_INK_SHOTS to a folder to keep the renders.
    [Collection("Application resources")]
    public sealed class TrashPolkaSurfaceTests
    {
        [Fact]
        public void NotePaneCardsCalculatorAndViewPickerCarryTheInk() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            Application.Current.Resources.MergedDictionaries[0] = ThemeHarness.Create(Theme.TrashPolka, Accent.Red);
            MarkdownAuthoringTests.Ready(window);
            CardTileHelper.ClickNewTile(window, "markdown");
            Settle(window);
            string? shots = Environment.GetEnvironmentVariable("KN_INK_SHOTS");
            var flat = new List<string>();
            void Check(string name, FrameworkElement element)
            {
                element.UpdateLayout();
                Settle(window);
                var brush = element switch { Border b => b.Background, Panel p => p.Background, Control c => c.Background, _ => null };
                if (brush is null or SolidColorBrush)
                {
                    var chain = new List<string>();
                    for (DependencyObject? up = VisualTreeHelper.GetParent(element); up != null && chain.Count < 4; up = VisualTreeHelper.GetParent(up))
                        chain.Add(up.GetType().Name);
                    flat.Add($"{name} ({brush}, part {Controls.ThemeMotif.GetPart(element)}, base {Controls.ThemeMotif.GetBaseBrush(element)}, "
                        + $"loaded {element.IsLoaded}, {element.ActualWidth:0}x{element.ActualHeight:0}, motif {element.TryFindResource("ThemeMotif")}, "
                        + $"menu same {ReferenceEquals(element.TryFindResource("MenuBackgroundBrush"), brush)}, combo same {ReferenceEquals(element.TryFindResource("ComboPopupBrush"), brush)}, "
                        + $"hit {element.IsHitTestVisible}, up {string.Join(">", chain)})");
                }
                if (shots != null && element.ActualWidth > 0) Save(element, System.IO.Path.Combine(shots, $"ink-{name}.png"));
            }

            Check("NotePane", (FrameworkElement)window.FindName("NotePaneFace"));

            // The About card's page ink slider sets the note's ink and repaints it (PageInk.cs).
            var noteFace = (Border)window.FindName("NotePaneFace");
            var inkBefore = noteFace.Background;
            var pageInk = (Slider)window.FindName("AboutPageInkSlider");
            double inkWas = pageInk.Value;
            pageInk.Value = 12;
            Settle(window);
            Assert.Equal(0.12, Controls.TrashPolkaInk.PageOpacity, 3);
            Assert.NotSame(inkBefore, noteFace.Background);
            pageInk.Value = inkWas;
            Settle(window);
            Check("FormatBar", (FrameworkElement)window.FindName("FormatBar"));
            if (shots != null) Save((FrameworkElement)window.Content, System.IO.Path.Combine(shots, "ink-Window.png"));

            var picker = (ComboBox)window.FindName("PreviewModeBtn");
            // No Loaded handler on the list here: one would make WPF deliver Loaded into it and hide the bug.
            picker.IsDropDownOpen = true;
            Settle(window);
            var popup = (Popup)picker.Template.FindName("PART_Popup", picker);
            var list = (Border)popup.Child;
            Check("ViewPickerList", list);
            picker.IsDropDownOpen = false;

            picker.SelectedValue = "Split";
            Settle(window);
            Check("PreviewPane", (FrameworkElement)window.FindName("PreviewPane"));
            if (shots != null) Save((FrameworkElement)window.Content, System.IO.Path.Combine(shots, "ink-Split.png"));
            picker.SelectedValue = "Source";

            foreach (string name in new[] { "RulePopup", "TableSizePopup" })
            {
                var flyout = (Popup)window.FindName(name);
                flyout.IsOpen = true;
                Settle(window);
                Check(name, (FrameworkElement)flyout.Child);
                flyout.IsOpen = false;
            }

            var findBar = (FrameworkElement)window.FindName("FindBar");
            findBar.Visibility = Visibility.Visible;
            findBar.Opacity = 1;
            Check("FindBar", findBar);
            findBar.Visibility = Visibility.Collapsed;

            Invoke(window, "OpenKalc");
            Settle(window);
            Check("CalculatorReadout", (FrameworkElement)window.FindName("KalcReadout"));
            if (shots != null) Save((FrameworkElement)window.FindName("KalcPanel"), System.IO.Path.Combine(shots, "ink-Calculator.png"));
            Invoke(window, "CloseKalc");

            Invoke(window, "ShowAboutOverlay");
            Settle(window);
            Check("AboutCard", (FrameworkElement)window.FindName("AboutCardFace"));
            ((FrameworkElement)window.FindName("AboutOverlay")).Visibility = Visibility.Collapsed;

            Invoke(window, "ToggleShortcutsOverlay");
            Settle(window);
            Check("ShortcutsCard", (FrameworkElement)window.FindName("ShortcutsCardFace"));
            ((FrameworkElement)window.FindName("ShortcutsOverlay")).Visibility = Visibility.Collapsed;

            Assert.True(flat.Count == 0, "Flat fill, no ink: " + string.Join(", ", flat));
        });

        private static void Invoke(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null)!
                .Invoke(target, null);

        private static void Settle(Window window)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }

        private static void Save(FrameworkElement element, string path)
        {
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),
                (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(path);
            encoder.Save(stream);
        }
    }
}
