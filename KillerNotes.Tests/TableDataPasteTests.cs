using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class TableDataPasteTests
    {
        [Fact]
        public void GrowingADataPasteKeepsTheHeaderColumnAfterReopening() => Sta.Run(() =>
        {
            var table = new Table();
            table.Columns.Add(new TableColumn());
            var group = new TableRowGroup();
            group.Rows.Add(new TableRow { Cells = { new TableCell(new Paragraph(new Run("old A"))) } });
            group.Rows.Add(new TableRow { Cells = { new TableCell(new Paragraph(new Run("old B"))) } });
            table.RowGroups.Add(group);
            var document = new FlowDocument(table);
            document.Resources["TextBrush"] = Brushes.Black;
            document.Resources["TableHeaderBrush"] = Brushes.LightGray;
            TableStyle.ApplyAppearance(table, new TableAppearance { HeaderColumn = true, Pattern = TableLinePattern.Dashed, Thickness = 3 });
            var editor = new RichTextBox(document);
            var shell = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            shell.Editor = editor;

            shell.FillTable(group.Rows[1].Cells[0], new List<string[]> { new[] { "pasted B" }, new[] { "pasted C" }, new[] { "pasted D" } });

            Assert.Equal(4, group.Rows.Count);
            Assert.True(TableStyle.ReadAppearance(table).HeaderColumn);
            using var package = new MemoryStream();
            new TextRange(document.ContentStart, document.ContentEnd).Save(package, DataFormats.XamlPackage);
            package.Position = 0;
            var reopened = new FlowDocument();
            new TextRange(reopened.ContentStart, reopened.ContentEnd).Load(package, DataFormats.XamlPackage);
            var restored = Assert.IsType<Table>(reopened.Blocks.FirstBlock);
            Assert.True(TableStyle.ReadAppearance(restored).HeaderColumn);
            Assert.Equal(3, TableStyle.ReadAppearance(restored).Thickness);
            Assert.Equal(TableLinePattern.Dashed, TableStyle.ReadAppearance(restored).Pattern);
            Assert.Contains("pasted D", new TextRange(restored.ContentStart, restored.ContentEnd).Text);
        });
    }
}
