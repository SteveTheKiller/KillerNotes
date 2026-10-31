using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// MarkdownWriter is the walker behind Export note as Markdown (F8) and the vault export. A
    /// rich note goes in, markdown text comes out, and a wrong branch produces a file that still
    /// opens but reads differently from the note, so these assert how the text reads in a
    /// markdown parser, not the spacing of the output.
    /// </summary>
    public sealed class MarkdownWriterTests
    {
        private static string Md(FlowDocument doc) =>
            MarkdownWriter.FromDocument(doc, Path.Combine(Path.GetTempPath(), "kn-writer-test.md"), null).Markdown;

        [Fact]
        public void BoldTextKeepsItsTrailingSpaceOutsideTheMarkers() => Sta.Run(() =>
        {
            var p = new Paragraph();
            p.Inlines.Add(new Run("Hello ") { FontWeight = FontWeights.Bold });
            p.Inlines.Add(new Run("world"));

            string md = Md(new FlowDocument(p));

            Assert.Contains("**Hello** world", md);
            Assert.DoesNotContain("**Hello **", md);
        });

        [Fact]
        public void AFormattedRunOfOnlySpacesIsNotWrappedInMarkers() => Sta.Run(() =>
        {
            var p = new Paragraph();
            p.Inlines.Add(new Run("a"));
            p.Inlines.Add(new Run(" ") { FontWeight = FontWeights.Bold });
            p.Inlines.Add(new Run("b"));

            Assert.Contains("a b", Md(new FlowDocument(p)));
        });

        [Theory]
        [InlineData(1, "1. parent\r\n   - child")]
        [InlineData(10, "10. parent\r\n    - child")]
        public void ABulletUnderANumberedItemIsIndentedPastTheNumber(int start, string expected) => Sta.Run(() =>
        {
            var inner = new List { MarkerStyle = TextMarkerStyle.Disc };
            inner.ListItems.Add(new ListItem(new Paragraph(new Run("child"))));
            var outer = new List { MarkerStyle = TextMarkerStyle.Decimal, StartIndex = start };
            var item = new ListItem(new Paragraph(new Run("parent")));
            item.Blocks.Add(inner);
            outer.ListItems.Add(item);

            Assert.Contains(expected, Md(new FlowDocument(outer)));
        });

        [Fact]
        public void ASecondParagraphInANumberedItemLinesUpUnderItsText() => Sta.Run(() =>
        {
            var outer = new List { MarkerStyle = TextMarkerStyle.Decimal };
            var item = new ListItem(new Paragraph(new Run("first")));
            item.Blocks.Add(new Paragraph(new Run("second")));
            outer.ListItems.Add(item);

            Assert.Contains("1. first\r\n\r\n   second", Md(new FlowDocument(outer)));
        });

        [Fact]
        public void ABoldParagraphAtAHeadingSizeIsWrittenAsAHeading() => Sta.Run(() =>
        {
            var heading = new Paragraph(new Run("Title"))
            {
                FontSize = Headings.SizeFor(2, Headings.DefaultBase),
                FontWeight = FontWeights.Bold,
            };
            var doc = new FlowDocument(heading);
            doc.Blocks.Add(new Paragraph(new Run("body")));

            string md = Md(doc);

            Assert.Contains("## Title", md);
            Assert.DoesNotContain("**", md);
        });

        [Theory]
        [InlineData("\u2610 buy milk", "- [ ] buy milk")]
        [InlineData("\u2611 done", "- [x] done")]
        public void ACheckboxLineIsWrittenAsATaskItem(string line, string expected) => Sta.Run(() =>
        {
            var md = Md(new FlowDocument(new Paragraph(new Run(line))));

            Assert.Contains(expected, md);
            Assert.DoesNotContain("\u2610", md);
            Assert.DoesNotContain("\u2611", md);
        });

        [Fact]
        public void TextThatWouldFormEmphasisIsEscaped() => Sta.Run(() =>
        {
            var doc = new FlowDocument(new Paragraph(new Run("*.cs and *.txt")));

            Assert.Contains("\\*.cs and \\*.txt", Md(doc));
        });

        [Fact]
        public void BoldMonospaceWrapsTheCodeSpanNotTheOtherWayAround() => Sta.Run(() =>
        {
            var p = new Paragraph();
            p.Inlines.Add(new Run("see "));
            p.Inlines.Add(new Run("x") { FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold });

            string md = Md(new FlowDocument(p));

            Assert.Contains("**`x`**", md);
            Assert.DoesNotContain("`**x**`", md);
        });

        [Theory]
        [InlineData("javascript:alert(1)")]
        [InlineData("file:///C:/Windows/notepad.exe")]
        public void AHostileLinkTargetNeverReachesTheMarkdown(string target) => Sta.Run(() =>
        {
            var link = new Hyperlink(new Run("label")) { NavigateUri = new Uri(target) };

            string md = Md(new FlowDocument(new Paragraph(link)));

            Assert.Contains("label", md);
            Assert.DoesNotContain("](", md);
        });

        [Fact]
        public void AWikiLinkInARichNoteIsWrittenUnescapedAndTheTextAroundItStillIs() => Sta.Run(() =>
        {
            var p = new Paragraph(new Run("see [[Title]] and [[a/b|label]] but [x] and *y*"));

            string md = Md(new FlowDocument(p));

            Assert.Contains("see [[Title]] and [[a/b|label]] but \\[x] and ", md);
            Assert.DoesNotContain("\\[[", md);
        });

        [Theory]
        [InlineData("ftp://host/file.txt", "label (ftp://host/file.txt)")]
        [InlineData("tel:+15551234", "label (tel:+15551234)")]
        [InlineData("file:///C:/Windows/notepad.exe", "label (file:///C:/Windows/notepad.exe)")]
        public void ARefusedDestinationFollowsTheLabelAsPlainText(string target, string expected) => Sta.Run(() =>
        {
            var link = new Hyperlink(new Run("label")) { NavigateUri = new Uri(target) };

            string md = Md(new FlowDocument(new Paragraph(link)));

            Assert.Contains(expected, md);
            Assert.DoesNotContain("](", md);
        });

        [Fact]
        public void ARelativeDestinationFollowsTheLabelAsPlainText() => Sta.Run(() =>
        {
            var link = new Hyperlink(new Run("label")) { NavigateUri = new Uri("docs/page.html", UriKind.Relative) };

            string md = Md(new FlowDocument(new Paragraph(link)));

            Assert.Contains("label (docs/page.html)", md);
            Assert.DoesNotContain("](", md);
        });

        [Fact]
        public void ARefusedDestinationThatIsTheLabelIsWrittenOnce() => Sta.Run(() =>
        {
            var link = new Hyperlink(new Run("ftp://host/x")) { NavigateUri = new Uri("ftp://host/x") };

            string md = Md(new FlowDocument(new Paragraph(link))).Trim();

            Assert.Equal("ftp://host/x", md);
        });

        [Fact]
        public void AnAllowedLinkKeepsTheDestinationAsWritten() => Sta.Run(() =>
        {
            var link = new Hyperlink(new Run("home")) { NavigateUri = new Uri("https://Example.COM") };

            Assert.Contains("[home](https://Example.COM)", Md(new FlowDocument(new Paragraph(link))));
        });

        [Fact]
        public void AnAllowedLinkKeepsItsEscapedTarget() => Sta.Run(() =>
        {
            var link = new Hyperlink(new Run("share")) { NavigateUri = new Uri("https://host/My%20Site/a%20b") };

            Assert.Contains("[share](https://host/My%20Site/a%20b)", Md(new FlowDocument(new Paragraph(link))));
        });

        [Fact]
        public void StrikethroughSetOnASpanReachesTheText() => Sta.Run(() =>
        {
            var span = new Span(new Run("gone")) { TextDecorations = TextDecorations.Strikethrough };

            Assert.Contains("~~gone~~", Md(new FlowDocument(new Paragraph(span))));
        });

        [Fact]
        public void AnUnderlineElementReachesTheText() => Sta.Run(() =>
        {
            var p = new Paragraph(new Underline(new Run("marked")));

            Assert.Contains("<u>marked</u>", Md(new FlowDocument(p)));
        });

        [Fact]
        public void TwoParagraphsInATableCellDoNotRunTogether() => Sta.Run(() =>
        {
            var cell = new TableCell(new Paragraph(new Run("first line")));
            cell.Blocks.Add(new Paragraph(new Run("second line")));
            var row = new TableRow();
            row.Cells.Add(cell);
            var group = new TableRowGroup();
            group.Rows.Add(row);
            var table = new Table();
            table.RowGroups.Add(group);

            Assert.Contains("first line second line", Md(new FlowDocument(table)));
        });

        [Fact]
        public void ARuleFromMarkdownSurvivesTheExport() => Sta.Run(() =>
        {
            var doc = MarkdownConvert.ToDocument("above\n\n---\n\nbelow", 13);

            Assert.Contains("---", Md(doc));
        });
    }
}
