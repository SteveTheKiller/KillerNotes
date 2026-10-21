using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>Convert to rich text turns a markdown pipe table into a real table, cells in
    /// order, and it survives being stored and reopened.</summary>
    public sealed class MarkdownTableConversionTests
    {
        [Fact]
        public void PipeTablesBecomeRealTables() => Sta.Run(() =>
        {
            const string md = "Before\n\n| Host | Port |\n| --- | --- |\n| web01 | 443 |\n| [db](https://example.com) | **5432** |\n\nAfter";
            var doc = MainWindow.RichDocumentFromMarkdown(md, 14);

            using var ms = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.XamlPackage);
            var reopened = new FlowDocument();
            ms.Position = 0;
            new TextRange(reopened.ContentStart, reopened.ContentEnd).Load(ms, DataFormats.XamlPackage);

            var table = Assert.Single(reopened.Blocks.OfType<Table>());
            var cells = table.RowGroups.SelectMany(g => g.Rows)
                .Select(r => r.Cells.Select(c => new TextRange(c.ContentStart, c.ContentEnd).Text.Trim()).ToArray())
                .ToArray();
            Assert.Equal(new[] { new[] { "Host", "Port" }, new[] { "web01", "443" }, new[] { "db", "5432" } }, cells);
            string all = new TextRange(reopened.ContentStart, reopened.ContentEnd).Text;
            Assert.True(all.IndexOf("Before") < all.IndexOf("Host") && all.IndexOf("5432") < all.IndexOf("After"));
        });
    }
}
