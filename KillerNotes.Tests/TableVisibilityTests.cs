using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class TableVisibilityTests
    {
        [Fact]
        public void SavedTablesUseLiveReadableGridWithoutChangingChosenLines() => Sta.Run(() =>
        {
            var table = new Table { BorderBrush = Brushes.DarkBlue, BorderThickness = new Thickness(2) };
            table.Columns.Add(new TableColumn());
            table.RowGroups.Add(new TableRowGroup { Rows = {
                new TableRow { Cells = { new TableCell(new Paragraph()) {
                    BorderBrush = Brushes.DarkBlue, BorderThickness = new Thickness(0, 0, 2, 2) } } } } });
            var original = new FlowDocument(table);
            using var saved = new MemoryStream();
            new TextRange(original.ContentStart, original.ContentEnd).Save(saved, DataFormats.XamlPackage);
            var loaded = new FlowDocument();
            saved.Position = 0;
            new TextRange(loaded.ContentStart, loaded.ContentEnd).Load(saved, DataFormats.XamlPackage);
            loaded.Resources["TextBrush"] = Brushes.Wheat;

            MainWindow.NormalizeThemeColors(loaded);

            var restored = Assert.IsType<Table>(loaded.Blocks.FirstBlock);
            var cell = restored.RowGroups[0].Rows[0].Cells[0];
            Assert.Equal(Colors.Wheat, Assert.IsType<SolidColorBrush>(restored.BorderBrush).Color);
            Assert.Equal(Colors.Wheat, Assert.IsType<SolidColorBrush>(cell.BorderBrush).Color);
            Assert.Equal(new Thickness(2), restored.BorderThickness);
            Assert.Equal(new Thickness(0, 0, 2, 2), cell.BorderThickness);

            loaded.Resources["TextBrush"] = Brushes.Black;
            Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(restored.BorderBrush).Color);
            Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(cell.BorderBrush).Color);
        });
    }
}
