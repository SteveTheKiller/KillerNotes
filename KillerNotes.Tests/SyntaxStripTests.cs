using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Stripping syntax colors out of a saved note (Services/SyntaxStrip.cs).
    ///
    /// This is byte surgery on the stored copy of somebody's note, so the bar is higher than
    /// "the colors went away". Every test here reloads the stripped package through the real
    /// XamlPackage reader and asserts the TEXT is intact, because a truncation bug or a broken
    /// package would not show up in a string comparison of the XAML.
    ///
    /// The colors have to go because they are a transient view, not the user's formatting. The
    /// live document keeps them; only the stored bytes lose them, which is what removed the full
    /// clear and repaint from every 2 second autosave.
    ///
    /// STA throughout: XamlPackage save and load need it.
    /// </summary>
    public class SyntaxStripTests
    {
        private static readonly Color Keyword = Color.FromRgb(86, 156, 214);
        private static readonly Color Comment = Color.FromRgb(106, 153, 85);
        private static readonly Color UserPick = Color.FromRgb(255, 0, 128);

        private static Color[] Palette => [Keyword, Comment];

        /// <summary>A one-paragraph document where each (text, color) pair becomes its own Run.
        /// A null color leaves the run unpainted.</summary>
        private static byte[] Package(params (string Text, Color? Color)[] runs)
        {
            var para = new Paragraph();
            foreach (var (text, color) in runs)
            {
                var run = new Run(text);
                if (color is Color c) run.Foreground = new SolidColorBrush(c);
                para.Inlines.Add(run);
            }
            var doc = new FlowDocument(para);
            using var ms = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.XamlPackage);
            return ms.ToArray();
        }

        /// <summary>Reload through the real reader and report the text plus every foreground the
        /// document still carries. If the package were damaged, Load throws and the test fails
        /// on that rather than on a colour assertion.</summary>
        private static (string Text, Color[] Colors) Reload(byte[] package)
        {
            var doc = new FlowDocument();
            using var ms = new MemoryStream(package);
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            range.Load(ms, DataFormats.XamlPackage);

            var colors = doc.Blocks.OfType<Paragraph>()
                .SelectMany(p => p.Inlines.OfType<Run>())
                .Select(r => r.Foreground as SolidColorBrush)
                .Where(b => b != null)
                .Select(b => b!.Color)
                .ToArray();
            return (range.Text.TrimEnd('\r', '\n'), colors);
        }

        [Fact]
        public void SyntaxColorsAreRemovedAndTheTextSurvives() => Sta.Run(() =>
        {
            byte[] stripped = SyntaxStrip.Remove(
                Package(("function ", Keyword), ("Get-Thing", null), (" # note", Comment)), Palette);

            var (text, colors) = Reload(stripped);
            Assert.Equal("function Get-Thing # note", text);
            Assert.DoesNotContain(Keyword, colors);
            Assert.DoesNotContain(Comment, colors);
        });

        /// <summary>The one that protects the user: a color they chose themselves is not a syntax
        /// color and must survive, even in a note that has highlighting switched on.</summary>
        [Fact]
        public void AColorTheUserChoseIsLeftAlone() => Sta.Run(() =>
        {
            byte[] stripped = SyntaxStrip.Remove(
                Package(("keyword", Keyword), ("mine", UserPick)), Palette);

            var (text, colors) = Reload(stripped);
            Assert.Equal("keywordmine", text);
            Assert.Contains(UserPick, colors);
            Assert.DoesNotContain(Keyword, colors);
        });

        [Fact]
        public void APackageWithNoSyntaxColorsComesBackByteIdentical() => Sta.Run(() =>
        {
            byte[] original = Package(("plain words", null), ("mine", UserPick));
            Assert.Equal(original, SyntaxStrip.Remove(original, Palette));
        });

        /// <summary>Markdown notes store raw source, not a package. Handing one to the stripper
        /// must return it untouched rather than mangling it.</summary>
        [Fact]
        public void ARawMarkdownBlobIsReturnedUnchanged() => Sta.Run(() =>
        {
            byte[] raw = System.Text.Encoding.UTF8.GetBytes("# heading\n\n\tindented");
            Assert.Equal(raw, SyntaxStrip.Remove(raw, Palette));
        });

        [Fact]
        public void NothingIsRemovedWhenThePaletteIsEmpty() => Sta.Run(() =>
        {
            byte[] original = Package(("keyword", Keyword));
            Assert.Equal(original, SyntaxStrip.Remove(original, []));
        });

        [Fact]
        public void EmptyAndNullInputAreSafe() => Sta.Run(() =>
        {
            Assert.Empty(SyntaxStrip.Remove([], Palette));
            Assert.Empty(SyntaxStrip.Remove(null!, Palette));
        });

        /// <summary>A long document is where a truncation bug would surface: the rewritten XAML is
        /// shorter than the original, and a part written without truncating leaves the tail of the
        /// old content behind and produces a package that either fails to load or duplicates text.</summary>
        [Fact]
        public void ALongDocumentStillLoadsAndKeepsEveryLine() => Sta.Run(() =>
        {
            var runs = Enumerable.Range(0, 400)
                .SelectMany(i => new (string, Color?)[] { ($"function{i} ", Keyword), ($"body{i} ", null) })
                .ToArray();

            byte[] stripped = SyntaxStrip.Remove(Package(runs), Palette);
            var (text, colors) = Reload(stripped);

            Assert.DoesNotContain(Keyword, colors);
            Assert.Contains("function399", text);
            Assert.Contains("body399", text);
            Assert.Equal(400, Enumerable.Range(0, 400).Count(i => text.Contains($"body{i} ")));
        });
    }
}
