using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using KillerNotes.Controls;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class LockedPaneGrainTests
    {
        [Theory]
        [InlineData("Dark")]
        [InlineData("Light")]
        [InlineData("Black")]
        [InlineData("Malaise")]
        public void LockedPaneRendersGrainAndHonorsTheOffSwitch(string theme) => Sta.Run(() =>
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "KillerNotes.csproj"))) root = root.Parent;
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var source = XDocument.Load(Path.Combine(root!.FullName, "Shell", "MainWindow.xaml"));
            var markup = new XElement(source.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "LockedPanel"));
            foreach (var element in markup.DescendantsAndSelf())
            {
                element.Attributes().Where(a => a.Name.LocalName == "Click" || a.Name.LocalName == "Style").Remove();
                if (element.Name.LocalName == "Glyph") element.Name = wpf + "TextBlock";
            }
            markup.SetAttributeValue("Visibility", "Visible");
            var pane = (Grid)XamlReader.Parse(markup.ToString());
            pane.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/KillerNotes;component/Themes/" + theme + ".xaml", UriKind.Relative),
            });
            pane.Resources["GrainTileBrush"] = new ImageBrush(GrainTexture.Generate(Colors.Coral))
            {
                TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 256, 256), Stretch = Stretch.None,
            };
            pane.Resources["FilmGrainVisibility"] = Visibility.Visible;
            var enabled = CornerPixels(pane);
            pane.Resources["FilmGrainVisibility"] = Visibility.Collapsed;
            var disabled = CornerPixels(pane);
            Assert.False(enabled.SequenceEqual(disabled), "The grain switch did not change the locked pane's pixels.");
            pane.Resources["GrainTileBrush"] = Brushes.Transparent;
            pane.Resources["FilmGrainVisibility"] = Visibility.Visible;
            Assert.Equal(disabled, CornerPixels(pane));
        });

        private static int[] CornerPixels(FrameworkElement pane)
        {
            pane.Measure(new Size(400, 300));
            pane.Arrange(new Rect(0, 0, 400, 300));
            pane.UpdateLayout();
            var bitmap = new RenderTargetBitmap(400, 300, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(pane);
            var pixels = new int[32 * 32];
            bitmap.CopyPixels(new Int32Rect(8, 8, 32, 32), pixels, 32 * 4, 0);
            return pixels;
        }
    }
}
