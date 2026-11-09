using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>A table copied inside KillerNotes keeps its Table Style when it is pasted; tables
    /// from Excel and the web still take the plain family grid (#11).</summary>
    public sealed class TablePasteStyleTests
    {
        [Fact]
        public void AKillerNotesTableKeepsItsStyleWhenPasted() => Sta.Run(() =>
        {
            var chosen = new TableAppearance { Lines = TableLines.Rows, Pattern = TableLinePattern.Dotted,
                Thickness = 2, HeaderColumn = true };
            Table pasted = RoundTrip(chosen);

            KillerNotes.Shell.MainWindow.NormalizePastedBlock(pasted, keepTableStyle: true);

            TableAppearance after = TableStyle.ReadAppearance(pasted);
            Assert.Equal(chosen.Lines, after.Lines);
            Assert.Equal(chosen.Pattern, after.Pattern);
            Assert.Equal(chosen.Thickness, after.Thickness);
            Assert.True(after.HeaderColumn);
        });

        [Fact]
        public void AnOutsideTableStillTakesThePlainGrid() => Sta.Run(() =>
        {
            Table pasted = RoundTrip(new TableAppearance { Lines = TableLines.Rows, Pattern = TableLinePattern.Dashed });

            KillerNotes.Shell.MainWindow.NormalizePastedBlock(pasted, keepTableStyle: false);

            TableAppearance after = TableStyle.ReadAppearance(pasted);
            Assert.Equal(TableLines.Grid, after.Lines);
            Assert.Equal(TableLinePattern.Solid, after.Pattern);
        });

        private static Table RoundTrip(TableAppearance appearance)
        {
            var table = new Table();
            var group = new TableRowGroup();
            for (int r = 0; r < 2; r++)
            {
                var row = new TableRow();
                for (int c = 0; c < 2; c++) row.Cells.Add(new TableCell(new Paragraph(new Run(r + "" + c))));
                group.Rows.Add(row);
            }
            table.Columns.Add(new TableColumn());
            table.Columns.Add(new TableColumn());
            table.RowGroups.Add(group);
            var document = new FlowDocument(table);
            document.Resources["TextBrush"] = Brushes.Black;
            document.Resources["TableHeaderBrush"] = Brushes.LightGray;
            TableStyle.ApplyAppearance(table, appearance);
            using var copied = new MemoryStream();
            new TextRange(document.ContentStart, document.ContentEnd).Save(copied, DataFormats.XamlPackage);
            var target = new FlowDocument();
            target.Resources["TextBrush"] = Brushes.Black;
            target.Resources["TableHeaderBrush"] = Brushes.LightGray;
            copied.Position = 0;
            new TextRange(target.ContentStart, target.ContentEnd).Load(copied, DataFormats.XamlPackage);
            return target.Blocks.OfType<Table>().Single();
        }
    }
}
