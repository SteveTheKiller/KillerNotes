using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class ControlVisibilityTests
    {
        [Fact]
        public void EveryPaletteHasAnOpaqueVisibleSliderTrack()
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "KillerNotes.csproj")))
                directory = directory.Parent;
            Assert.NotNull(directory);
            var palettes = Directory.GetFiles(Path.Combine(directory!.FullName, "Themes"), "*.xaml");
            Assert.Equal(13, palettes.Length);
            foreach (var path in palettes)
            {
                var resources = XDocument.Load(path).Root!.Elements().ToList();
                string Color(string key) => resources.Single(item => (string?)item.Attribute(x + "Key") == key).Attribute("Color")!.Value;
                string track = Color("SliderTrack");
                Assert.True(track.Length == 7 || track.Length == 9 && track.Substring(1, 2).Equals("FF", StringComparison.OrdinalIgnoreCase), path);
                double a = Luminance(track), b = Luminance(Color("MenuBackgroundBrush"));
                Assert.True((Math.Max(a, b) + .05) / (Math.Min(a, b) + .05) >= 2, path + " has an indistinct slider track.");
            }
        }

        private static double Luminance(string color)
        {
            double Channel(int offset)
            {
                double value = Convert.ToInt32(color.Substring(color.Length - 6 + offset, 2), 16) / 255.0;
                return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
            }
            return .2126 * Channel(0) + .7152 * Channel(2) + .0722 * Channel(4);
        }
    }
}
