using System.Linq;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class TableStructureRegressionTests
    {
        [Theory]
        [InlineData(false, "Thick", "Roomy", 2, 2, 8)]
        [InlineData(true, "Thick", "Roomy", 2, 2, 8)]
        [InlineData(false, "Rows", "Compact", 0, 1, 1)]
        [InlineData(true, "None", "Compact", 0, 0, 1)]
        [InlineData(false, "Outline", "Roomy", 0, 0, 8)]
        public void PrependingCellsPreservesChosenLinesAndSpacing(bool column, string lines, string spacing,
            double right, double bottom, double verticalPadding) => Sta.Run(() =>
        {
            var table = new Table();
            table.Columns.Add(new TableColumn());
            var group = new TableRowGroup();
            group.Rows.Add(new TableRow { Cells = { new TableCell(new Paragraph(new Run("existing"))) } });
            table.RowGroups.Add(group);
            TableStyle.ApplyLines(table, (TableLines)System.Enum.Parse(typeof(TableLines), lines));
            TableStyle.ApplySpacing(table, (TableSpacing)System.Enum.Parse(typeof(TableSpacing), spacing));
            Thickness outer = table.BorderThickness;

            TableStyle.EditStructure(table, () =>
            {
                var fresh = new TableCell(new Paragraph()) {
                    BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(6, 3, 6, 3) };
                if (column)
                {
                    table.Columns.Insert(0, new TableColumn());
                    group.Rows[0].Cells.Insert(0, fresh);
                }
                else group.Rows.Insert(0, new TableRow { Cells = { fresh } });
            });

            Assert.Equal(outer, table.BorderThickness);
            Assert.All(group.Rows.SelectMany(r => r.Cells), cell =>
            {
                Assert.Equal(new Thickness(0, 0, right, bottom), cell.BorderThickness);
                Assert.Equal(new Thickness(6, verticalPadding, 6, verticalPadding), cell.Padding);
            });
        });
    
        // Header paragraphs carry Bold themselves (the editor styles every Paragraph Normal), so
        // a header row or column that a new row or column pushes aside must lose that Bold too.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void APushedAsideHeaderLosesItsBold(bool column) => Sta.Run(() =>
        {
            var table = new Table();
            table.Columns.Add(new TableColumn());
            table.Columns.Add(new TableColumn());
            var group = new TableRowGroup();
            for (int r = 0; r < 2; r++)
                group.Rows.Add(new TableRow { Cells = {
                    new TableCell(new Paragraph(new Run("a" + r))), new TableCell(new Paragraph(new Run("b" + r))) } });
            table.RowGroups.Add(group);
            _ = new FlowDocument(table);
            TableStyle.ApplyAppearance(table, new TableAppearance { HeaderRow = !column, HeaderColumn = column });
            var oldHeader = column ? group.Rows.Select(r => r.Cells[0]).ToArray() : group.Rows[0].Cells.ToArray();

            TableStyle.EditStructure(table, () =>
            {
                if (column)
                {
                    table.Columns.Insert(0, new TableColumn());
                    foreach (TableRow row in group.Rows) row.Cells.Insert(0, new TableCell(new Paragraph()));
                }
                else group.Rows.Insert(0, new TableRow { Cells = { new TableCell(new Paragraph()), new TableCell(new Paragraph()) } });
            });

            Assert.All(oldHeader.SelectMany(c => c.Blocks.OfType<Paragraph>()), p =>
                Assert.Equal(DependencyProperty.UnsetValue, p.ReadLocalValue(TextElement.FontWeightProperty)));
            var newHeader = column ? group.Rows.Select(r => r.Cells[0]) : group.Rows[0].Cells;
            Assert.All(newHeader.SelectMany(c => c.Blocks.OfType<Paragraph>()), p => Assert.Equal(FontWeights.Bold, p.FontWeight));
        });
    }
}
