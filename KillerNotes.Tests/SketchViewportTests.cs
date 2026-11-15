using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KillerNotes.Controls.Sketch;
using KillerNotes.Models;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class SketchViewportTests
    {
        public static IEnumerable<object[]> ToolbarLocales()
        {
            foreach (Locale locale in Enum.GetValues(typeof(Locale)))
                foreach (double width in new[] { 900.0, 520.0 })
                    yield return new object[] { locale.ToString(), width };
        }

        [Theory]
        [MemberData(nameof(ToolbarLocales))]
        public void ToolbarFitsLocalizedLabelsAndKeepsZoomBesideClear(string language, double width) => CardColorBehaviorTests.WithShell((_, _) =>
        {
            var previous = LocaleManager.Current;
            LocaleManager.Apply((Locale)Enum.Parse(typeof(Locale), language));
            var pad = new SketchPadWindow(null, (_, _, _) => { }) { Width = width };
            try
            {
                pad.Show();
                Drain(pad);
                if (width == 520) { pad.Height = 460; Drain(pad); }
                var elements = Elements(pad).ToList();
                Button ButtonNamed(string key) => elements.OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == (string)Application.Current.FindResource(key));
                var clear = ButtonNamed("Str_Sketch_Clear");
                var undo = ButtonNamed("Str_Sketch_Undo");
                var redo = ButtonNamed("Str_Sketch_Redo");
                var copy = ButtonNamed("Str_Sketch_CopyImageTip");
                var print = ButtonNamed("Str_Sketch_Print");
                var zoom = elements.OfType<ComboBox>().Single();
                Rect Bounds(FrameworkElement element) => element.TransformToAncestor(pad)
                    .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                var toolbar = Bounds((FrameworkElement)clear.Parent);
                var controls = new FrameworkElement[] { undo, redo, copy, print, zoom, clear };
                foreach (var control in controls)
                {
                    var bounds = Bounds(control);
                    Assert.True(bounds.Width > 10 && bounds.Height > 10 && toolbar.Contains(bounds),
                        $"{language} at {width}px: toolbar control is clipped.");
                }
                for (int i = 0; i < controls.Length; i++)
                    for (int j = i + 1; j < controls.Length; j++)
                        Assert.False(Bounds(controls[i]).IntersectsWith(Bounds(controls[j])),
                            $"{language} at {width}px: toolbar controls overlap.");
                foreach (var button in new[] { copy, print })
                    foreach (var text in Elements(button).OfType<TextBlock>())
                        Assert.True(Bounds(button).Contains(Bounds(text)),
                            $"{language} at {width}px: action label is clipped.");
                Assert.InRange(Math.Abs(Bounds(clear).Right - toolbar.Right), 0, 1);
                Assert.InRange(Math.Abs(Bounds(clear).Top - Bounds(zoom).Top), 0, 1);
                Assert.True(Bounds(zoom).Right <= Bounds(clear).Left - 7, $"{language}: Zoom must leave a gap before Clear.");
                Assert.True(Bounds(undo).Left < Bounds(redo).Left, $"{language}: Undo must precede Redo.");
                Assert.True(Bounds(copy).Top >= Bounds(undo).Top - 1,
                    $"{language}: Copy at {Bounds(copy)} must not rise above Undo at {Bounds(undo)}.");
                Fits(elements.OfType<ScrollViewer>().Single(viewer => viewer.Content is Canvas));
            }
            finally { pad.Hide(); pad.Close(); LocaleManager.Apply(previous); }
        });

        [Fact]
        public void BlankPadOpensAndResizesWithoutScrollbars() => CardColorBehaviorTests.WithShell((_, _) =>
        {
            var pad = new SketchPadWindow(null, (_, _, _) => { });
            try
            {
                pad.Show();
                Drain(pad);
                var viewer = Elements(pad).OfType<ScrollViewer>().Single(v => v.Content is Canvas);
                Fits(viewer);
                pad.Width = 520;
                pad.Height = 460;
                Drain(pad);
                Fits(viewer);
            }
            finally { pad.Close(); }
        });

        [Fact]
        public void OversizedDrawingScrollsWithoutAWhiteCorner() => CardColorBehaviorTests.WithShell((_, _) =>
        {
            var pad = new SketchPadWindow(null, (_, _, _) => { });
            try
            {
                pad.LoadObjects(new[] { new SketchObject { Kind = SketchKind.Rect, Pts = [10, 10, 100, 100] } }, 3000, 2000);
                pad.Show();
                Drain(pad);
                var viewer = Elements(pad).OfType<ScrollViewer>().Single(v => v.Content is Canvas);
                Assert.Equal(Visibility.Visible, viewer.ComputedHorizontalScrollBarVisibility);
                Assert.Equal(Visibility.Visible, viewer.ComputedVerticalScrollBarVisibility);
                Assert.True(viewer.ExtentWidth >= 3000 && viewer.ExtentHeight >= 2000,
                    "Opening a saved drawing must preserve its requested canvas size.");
                int width = (int)viewer.ActualWidth, height = (int)viewer.ActualHeight;
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(viewer);
                var pixel = new byte[4];
                bitmap.CopyPixels(new Int32Rect(width - 2, height - 2, 1, 1), pixel, 4, 0);
                Assert.False(pixel[3] > 200 && pixel.Take(3).All(value => value > 220),
                    "The scrollbar intersection must not paint the stock white corner.");
                var zoom = Elements(pad).OfType<ComboBox>().Single();
                zoom.SelectedItem = zoom.Items.OfType<ComboBoxItem>().Single(item => item.Tag is double value && value == 2.0);
                Drain(pad);
                Assert.Equal("200%", ((ComboBoxItem)zoom.SelectedItem).Content);
                Assert.True(viewer.ExtentWidth >= 6000);
                zoom.SelectedItem = zoom.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "fit"));
                Drain(pad);
                Fits(viewer);
                zoom.SelectedItem = zoom.Items.OfType<ComboBoxItem>().Single(item => item.Tag is double value && value == 1.0);
                Drain(pad);
                Assert.Equal("100%", ((ComboBoxItem)zoom.SelectedItem).Content);
                Assert.Equal(Visibility.Visible, viewer.ComputedHorizontalScrollBarVisibility);
            }
            finally { pad.Close(); }
        });

        private static void Fits(ScrollViewer viewer)
        {
            Assert.True(viewer.ViewportWidth > 100 && viewer.ViewportHeight > 100);
            Assert.Equal(Visibility.Collapsed, viewer.ComputedHorizontalScrollBarVisibility);
            Assert.Equal(Visibility.Collapsed, viewer.ComputedVerticalScrollBarVisibility);
            Assert.InRange(viewer.ScrollableWidth, 0, 0.5);
            Assert.InRange(viewer.ScrollableHeight, 0, 0.5);
        }

        private static void Drain(Window window)
        {
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            window.UpdateLayout();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }

        private static IEnumerable<DependencyObject> Elements(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Elements(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
    }
}
