using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>A 10px MDL2 X fills a 98SE caption button's face edge to edge, and a dialog X sized
    /// like the main caption's 16x14 button looks undersized on the 20px dialog band. The glyph
    /// size comes from the theme, and every dialog and card X takes DialogCloseWidth/Height,
    /// which Win98's rule derives from the band: height is the band minus 4, width the band minus 2.</summary>
    public sealed class CaptionGlyphTests
    {
        private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

        [Fact]
        public void ChromeButtonTakesItsGlyphSizeFromTheTheme()
        {
            var xaml = XDocument.Load(Path.Combine(Root(), "Controls", "Controls.xaml"));
            var style = Assert.Single(xaml.Descendants(), e => e.Name.LocalName == "Style"
                && (string?)e.Attribute(X + "Key") == "ChromeButton");
            var size = Assert.Single(style.Elements(), e => e.Name.LocalName == "Setter"
                && (string?)e.Attribute("Property") == "FontSize");
            Assert.Equal("{DynamicResource CaptionGlyphSize}", (string?)size.Attribute("Value"));
        }

        [Fact]
        public void FlatCaptionGetsTheSmallerGlyph()
        {
            var source = File.ReadAllText(Path.Combine(Root(), "Services", "ThemeManager.cs"));
            Assert.Contains("combined[\"CaptionGlyphSize\"] = flat ? 8.0 : 10.0;", source);
        }

        [Fact]
        public void FlatDialogCloseIsDerivedFromTheBand()
        {
            var source = File.ReadAllText(Path.Combine(Root(), "Services", "ThemeManager.cs"));
            Assert.Contains("SetIfAbsent(\"DialogCloseWidth\", pickerCaption ? bar - 2 : 28.0);", source);
            Assert.Contains("SetIfAbsent(\"DialogCloseHeight\", pickerCaption ? bar - 4 : 26.0);", source);
        }

        /// <summary>Every dialog and card close (the OverlayCloseButton X) takes the band-derived
        /// size. The main window's own caption X (CloseBtn, ChromeCloseButton) is not one of them.</summary>
        [Theory]
        [InlineData("Controls", "ConfirmDialog.xaml", 1)]
        [InlineData("Controls", "DialogTitleBar.xaml", 1)]
        [InlineData("Controls", "FileDialog.xaml", 1)]
        [InlineData("Shell", "MainWindow.xaml", 3)]
        public void DialogCaptionCloseTakesTheBandDerivedSize(string folder, string file, int expected)
        {
            var xaml = XDocument.Load(Path.Combine(Root(), folder, file));
            var closes = xaml.Descendants().Where(e => e.Name.LocalName == "Button"
                && (string?)e.Attribute("Style") == "{StaticResource OverlayCloseButton}"
                && (string?)e.Attribute(X + "Name") != "CloseBtn").ToList();
            Assert.Equal(expected, closes.Count);
            foreach (var close in closes)
            {
                Assert.Equal("{DynamicResource DialogCloseWidth}", (string?)close.Attribute("Width"));
                Assert.Equal("{DynamicResource DialogCloseHeight}", (string?)close.Attribute("Height"));
                // A close that spans the card's rows is placed by AboutCloseMargin from the top;
                // one inside its own band row is centered in it.
                if ((string?)close.Attribute("Margin") != "{DynamicResource AboutCloseMargin}")
                    Assert.Equal("Center", (string?)close.Attribute("VerticalAlignment"));
            }
        }

        /// <summary>The About, Fonts and Shortcuts closes sit top-aligned over the card; on 98SE the
        /// 2px band inset plus a 2px gap centers the 16px X in the 20px band.</summary>
        [Fact]
        public void FlatCardCloseMarginCentersItInTheBand()
        {
            var source = File.ReadAllText(Path.Combine(Root(), "Services", "ThemeManager.cs"));
            Assert.Contains("combined[\"AboutCloseMargin\"] = flat ? new Thickness(0, 4, 4, 0)", source);
        }

        private static string Root([CallerFilePath] string here = "")
            => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));
    }
}
