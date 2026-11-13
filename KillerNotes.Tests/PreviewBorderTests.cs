using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class PreviewBorderTests
    {
        [Fact]
        public void RenderedPreviewHasNoInternalLeftDividerAndSplitKeepsItsDivider() => CardColorBehaviorTests.WithShell((window, _) =>
        {
            var preview = (Border)window.FindName("PreviewPane");
            var parent = (Panel)preview.Parent;
            parent.Children.Remove(preview);
            var host = new Grid { Width = 160, Height = 80 };
            host.Children.Add(preview);
            try
            {
                Click("PreviewRenderedSeg");
                Assert.Equal(new Thickness(0), preview.BorderThickness);
                AssertDivider(false);
                Click("PreviewSplitSeg");
                Assert.Equal(new Thickness(1, 0, 0, 0), preview.BorderThickness);
                AssertDivider(true);
                Click("PreviewRenderedSeg");
                Assert.Equal(new Thickness(0), preview.BorderThickness);
                AssertDivider(false);
            }
            finally
            {
                host.Children.Remove(preview);
                parent.Children.Add(preview);
            }

            void Click(string name) => ((ButtonBase)window.FindName(name)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            void AssertDivider(bool present)
            {
                // Isolate the real pane's edge with contrasting brushes so a one-pixel internal
                // divider is distinguishable from both the document and the outer pane frame.
                preview.Background = Brushes.White;
                preview.BorderBrush = Brushes.Black;
                preview.Child = null;
                host.Measure(new Size(160, 80));
                host.Arrange(new Rect(0, 0, 160, 80));
                host.UpdateLayout();
                var bitmap = new RenderTargetBitmap(160, 80, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(host);
                var pixel = new byte[4];
                bitmap.CopyPixels(new Int32Rect(0, 40, 1, 1), pixel, 4, 0);
                Assert.Equal((byte)(present ? 0 : 255), pixel[0]);
                Assert.Equal((byte)255, pixel[3]);
            }
        });
    }
}
