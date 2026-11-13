using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class FooterAlignmentTests
    {
        public static System.Collections.Generic.IEnumerable<object[]> ThemesAndScales =>
            from Theme theme in Enum.GetValues(typeof(Theme))
            from double scale in new[] { 1.0, 1.25, 1.5, 2.0 }
            select new object[] { theme, scale };

        [Theory]
        [MemberData(nameof(ThemesAndScales))]
        public void NoteCountStartsAtTheContentInset(Theme theme, double scale) => Sta.RunApplication(() =>
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "KillerNotes.csproj"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var saved = app.Resources.MergedDictionaries.ToList();
            try
            {
                app.Resources.MergedDictionaries.Clear();
                app.Resources.MergedDictionaries.Add(ThemeHarness.Create(theme, ThemeHarness.FirstRunAccent(theme)));
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/KillerNotes;component/Controls/Controls.xaml", UriKind.Relative) });
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                var document = XDocument.Load(Path.Combine(directory!.FullName, "Shell", "MainWindow.xaml"));
                var element = new XElement(document.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "StatusText"));
                element.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
                element.SetAttributeValue(XNamespace.Xmlns + "kl", "clr-namespace:KillerFamily.Layout;assembly=KillerNotes");
                var text = (TextBlock)XamlReader.Parse(element.ToString());
                text.Text = "61 notes";
                text.Foreground = Brushes.White;
                var host = new Grid { Width = 220, Height = 24, UseLayoutRounding = true };
                var inset = new Grid();
                inset.SetResourceReference(FrameworkElement.MarginProperty, "FooterPadding");
                var cell = new Grid();
                cell.SetResourceReference(FrameworkElement.MarginProperty, "FooterCellMargin");
                cell.Children.Add(text);
                inset.Children.Add(cell);
                host.Children.Add(inset);
                host.Measure(new Size(220, 24));
                host.Arrange(new Rect(0, 0, 220, 24));
                host.UpdateLayout();
                int width = (int)(220 * scale), height = (int)(24 * scale);
                var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(host);
                var pixels = new byte[width * height * 4];
                bitmap.CopyPixels(pixels, width * 4, 0);
                int top = height, bottom = -1, left = width;
                for (int y = 0; y < height; y++)
                    for (int column = 0; column < width; column++)
                        if (pixels[(y * width + column) * 4 + 3] > 128) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); left = Math.Min(left, column); }
                Assert.True(bottom >= top, "The note count did not render.");
                double expected = theme == Theme.SE98 ? 4 : 8;
                Assert.InRange(left / scale, expected, expected + 2);
                Assert.True(Math.Abs(top - (height - 1 - bottom)) / scale <= 2.5, $"Note count has {top}px above and {height - 1 - bottom}px below.");
                string? folder = Environment.GetEnvironmentVariable("KN_FOOTER_PNG_DIR");
                if (folder != null)
                {
                    text.SetResourceReference(TextBlock.ForegroundProperty, "TextFooter");
                    host.SetResourceReference(Panel.BackgroundProperty, "FooterBackgroundBrush");
                    var capture = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    capture.Render(host);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(capture));
                    using var file = File.Create(Path.Combine(folder, $"footer-{theme}-{scale * 100:0}.png"));
                    encoder.Save(file);
                }
            }
            finally
            {
                app.Resources.MergedDictionaries.Clear();
                foreach (var item in saved) app.Resources.MergedDictionaries.Add(item);
            }
        });
    }
}
