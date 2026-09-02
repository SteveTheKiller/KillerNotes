using System.Linq;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>Headings (1.3.1): the one convention the editor, the outline and the markdown
    /// writer share, and the markdown round trip it enables.</summary>
    public sealed class HeadingsTests
    {
        private const double Base = 13;

        [Fact]
        public void ALevelIsABoldParagraphAtItsScaledSize()
        {
            Assert.Equal(1, Headings.LevelOf(Headings.SizeFor(1, Base), true, Base));
            Assert.Equal(2, Headings.LevelOf(Headings.SizeFor(2, Base), true, Base));
            Assert.Equal(3, Headings.LevelOf(Headings.SizeFor(3, Base), true, Base));
            Assert.Equal(2, Headings.LevelOf(Headings.SizeFor(2, Base) + 0.4, true, Base));   // within tolerance
        }

        [Fact]
        public void PlainBoldOrOddSizesAreNotHeadings()
        {
            Assert.Equal(0, Headings.LevelOf(Headings.SizeFor(1, Base), false, Base));   // big but not bold
            Assert.Equal(0, Headings.LevelOf(Base, true, Base));                          // bold body text
            Assert.Equal(0, Headings.LevelOf(30, true, Base));                            // bold, but no level sits at 30
            Assert.Equal(0, Headings.LevelOf(Headings.SizeFor(4, Base), true, Base));     // h4 is below what the app recognizes
        }

        [Fact]
        public void MarkdownMarkersReadAndRewrite()
        {
            Assert.Equal(2, Headings.LevelOfMarkdown("## Title"));
            Assert.Equal(0, Headings.LevelOfMarkdown("#Title"));      // no space, not a heading
            Assert.Equal(0, Headings.LevelOfMarkdown("Title"));
            Assert.Equal("# Title", Headings.SetMarkdownLevel("Title", 1));
            Assert.Equal("### Title", Headings.SetMarkdownLevel("# Title", 3));
            Assert.Equal("Title", Headings.SetMarkdownLevel("## Title", 0));
        }

        [Fact]
        public void ParagraphLevelNeedsUniformSizeAndBoldThroughout() => Sta.Run(() =>
        {
            var heading = new Paragraph(new Run("Section")) { FontSize = Headings.SizeFor(2, Base), FontWeight = FontWeights.Bold };
            Assert.Equal(2, Headings.LevelOf(heading, Base));

            var mixed = new Paragraph { FontSize = Headings.SizeFor(2, Base) };
            mixed.Inlines.Add(new Bold(new Run("Half ")));
            mixed.Inlines.Add(new Run("bold"));
            Assert.Equal(0, Headings.LevelOf(mixed, Base));

            var empty = new Paragraph { FontSize = Headings.SizeFor(1, Base), FontWeight = FontWeights.Bold };
            Assert.Equal(0, Headings.LevelOf(empty, Base));
        });

        [Fact]
        public void HeadingsSurviveTheMarkdownRoundTrip() => Sta.Run(() =>
        {
            var doc = MarkdownConvert.ToDocument("# Top\n\nBody text.\n\n## Second **loud** part\n", Base);
            string md = MarkdownConvert.FromDocument(doc);
            Assert.Contains("# Top\n", md);
            Assert.Contains("## Second loud part\n", md);   // the heading's own bold is not emphasis
            Assert.Contains("Body text.", md);
            Assert.DoesNotContain("**Top**", md);

            var again = MarkdownConvert.ToDocument(md, Base);
            var levels = again.Blocks.OfType<Paragraph>().Select(p => Headings.LevelOf(p, Base)).ToList();
            Assert.Equal([1, 0, 2], levels);
        });

        [Fact]
        public void AHandMadeBoldLineAtBodySizeStaysAParagraph() => Sta.Run(() =>
        {
            var doc = new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Bold(new Run("Just bold"))));
            string md = MarkdownConvert.FromDocument(doc);
            Assert.Contains("**Just bold**", md);
            Assert.DoesNotContain("# ", md);
        });
    }
}
