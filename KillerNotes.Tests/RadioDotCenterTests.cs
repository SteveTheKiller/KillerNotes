using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The checked radio dot must sit in the exact middle of its ring. An odd dot size in the
    /// even 16px indicator box lands on a half pixel and gets rounded up and to the left, which
    /// is the lopsided dot this guards against. Reads the shipped ThemeRadio template from
    /// source and measures the laid-out ring and dot at every app size.
    /// </summary>
    public sealed class RadioDotCenterTests
    {
        private const string TemplateFile = @"Controls\Controls.xaml";

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.25)]
        [InlineData(1.5)]
        [InlineData(2.0)]
        public void CheckedDotSitsInTheExactMiddleOfTheRing(double scale)
        {
            Exception? failure = null;
            var thread = new System.Threading.Thread(() =>
            {
                try { Check(scale); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        private static void Check(double scale)
        {
            var radio = new RadioButton { Content = "Black", IsChecked = true, Style = LoadThemeRadio() };
            var scaled = new Border { LayoutTransform = new ScaleTransform(scale, scale), Child = radio };
            var root = new Border { Width = 240, UseLayoutRounding = true, Child = scaled };
            foreach (string key in new[] { "TextBrush", "DimTextBrush", "PrimaryBrush", "RadioAccent",
                                           "RadioHoverFgBrush", "RadioWellBrush", "RowHoverBrush" })
                root.Resources[key] = Brushes.Gray;
            root.Resources["UiFont"] = new FontFamily("Segoe UI");

            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            root.Arrange(new Rect(root.DesiredSize));
            root.UpdateLayout();

            var ring = (FrameworkElement)radio.Template.FindName("ring", radio);
            var dot = (FrameworkElement)radio.Template.FindName("dot", radio);
            Assert.NotNull(ring);
            Assert.NotNull(dot);
            Assert.Equal(Visibility.Visible, dot.Visibility);

            Rect r = ring.TransformToAncestor(root).TransformBounds(new Rect(ring.RenderSize));
            Rect d = dot.TransformToAncestor(root).TransformBounds(new Rect(dot.RenderSize));
            double dx = (d.X + d.Width / 2) - (r.X + r.Width / 2);
            double dy = (d.Y + d.Height / 2) - (r.Y + r.Height / 2);
            Assert.True(Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01,
                $"Radio dot is off the ring's center by x={dx:0.##}px y={dy:0.##}px at {scale * 100}% (dot {dot.Width}px). " +
                "Keep the dot an even size so it shares the ring's center.");
        }

        /// <summary>The ThemeRadio style exactly as written in the app's XAML, minus the app's own
        /// clr-namespace bindings (the indicator margin), which do not affect the ring or dot.</summary>
        private static Style LoadThemeRadio()
        {
            XNamespace w = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var doc = XDocument.Load(Path.Combine(FindRepoRoot(), TemplateFile));
            var style = new XElement(doc.Descendants(w + "Style")
                .First(s => (string?)s.Attribute(x + "Key") == "ThemeRadio"));
            foreach (var margin in style.Descendants().Where(e => e.Name.LocalName.EndsWith(".Margin", StringComparison.Ordinal)).ToList())
                margin.Remove();
            foreach (var attr in style.DescendantsAndSelf().SelectMany(e => e.Attributes()).Where(a => a.IsNamespaceDeclaration).ToList())
                attr.Remove();
            style.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
            return (Style)XamlReader.Parse(style.ToString());
        }

        private static string FindRepoRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, TemplateFile))) dir = Path.GetDirectoryName(dir);
            return dir ?? throw new DirectoryNotFoundException(TemplateFile + " not found above the test output.");
        }
    }
}
