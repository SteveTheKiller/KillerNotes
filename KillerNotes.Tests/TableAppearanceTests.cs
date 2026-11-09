using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class TableAppearanceTests
    {
        // Pattern lines and header shading are drawn in the theme's colors and follow a theme
        // change, however the brushes are built.
        [Fact]
        public void PatternAndHeaderBrushesFollowTheTheme() => Sta.Run(() =>
        {
            var cell = new TableCell(new Paragraph(new Run("x")));
            var table = new Table { Columns = { new TableColumn() },
                RowGroups = { new TableRowGroup { Rows = { new TableRow { Cells = { cell } } } } } };
            var document = new FlowDocument(table);
            document.Resources["TextBrush"] = Brushes.Red;
            document.Resources["TableHeaderBrush"] = Brushes.Blue;
            TableStyle.ApplyAppearance(table, new TableAppearance { Pattern = TableLinePattern.Dashed, HeaderRow = true });

            Brush Line() => ((GeometryDrawing)((DrawingGroup)((DrawingBrush)cell.BorderBrush).Drawing).Children[0]).Brush;
            Brush Shade() => ((GeometryDrawing)((DrawingBrush)table.RowGroups[0].Rows[0].Background).Drawing).Brush;
            Assert.Equal(Brushes.Red, Line());
            Assert.Equal(Brushes.Blue, Shade());

            document.Resources["TextBrush"] = Brushes.Green;
            document.Resources["TableHeaderBrush"] = Brushes.Yellow;

            Assert.Equal(Brushes.Green, Line());
            Assert.Equal(Brushes.Yellow, Shade());
            Assert.Equal(TableLinePattern.Dashed, TableStyle.ReadAppearance(table).Pattern);
            Assert.True(TableStyle.ReadAppearance(table).HeaderRow);
        });

        [Fact]
        public void RestoringAnExistingTablePreservesItsChosenShading() => Sta.Run(() =>
        {
            var row = new TableRow { FontWeight = FontWeights.Bold, Background = Brushes.Red,
                Cells = { new TableCell(new Paragraph(new Run("Existing heading"))) } };
            var table = new Table { RowGroups = { new TableRowGroup { Rows = { row } } } };
            var document = new FlowDocument(table);
            document.Resources["TextBrush"] = Brushes.Black;
            document.Resources["TableHeaderBrush"] = Brushes.LightGray;
            using var saved = new MemoryStream();
            new TextRange(document.ContentStart, document.ContentEnd).Save(saved, DataFormats.XamlPackage);
            var reopened = new FlowDocument();
            reopened.Resources["TextBrush"] = Brushes.Black;
            reopened.Resources["TableHeaderBrush"] = Brushes.LightGray;
            saved.Position = 0;
            new TextRange(reopened.ContentStart, reopened.ContentEnd).Load(saved, DataFormats.XamlPackage);
            var restored = Assert.IsType<Table>(reopened.Blocks.FirstBlock);

            TableStyle.RestoreGridBrushes(restored);

            Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(restored.RowGroups[0].Rows[0].Background).Color);
            Assert.False(TableStyle.ReadAppearance(restored).HeaderRow);
            Assert.Equal("Existing heading\r\n", new TextRange(restored.ContentStart, restored.ContentEnd).Text);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PrependingStructureKeepsStyleAndMovesHeaderFormatting(bool column) => Sta.Run(() =>
        {
            var table = new Table();
            table.Columns.Add(new TableColumn());
            table.Columns.Add(new TableColumn());
            var group = new TableRowGroup();
            group.Rows.Add(new TableRow { Cells = { new TableCell(new Paragraph(new Run("A"))), new TableCell(new Paragraph(new Run("B"))) } });
            group.Rows.Add(new TableRow { Cells = { new TableCell(new Paragraph(new Run("C"))), new TableCell(new Paragraph(new Run("D"))) } });
            table.RowGroups.Add(group);
            var doc = new FlowDocument(table);
            doc.Resources["TextBrush"] = Brushes.Black;
            doc.Resources["TableHeaderBrush"] = Brushes.LightGray;
            TableStyle.ApplyAppearance(table, new TableAppearance { Thickness = 3, Pattern = TableLinePattern.Dashed,
                Spacing = TableSpacing.Roomy, HeaderRow = true, HeaderColumn = true });

            TableStyle.EditStructure(table, () =>
            {
                if (column)
                {
                    table.Columns.Insert(0, new TableColumn());
                    foreach (TableRow row in group.Rows) row.Cells.Insert(0, new TableCell(new Paragraph()));
                }
                else group.Rows.Insert(0, new TableRow { Cells = { new TableCell(new Paragraph()), new TableCell(new Paragraph()) } });
            });
            TableStyle.Refresh(table);

            TableAppearance actual = TableStyle.ReadAppearance(table);
            Assert.Equal(3, actual.Thickness);
            Assert.Equal(TableLinePattern.Dashed, actual.Pattern);
            Assert.Equal(TableSpacing.Roomy, actual.Spacing);
            Assert.True(actual.HeaderRow);
            Assert.True(actual.HeaderColumn);
            Assert.Equal(FontWeights.Normal, group.Rows[1].Cells[1].FontWeight);
            Assert.Null(group.Rows[1].Cells[1].Background);
            Assert.Contains("D", new TextRange(table.ContentStart, table.ContentEnd).Text);
        });

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("broken")]
        [InlineData("2|Rows|Dashed|3|Roomy|1|1")]
        [InlineData("1|Rows|999|3|Roomy|1|1")]
        [InlineData("1|Rows|Dashed|NaN|Roomy|1|1")]
        [InlineData("1|Rows|Dashed|-1|Roomy|1|1")]
        [InlineData("1|Rows|Dashed|9|Roomy|1|1")]
        [InlineData("1|Rows|Dashed|3|Roomy|invalid|1")]
        public void MissingOrInvalidDefaultsUseReadableGrid(string? saved)
        {
            TableAppearance actual = TableAppearance.FromSetting(saved);
            Assert.Equal(TableLines.Grid, actual.Lines);
            Assert.Equal(TableLinePattern.Solid, actual.Pattern);
            Assert.Equal(1, actual.Thickness);
            Assert.Equal(TableSpacing.Normal, actual.Spacing);
            Assert.False(actual.HeaderRow);
            Assert.False(actual.HeaderColumn);
        }

        [Fact]
        public void DefaultChoiceCanBeSavedAndAppliedToANewTable() => Sta.Run(() =>
        {
            var chosen = new TableAppearance { Lines = TableLines.Rows, Pattern = TableLinePattern.Dotted,
                Thickness = 3, Spacing = TableSpacing.Compact, HeaderRow = true, HeaderColumn = true };
            string savedDefault = chosen.ToSetting();
            TableAppearance restored = TableAppearance.FromSetting(savedDefault);
            var table = new Table();
            table.Columns.Add(new TableColumn());
            table.RowGroups.Add(new TableRowGroup { Rows = {
                new TableRow { Cells = { new TableCell(new Paragraph(new Run("new table"))) } } } });
            var doc = new FlowDocument(table);
            doc.Resources["TextBrush"] = Brushes.Black;
            doc.Resources["TableHeaderBrush"] = Brushes.LightGray;
            TableStyle.ApplyAppearance(table, restored);

            TableAppearance actual = TableStyle.ReadAppearance(table);
            Assert.Equal(TableLines.Rows, actual.Lines);
            Assert.Equal(TableLinePattern.Dotted, actual.Pattern);
            Assert.Equal(3, actual.Thickness);
            Assert.Equal(TableSpacing.Compact, actual.Spacing);
            Assert.True(actual.HeaderRow);
            Assert.True(actual.HeaderColumn);
        });

        [Fact]
        public void StyleAndHeadersSurviveSavingWithoutChangingCellText() => Sta.Run(() =>
        {
            var table = new Table();
            table.Columns.Add(new TableColumn());
            table.Columns.Add(new TableColumn());
            var rows = new TableRowGroup();
            rows.Rows.Add(new TableRow { Cells = { new TableCell(new Paragraph(new Run("A"))), new TableCell(new Paragraph(new Run("B"))) } });
            rows.Rows.Add(new TableRow { Cells = { new TableCell(new Paragraph(new Run("C"))), new TableCell(new Paragraph(new Run("D"))) } });
            table.RowGroups.Add(rows);
            var document = new FlowDocument(table);
            document.Resources["TextBrush"] = Brushes.Black;
            document.Resources["TableHeaderBrush"] = Brushes.LightGray;
            TableStyle.ApplyAppearance(table, new TableAppearance { Lines = TableLines.Outline, Thickness = 3,
                Spacing = TableSpacing.Roomy, HeaderRow = true, HeaderColumn = true });
            using var saved = new MemoryStream();
            new TextRange(document.ContentStart, document.ContentEnd).Save(saved, DataFormats.XamlPackage);
            var loaded = new FlowDocument();
            loaded.Resources["TextBrush"] = Brushes.Black;
            loaded.Resources["TableHeaderBrush"] = Brushes.LightGray;
            saved.Position = 0;
            new TextRange(loaded.ContentStart, loaded.ContentEnd).Load(saved, DataFormats.XamlPackage);
            var restored = Assert.IsType<Table>(loaded.Blocks.FirstBlock);
            TableStyle.RestoreGridBrushes(restored);

            TableAppearance appearance = TableStyle.ReadAppearance(restored);
            Assert.Equal(TableLines.Outline, appearance.Lines);
            Assert.Equal(3, appearance.Thickness);
            Assert.Equal(TableSpacing.Roomy, appearance.Spacing);
            Assert.True(appearance.HeaderRow);
            Assert.True(appearance.HeaderColumn);
            Assert.Equal(new Thickness(3), restored.BorderThickness);
            Assert.Equal(new Thickness(0), restored.RowGroups[0].Rows[1].Cells[1].BorderThickness);
            Assert.Equal(new Thickness(6, 8, 6, 8), restored.RowGroups[0].Rows[1].Cells[1].Padding);
            Assert.Equal(FontWeights.Bold, restored.RowGroups[0].Rows[0].FontWeight);
            Assert.Equal(FontWeights.Bold, restored.RowGroups[0].Rows[1].Cells[0].FontWeight);
            Assert.Equal("D", new TextRange(restored.RowGroups[0].Rows[1].Cells[1].ContentStart,
                restored.RowGroups[0].Rows[1].Cells[1].ContentEnd).Text);
        });

        [Theory]
        [InlineData("Dashed")]
        [InlineData("Dotted")]
        public void PatternedGridSurvivesNoteSaveAndReopen(string patternName) => Sta.Run(() =>
        {
            var pattern = (TableLinePattern)Enum.Parse(typeof(TableLinePattern), patternName);
            var table = new Table();
            table.Columns.Add(new TableColumn());
            table.RowGroups.Add(new TableRowGroup { Rows = {
                new TableRow { Cells = { new TableCell(new Paragraph(new Run("Keep this text"))) } } } });
            var doc = new FlowDocument(table);
            doc.Resources["TextBrush"] = Brushes.Black;

            TableStyle.ApplyAppearance(table, new TableAppearance { Pattern = pattern });
            using var saved = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(saved, DataFormats.XamlPackage);
            var reopened = new FlowDocument();
            reopened.Resources["TextBrush"] = Brushes.Black;
            saved.Position = 0;
            new TextRange(reopened.ContentStart, reopened.ContentEnd).Load(saved, DataFormats.XamlPackage);
            var restored = Assert.IsType<Table>(reopened.Blocks.FirstBlock);
            TableStyle.RestoreGridBrushes(restored);
            Assert.Equal(pattern, TableStyle.ReadAppearance(restored).Pattern);
            Assert.Equal("Keep this text\r\n", new TextRange(restored.ContentStart, restored.ContentEnd).Text);
            Assert.Equal(new Thickness(1, 1, 0, 0), restored.BorderThickness);
            Assert.Equal(new Thickness(0, 0, 1, 1), restored.RowGroups[0].Rows[0].Cells[0].BorderThickness);
            var visual = new DrawingVisual();
            RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 32, 32));
                drawing.DrawRectangle(restored.BorderBrush, null, new Rect(0, 0, 32, 32));
            }
            var rendered = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(visual);
            var pixels = new byte[32 * 32 * 4];
            rendered.CopyPixels(pixels, 32 * 4, 0);
            string expected = patternName == "Dotted" ? "10101010101010101010101010101010" : "11110000111100001111000011110000";
            Assert.Equal(expected, new string(Enumerable.Range(0, 32).Select(x => pixels[x * 4] < 128 ? '1' : '0').ToArray()));
            Assert.Equal(expected, new string(Enumerable.Range(0, 32).Select(y => pixels[y * 32 * 4] < 128 ? '1' : '0').ToArray()));
        });
    }
}
