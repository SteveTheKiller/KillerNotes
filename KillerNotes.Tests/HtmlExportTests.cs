using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// What HtmlExport keeps of a note's structure, in the standalone page and in the clipboard
    /// fragment, which share one walker.
    /// </summary>
    public sealed class HtmlExportTests
    {
        [Fact]
        public void TwoParagraphsInOneListItemStayApart() => Sta.Run(() =>
        {
            var list = new List { MarkerStyle = TextMarkerStyle.Disc };
            var item = new ListItem(new Paragraph(new Run("first line")));
            item.Blocks.Add(new Paragraph(new Run("second line")));
            list.ListItems.Add(item);

            string html = HtmlExport.FragmentFromDocument(new FlowDocument(list));

            Assert.Contains("<p>first line</p>", html);
            Assert.Contains("<p>second line</p>", html);
        });

        [Fact]
        public void TwoParagraphsInOneTableCellStayApart() => Sta.Run(() =>
        {
            var cell = new TableCell(new Paragraph(new Run("first line")));
            cell.Blocks.Add(new Paragraph(new Run("second line")));
            var row = new TableRow();
            row.Cells.Add(cell);
            var group = new TableRowGroup();
            group.Rows.Add(row);
            var table = new Table();
            table.RowGroups.Add(group);

            string html = HtmlExport.FragmentFromDocument(new FlowDocument(table));

            Assert.Contains("<p>first line</p>", html);
            Assert.Contains("<p>second line</p>", html);
        });

        [Fact]
        public void AParagraphAloneInAListItemIsNotWrapped() => Sta.Run(() =>
        {
            var list = new List { MarkerStyle = TextMarkerStyle.Disc };
            list.ListItems.Add(new ListItem(new Paragraph(new Run("only"))));

            Assert.Equal("<ul><li>only</li></ul>", HtmlExport.FragmentFromDocument(new FlowDocument(list)));
        });

        [Fact]
        public void AnOrderedListThatContinuesAtFiveKeepsItsStart() => Sta.Run(() =>
        {
            var list = new List { MarkerStyle = TextMarkerStyle.Decimal, StartIndex = 5 };
            list.ListItems.Add(new ListItem(new Paragraph(new Run("five"))));

            Assert.StartsWith("<ol start=\"5\">", HtmlExport.FragmentFromDocument(new FlowDocument(list)));
        });

        [Fact]
        public void AnOrderedListFromOneHasNoStartAttribute() => Sta.Run(() =>
        {
            var list = new List { MarkerStyle = TextMarkerStyle.Decimal };
            list.ListItems.Add(new ListItem(new Paragraph(new Run("one"))));

            Assert.StartsWith("<ol>", HtmlExport.FragmentFromDocument(new FlowDocument(list)));
        });

        [Fact]
        public void StrikethroughSetOnASpanReachesTheText() => Sta.Run(() =>
        {
            var span = new Span(new Run("gone")) { TextDecorations = TextDecorations.Strikethrough };

            Assert.Contains("<s>gone</s>", HtmlExport.FragmentFromDocument(new FlowDocument(new Paragraph(span))));
        });

        [Fact]
        public void AnUnderlineElementReachesTheText() => Sta.Run(() =>
        {
            var p = new Paragraph(new Underline(new Run("marked")));

            Assert.Contains("<u>marked</u>", HtmlExport.FragmentFromDocument(new FlowDocument(p)));
        });
    }
}
