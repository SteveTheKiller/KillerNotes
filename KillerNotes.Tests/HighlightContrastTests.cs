using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The highlighter paints black text on yellow. Loading or pasting clears neutral text colors
    /// so ordinary text follows the theme, and that used to strip the black too, leaving white on
    /// yellow in every dark theme. Text on a deliberate colored ground keeps its color; plain
    /// neutral text still follows the theme.
    /// </summary>
    public sealed class HighlightContrastTests
    {
        [Fact]
        public void HighlightedTextKeepsItsColorThroughReload() => Sta.Run(() =>
        {
            var highlighted = new Run("marked") { Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0x3B)), Foreground = Brushes.Black };
            var plain = new Run(" plain") { Foreground = Brushes.Black };
            var spanned = new Span(new Run("inside")) { Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0x3B)) };
            ((Run)spanned.Inlines.FirstInline).Foreground = Brushes.Black;
            var doc = Reload(new FlowDocument(new Paragraph(highlighted) { Inlines = { plain, spanned } }));

            MainWindow.NormalizeThemeColors(doc);

            var runs = doc.Blocks.OfType<Paragraph>().SelectMany(p => Runs(p.Inlines)).ToList();
            Assert.Equal(Colors.Black, ((SolidColorBrush)runs.Single(r => r.Text == "marked").ReadLocalValue(TextElement.ForegroundProperty)).Color);
            Assert.Equal(Colors.Black, ((SolidColorBrush)runs.Single(r => r.Text == "inside").ReadLocalValue(TextElement.ForegroundProperty)).Color);
            Assert.Equal(DependencyProperty.UnsetValue, runs.Single(r => r.Text == " plain").ReadLocalValue(TextElement.ForegroundProperty));
        });

        [Fact]
        public void TextOnADarkGroundStillFollowsTheTheme() => Sta.Run(() =>
        {
            // A background swatch (dark teal) over text whose color is only the save-time theme's.
            var swatched = new Run("swatch") { Background = new SolidColorBrush(Color.FromRgb(0x08, 0x4D, 0x6E)) };
            var doc = Reload(new FlowDocument(new Paragraph(swatched)) { Foreground = Brushes.White });

            MainWindow.NormalizeThemeColors(doc);

            var run = doc.Blocks.OfType<Paragraph>().SelectMany(p => Runs(p.Inlines)).Single(r => r.Text == "swatch");
            Assert.Equal(DependencyProperty.UnsetValue, run.ReadLocalValue(TextElement.ForegroundProperty));
        });

        private static FlowDocument Reload(FlowDocument doc)
        {
            using var ms = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.XamlPackage);
            var loaded = new FlowDocument();
            ms.Position = 0;
            new TextRange(loaded.ContentStart, loaded.ContentEnd).Load(ms, DataFormats.XamlPackage);
            return loaded;
        }

        private static System.Collections.Generic.IEnumerable<Run> Runs(InlineCollection inlines)
        {
            foreach (var i in inlines)
            {
                if (i is Run r) yield return r;
                else if (i is Span s) foreach (var n in Runs(s.Inlines)) yield return n;
            }
        }
    }
}
