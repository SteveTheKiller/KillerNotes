using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Xml.Linq;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class MenuSeparatorThemeTests
    {
        private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
        private static readonly XNamespace W = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        [Fact]
        public void AllThemesHaveOpaqueMenuDividersAndDeliriumKeepsMatchingSketchGray() => Sta.RunApplication(() =>
        {
            foreach (Theme theme in Enum.GetValues(typeof(Theme)))
                foreach (Accent accent in Enum.GetValues(typeof(Accent)))
                {
                    var palette = ThemeHarness.Create(theme, accent);
                    var divider = Assert.IsType<SolidColorBrush>(palette["MenuSeparatorBrush"]);
                    Assert.Equal((byte)255, divider.Color.A);
                    Assert.Equal(1d, divider.Opacity);
                    var sketch = Assert.IsType<SolidColorBrush>(palette["SketchMenuSeparatorBrush"]);
                    if (theme == Theme.Delirium)
                        Assert.Equal(Color.FromRgb(0x66, 0x66, 0x66), sketch.Color);
                    else
                        Assert.Same(palette["CardBorderBrush"], palette["SketchMenuSeparatorBrush"]);
                }
        });

        [Fact]
        public void OnlyKeyedMenuSeparatorsUseTheMenuSeparatorBrush()
        {
            var styles = XDocument.Load(Path.Combine(FindRepoRoot(), "Controls", "Controls.xaml"))
                .Descendants(W + "Style").Where(style => (string?)style.Attribute("TargetType") == "Separator").ToList();
            var menu = styles.Single(style => (string?)style.Attribute(X + "Key") == "{x:Static MenuItem.SeparatorStyleKey}");
            var generic = styles.Single(style => style.Attribute(X + "Key") == null);
            Assert.Equal("{DynamicResource MenuSeparatorBrush}", Background(menu));
            Assert.Equal("{DynamicResource MenuBorderBrush}", Background(generic));
            string sketch = File.ReadAllText(Path.Combine(FindRepoRoot(), "Controls", "Sketch", "SketchPadWindow.Input.cs"));
            Assert.Contains("line.SetResourceReference(Border.BackgroundProperty, \"SketchMenuSeparatorBrush\");", sketch);
        }

        private static string? Background(XElement style) =>
            (string?)style.Elements(W + "Setter").Single(setter => (string?)setter.Attribute("Property") == "Background").Attribute("Value");

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "KillerNotes.csproj")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new DirectoryNotFoundException("KillerNotes repository root not found.");
        }
    }
}
