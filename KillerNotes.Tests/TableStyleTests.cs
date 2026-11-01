using System.Linq;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>Table line styles, column widths and row spacing, as the Table menu applies them.</summary>
    public sealed class TableStyleTests
    {
        private static Table Build(params string[][] rows)
        {
            var table = new Table();
            foreach (string _ in rows[0]) table.Columns.Add(new TableColumn());
            var group = new TableRowGroup();
            foreach (string[] row in rows)
            {
                var tr = new TableRow();
                foreach (string text in row) tr.Cells.Add(new TableCell(new Paragraph(new Run(text))));
                group.Rows.Add(tr);
            }
            table.RowGroups.Add(group);
            return table;
        }

        private static TableCell First(Table table) => table.RowGroups[0].Rows[0].Cells[0];

        [Theory]
        [InlineData("Grid", 1, 1, 0, 1, 1)]
        [InlineData("Thick", 2, 2, 0, 2, 2)]
        [InlineData("Rows", 0, 1, 0, 0, 1)]
        [InlineData("Outline", 1, 1, 1, 0, 0)]
        [InlineData("None", 0, 0, 0, 0, 0)]
        public void ALineStyleSetsTheTableAndEveryCell(string name, double left, double top, double bottom, double cellRight, double cellBottom) => Sta.Run(() =>
        {
            var lines = (TableLines)System.Enum.Parse(typeof(TableLines), name);
            var table = Build(new[] { "a", "b" }, new[] { "c", "d" });

            TableStyle.ApplyLines(table, lines);

            Assert.Equal(new Thickness(left, top, bottom, bottom), table.BorderThickness);
            Assert.All(table.RowGroups[0].Rows.SelectMany(r => r.Cells),
                cell => Assert.Equal(new Thickness(0, 0, cellRight, cellBottom), cell.BorderThickness));
            Assert.Equal(lines, TableStyle.ReadLines(table));
        });

        [Fact]
        public void ACellAddedLaterTakesTheTablesLook() => Sta.Run(() =>
        {
            var table = Build(new[] { "a", "b" });
            TableStyle.ApplyLines(table, TableLines.Rows);
            TableStyle.ApplySpacing(table, TableSpacing.Roomy);
            var added = new TableCell(new Paragraph(new Run("c")));
            table.RowGroups[0].Rows[0].Cells.Add(added);

            TableStyle.Refresh(table);

            Assert.Equal(new Thickness(0, 0, 0, 1), added.BorderThickness);
            Assert.Equal(First(table).Padding, added.Padding);
        });

        [Fact]
        public void SpacingChangesTheRowsThroughCellPadding() => Sta.Run(() =>
        {
            var table = Build(new[] { "a" });

            TableStyle.ApplySpacing(table, TableSpacing.Compact);
            Assert.Equal(new Thickness(6, 1, 6, 1), First(table).Padding);

            TableStyle.ApplySpacing(table, TableSpacing.Roomy);
            Assert.Equal(new Thickness(6, 8, 6, 8), First(table).Padding);
            Assert.Equal(TableSpacing.Roomy, TableStyle.ReadSpacing(table));
        });

        [Fact]
        public void SettingOneColumnFreezesTheOthersAtTheirShare() => Sta.Run(() =>
        {
            var table = Build(new[] { "a", "b", "c" });

            TableStyle.SetColumnWidth(table, 1, 200, 800);

            Assert.Equal(new[] { 300.0, 200.0, 300.0 }, table.Columns.Select(c => c.Width.Value));
            Assert.All(table.Columns, c => Assert.True(c.Width.IsAbsolute));
        });

        [Fact]
        public void FitToContentGivesALongColumnMoreRoom() => Sta.Run(() =>
        {
            var table = Build(new[] { "a", "a much longer line of cell text" }, new[] { "b", "c" });

            TableStyle.FitToContent(table, 14, new System.Windows.Media.FontFamily("Segoe UI"), 1);

            Assert.True(table.Columns[1].Width.Value > table.Columns[0].Width.Value * 3);
            Assert.True(table.Columns[0].Width.IsAbsolute);
        });

        [Fact]
        public void FitToPageSharesTheWidthEvenly() => Sta.Run(() =>
        {
            var table = Build(new[] { "a", "b" });
            TableStyle.SetColumnWidth(table, 0, 90, 800);

            TableStyle.FitToPage(table);

            Assert.All(table.Columns, c => Assert.Equal(new GridLength(1, GridUnitType.Star), c.Width));
        });
    }
}
