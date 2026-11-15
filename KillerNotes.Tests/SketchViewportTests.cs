using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KillerNotes.Controls.Sketch;
using KillerNotes.Models;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class SketchViewportTests
    {
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
