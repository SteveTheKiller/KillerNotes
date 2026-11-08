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
    }
}
