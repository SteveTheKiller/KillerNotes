using System.Linq;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// MarkdownConvert (1.3.0). The two walks here are the part of the markdown feature that
    /// cannot be checked by looking at the app: a wrong branch produces a document that still
    /// opens and still saves, just with content quietly missing.
    ///
    /// Markdown to rich is total, so those tests assert exact structure. Rich to markdown is
    /// LOSSY by design, so those assert what must survive and what must be reported as lost -
    /// never an exact string, which would just pin today's spacing in place.
    ///
    /// Every body runs on STA (Sta.Run): the XamlPackage round trip throws on xunit's MTA thread.
    /// </summary>
    public class MarkdownConvertTests
    {
        private const double Base = 14;

        // ---- Markdown -> FlowDocument ----

        [Fact]
        public void HeadingBecomesABoldParagraphLargerThanBodyText() => Sta.Run(() =>
        {
            var doc = MarkdownConvert.ToDocument("# Title", Base);
            var p = doc.Blocks.OfType<Paragraph>().First();
            Assert.Equal(FontWeights.Bold, p.FontWeight);
            Assert.True(p.FontSize > Base, "an h1 must be larger than the surrounding body text");
            Assert.Equal("Title", TextOf(p));
        });

        [Fact]
        public void HeadingLevelsGetSmallerAsTheyGetDeeper() => Sta.Run(() =>
        {
            double h1 = FirstParagraph("# a").FontSize;
            double h2 = FirstParagraph("## a").FontSize;
            double h3 = FirstParagraph("### a").FontSize;
            Assert.True(h1 > h2 && h2 > h3, $"expected descending sizes, got {h1}/{h2}/{h3}");
        });

        [Fact]
        public void EmphasisMapsToWeightAndStyleNotToLiteralAsterisks() => Sta.Run(() =>
        {
            var bold = FirstParagraph("**loud**");
            Assert.DoesNotContain("*", TextOf(bold));
            Assert.Contains(bold.Inlines.OfType<Span>(), s => s.FontWeight == FontWeights.Bold);

            var italic = FirstParagraph("*lean*");
            Assert.Contains(italic.Inlines.OfType<Span>(), s => s.FontStyle != FontStyles.Normal);
        });

        [Fact]
        public void BulletListBecomesAListNotParagraphsStartingWithDashes() => Sta.Run(() =>
        {
            var doc = MarkdownConvert.ToDocument("- one\n- two", Base);
            var list = Assert.Single(doc.Blocks.OfType<List>());
            Assert.Equal(2, list.ListItems.Count);
            Assert.DoesNotContain("-", TextOf((Paragraph)list.ListItems.First().Blocks.First()));
        });

        [Fact]
        public void OrderedListKeepsItsStartingNumber() => Sta.Run(() =>
        {
            var list = Assert.Single(MarkdownConvert.ToDocument("3. three\n4. four", Base).Blocks.OfType<List>());
            Assert.Equal(TextMarkerStyle.Decimal, list.MarkerStyle);
            Assert.Equal(3, list.StartIndex);
        });

        [Fact]
        public void FencedCodeKeepsEveryLineAsOneBlock() => Sta.Run(() =>
        {
            var doc = MarkdownConvert.ToDocument("```\nline one\nline two\n```", Base);
            var p = Assert.Single(doc.Blocks.OfType<Paragraph>());
            // One paragraph, the lines joined by LineBreak rather than split into two blocks.
            Assert.Single(doc.Blocks);
            Assert.Contains("line one", TextOf(p));
            Assert.Contains("line two", TextOf(p));
            Assert.Contains(p.Inlines, i => i is LineBreak);
        });

        [Fact]
        public void SafeLinkBecomesAHyperlink() => Sta.Run(() =>
        {
            var p = FirstParagraph("[docs](https://killernotes.net)");
            var link = Assert.Single(p.Inlines.OfType<Hyperlink>());
            Assert.Equal("https://killernotes.net/", link.NavigateUri!.ToString());
            Assert.Equal("docs", TextOf(link.Inlines));
        });

        // The security rule from Links.cs: only http, https and mailto ever become clickable.
        // A markdown note is untrusted input the moment it can arrive by import or from a vault
        // folder, so link SYNTAX must not be enough to produce a live link.
        [Theory]
        [InlineData("javascript:alert(1)")]
        [InlineData("file:///C:/Windows/System32/cmd.exe")]
        [InlineData("vbscript:msgbox")]
        public void UnsafeSchemeKeepsItsTextButDoesNotBecomeAHyperlink(string url) => Sta.Run(() =>
        {
            var p = FirstParagraph($"[click me]({url})");
            Assert.Empty(p.Inlines.OfType<Hyperlink>());
            Assert.Contains("click me", TextOf(p));
        });

        [Fact]
        public void ImageIsNotFetchedAndLeavesItsAltTextBehind() => Sta.Run(() =>
        {
            var p = FirstParagraph("![a diagram](https://example.com/x.png)");
            Assert.Empty(p.Inlines.OfType<InlineUIContainer>());
            Assert.Contains("a diagram", TextOf(p));
        });

        [Fact]
        public void EmptyMarkdownStillProducesACaretHome() => Sta.Run(() =>
        {
            var doc = MarkdownConvert.ToDocument("", Base);
            Assert.NotEmpty(doc.Blocks);
        });

        // ---- FlowDocument -> markdown ----

        [Fact]
        public void BoldAndItalicRunsComeBackAsMarkers() => Sta.Run(() =>
        {
            var doc = new FlowDocument();
            var p = new Paragraph();
            p.Inlines.Add(new Run("plain "));
            p.Inlines.Add(new Run("loud") { FontWeight = FontWeights.Bold });
            doc.Blocks.Add(p);

            string md = MarkdownConvert.FromDocument(doc);
            Assert.Contains("**loud**", md);
            Assert.Contains("plain", md);
        });

        [Fact]
        public void EmphasisMarkersHugTheTextSoParsersSeeThem() => Sta.Run(() =>
        {
            var doc = new FlowDocument();
            var p = new Paragraph();
            // Trailing space INSIDE the bold run: "** bold **" is literal asterisks everywhere.
            p.Inlines.Add(new Run(" loud ") { FontWeight = FontWeights.Bold });
            doc.Blocks.Add(p);

            // Asserted as the whole line, so this says the markers TOUCH the word rather than
            // banning a space next to an asterisk anywhere in the output. The blanket
            // DoesNotContain(" **") this used to carry forbade correct markdown and contradicted
            // BoldAndItalicRunsComeBackAsMarkers above, whose own output is "plain **loud**".
            // The space that was inside the run ends up outside the markers, where it is ordinary
            // leading whitespace; the trailing one goes entirely, since trailing spaces are a hard
            // line break in markdown.
            Assert.Equal("**loud**", MarkdownConvert.FromDocument(doc).Trim());
        });

        [Fact]
        public void HyperlinkComesBackAsMarkdownLinkSyntax() => Sta.Run(() =>
        {
            var doc = new FlowDocument();
            var p = new Paragraph();
            var link = new Hyperlink(new Run("docs")) { NavigateUri = new System.Uri("https://killernotes.net") };
            p.Inlines.Add(link);
            doc.Blocks.Add(p);

            Assert.Contains("[docs](https://killernotes.net", MarkdownConvert.FromDocument(doc));
        });

        [Fact]
        public void OutputEndsWithExactlyOneNewlineAndNoRunsOfBlankLines() => Sta.Run(() =>
        {
            var doc = new FlowDocument();
            for (int i = 0; i < 3; i++) doc.Blocks.Add(new Paragraph(new Run($"line {i}")));

            string md = MarkdownConvert.FromDocument(doc);
            Assert.EndsWith("\n", md);
            Assert.DoesNotContain("\n\n\n", md);
            Assert.DoesNotContain("\r", md);
        });

        // ---- Loss reporting ----

        [Fact]
        public void CleanDocumentReportsNoLosses() => Sta.Run(() =>
        {
            var doc = new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Run("just words")));
            Assert.Empty(MarkdownConvert.Losses(doc));
        });

        [Fact]
        public void TableIsReportedAsALossAndItsTextStillSurvives() => Sta.Run(() =>
        {
            var doc = new FlowDocument();
            var table = new Table();
            var group = new TableRowGroup();
            var row = new TableRow();
            row.Cells.Add(new TableCell(new Paragraph(new Run("cell text"))));
            group.Rows.Add(row);
            table.RowGroups.Add(group);
            doc.Blocks.Add(table);

            Assert.Contains("Str_MdLoss_Tables", MarkdownConvert.Losses(doc));
            // Lossy, but never silently empty: the words must still come out.
            Assert.Contains("cell text", MarkdownConvert.FromDocument(doc));
        });

        [Fact]
        public void ColoredTextIsReportedAsALoss() => Sta.Run(() =>
        {
            var doc = new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Run("red") { Foreground = System.Windows.Media.Brushes.Red }));
            Assert.Contains("Str_MdLoss_Colors", MarkdownConvert.Losses(doc));
        });

        // ---- Round trip ----

        [Fact]
        public void MarkdownSurvivesARoundTripThroughTheDocument() => Sta.Run(() =>
        {
            const string source = "# Heading\n\nSome **bold** and *italic* text.\n\n- one\n- two\n";

            string result = MarkdownConvert.FromDocument(MarkdownConvert.ToDocument(source, Base));

            Assert.Contains("Heading", result);
            Assert.Contains("**bold**", result);
            Assert.Contains("*italic*", result);
            Assert.Contains("- one", result);
            Assert.Contains("- two", result);
        });

        // ---- helpers ----

        private static Paragraph FirstParagraph(string markdown) =>
            MarkdownConvert.ToDocument(markdown, Base).Blocks.OfType<Paragraph>().First();

        private static string TextOf(Paragraph p) => TextOf(p.Inlines);

        private static string TextOf(InlineCollection inlines) =>
            new TextRange(inlines.FirstInline.ContentStart, inlines.LastInline.ContentEnd).Text;
    }
}
