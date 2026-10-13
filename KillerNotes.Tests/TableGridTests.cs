using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Table paste input: copied HTML tables and tab, comma or space separated text read as a
    /// grid of rows and cells (Services/TableGrid.cs).
    /// </summary>
    public sealed class TableGridTests
    {
        [Fact]
        public void BrowserTableKeepsEveryRowAndCell()
        {
            var g = TableGrid.FromHtml(
                "<table><thead><tr><th>Host</th><th>IP</th></tr></thead>" +
                "<tbody><tr><td>fw01</td><td>10.0.0.1</td></tr><tr><td>sw01</td><td>10.0.0.2</td></tr></tbody></table>",
                tableOnly: true);
            Assert.NotNull(g);
            Assert.Equal(3, g!.Count);
            Assert.Equal(new[] { "Host", "IP" }, g[0]);
            Assert.Equal(new[] { "sw01", "10.0.0.2" }, g[2]);
        }

        [Fact]
        public void ExcelFragmentWithoutTableTagIsATable()
        {
            var g = TableGrid.FromHtml(
                "<col width=64><tr height=20><td class=xl65>a&nbsp;1</td><td>b</td></tr>\r\n<tr><td>c</td><td></td></tr>",
                tableOnly: true);
            Assert.NotNull(g);
            Assert.Equal(new[] { "a 1", "b" }, g![0]);
            Assert.Equal(new[] { "c", "" }, g[1]);
        }

        [Fact]
        public void ColspanAndRaggedRowsStayAligned()
        {
            var g = TableGrid.FromHtml(
                "<table><tr><td colspan=\"2\">wide</td><td>x</td></tr><tr><td>1</td></tr></table>", tableOnly: true);
            Assert.Equal(new[] { "wide", "", "x" }, g![0]);
            Assert.Equal(new[] { "1", "", "" }, g[1]);
        }

        [Fact]
        public void SourceLineBreaksCollapseButBrIsKept()
        {
            var g = TableGrid.FromHtml(
                "<table><tr><td>one\r\n   two</td><td>a<br>b</td></tr><tr><td>x</td><td>y</td></tr></table>", tableOnly: true);
            Assert.Equal("one two", g![0][0]);
            Assert.Equal("a\nb", g[0][1]);
        }

        [Fact]
        public void DocumentWithTextAroundTheTableIsLeftToNativePaste()
        {
            const string html = "<p>Intro</p><table><tr><td>a</td><td>b</td></tr></table><p>After</p>";
            Assert.Null(TableGrid.FromHtml(html, tableOnly: true));
            Assert.NotNull(TableGrid.FromHtml(html, tableOnly: false));
        }

        [Fact]
        public void HtmlWithoutATableIsNotAGrid()
        {
            Assert.Null(TableGrid.FromHtml("<p>Just <b>text</b></p>", tableOnly: false));
            Assert.Null(TableGrid.FromHtml("<table><tr><td>one cell</td></tr></table>", tableOnly: true));
        }

        [Fact]
        public void TabSeparatedTextSplitsOnTabs()
        {
            var g = TableGrid.FromText("Name\tRole\r\nAmy\tTech lead\r\nBo\t\r\n");
            Assert.Equal(3, g!.Count);
            Assert.Equal(new[] { "Amy", "Tech lead" }, g[1]);
            Assert.Equal(new[] { "Bo", "" }, g[2]);
        }

        [Fact]
        public void CsvHandlesQuotesCommasAndLineBreaks()
        {
            var g = TableGrid.FromText("a, b,\"c, d\"\n\"say \"\"hi\"\"\",\"two\nlines\",z");
            Assert.Equal(new[] { "a", "b", "c, d" }, g![0]);
            Assert.Equal(new[] { "say \"hi\"", "two\nlines", "z" }, g[1]);
        }

        [Fact]
        public void SingleCsvRowFillsAcross()
        {
            Assert.Equal(new[] { "1", "2", "3" }, TableGrid.FromText("1,2,3")![0]);
        }

        [Fact]
        public void LinedUpColumnsSplitOnWideGaps()
        {
            var g = TableGrid.FromText("Port   Service    State\n22     ssh        open\n3389   ms-wbt     closed");
            Assert.Equal(3, g!.Count);
            Assert.Equal(new[] { "3389", "ms-wbt", "closed" }, g[2]);
        }

        [Fact]
        public void SingleSpacesSplitOnlyAcrossSeveralLines()
        {
            Assert.Null(TableGrid.FromText("just a sentence"));
            var g = TableGrid.FromText("a b c\nd e f");
            Assert.Equal(new[] { "d", "e", "f" }, g![1]);
        }

        [Fact]
        public void OneCellIsNotAGrid()
        {
            Assert.Null(TableGrid.FromText("word"));
            Assert.Null(TableGrid.FromText("\r\n"));
        }
    }
}
