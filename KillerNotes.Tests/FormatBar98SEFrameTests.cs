using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace KillerNotes.Tests
{
    // On 98SE the format bar is a raised strip framed on all four sides, docked or floating.
    public sealed class FormatBar98SEFrameTests
    {
        private static string Root()
        {
            for (var d = new DirectoryInfo(System.AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "KillerNotes.csproj"))) return d.FullName;
            throw new DirectoryNotFoundException("KillerNotes sources");
        }

        private static string Key(string file, string key) =>
            XDocument.Load(Path.Combine(Root(), "Themes", "App", file)).Descendants()
                .Single(e => (string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) == key).Value.Trim();

        [Fact]
        public void DockingKeeps98SEOuterFrame() =>
            Assert.Equal(Key("98SE.xaml", "BarEdgeThickness"), Key("98SE.xaml", "DockedBarEdgeThickness"));

        [Fact]
        public void TheInnerBevelRunsAllFourSides()
        {
            var bar = XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml")).Descendants()
                .Single(e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "FormatBar");
            var frames = bar.Descendants().Where(e => e.Name.LocalName == "Border" && ((string?)e.Attribute("BorderBrush"))?.Contains("Bevel") == true)
                .Select(e => ((string)e.Attribute("BorderBrush")!, (string)e.Attribute("BorderThickness")!)).ToList();
            Assert.Contains(("{DynamicResource BevelLightBrush}", "2,2,0,0"), frames);
            Assert.Contains(("{DynamicResource BevelDarkBrush}", "0,0,2,2"), frames);
        }
    }
}
