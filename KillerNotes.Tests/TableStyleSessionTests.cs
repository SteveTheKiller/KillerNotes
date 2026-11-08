using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class TableStyleSessionTests
    {
        [Fact]
        public void AcceptingHeaderEmphasisMatchesThePreviewAfterManualUnbolding() => Sta.Run(() =>
        {
            var run = new Run("heading");
            var table = new Table();
            table.Columns.Add(new TableColumn());
            table.RowGroups.Add(new TableRowGroup { Rows = { new TableRow { Cells = { new TableCell(new Paragraph(run)) } } } });
            var document = new FlowDocument(table);
            document.Resources["TextBrush"] = Brushes.Black;
            document.Resources["TableHeaderBrush"] = Brushes.LightGray;
            TableStyle.ApplyAppearance(table, new TableAppearance { HeaderRow = true });
            run.FontWeight = FontWeights.Normal;
            var editor = new RichTextBox(document);
            using var session = new TableStyleSession(editor, table);
            session.Preview(TableStyle.ReadAppearance(table));

            Assert.Equal(TableStyleCommitResult.Applied, session.Commit());
            Assert.Equal(FontWeights.Bold, run.FontWeight);
            Assert.Equal("heading", run.Text);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void HeaderPreviewBoldsExplicitNormalTextAndCancelRestoresIt(bool column) => Sta.Run(() =>
        {
            var run = new Run("Keep this heading") { FontWeight = FontWeights.Normal };
            var paragraph = new Paragraph(new Span(run) { FontWeight = FontWeights.Normal }) { FontWeight = FontWeights.Normal };
            var table = new Table();
            table.Columns.Add(new TableColumn());
            table.RowGroups.Add(new TableRowGroup { Rows = { new TableRow { Cells = {
                new TableCell(paragraph) { FontWeight = FontWeights.Normal } } } } });
            var document = new FlowDocument(table) { FontWeight = FontWeights.Bold };
            document.Resources["TextBrush"] = Brushes.Black;
            document.Resources["TableHeaderBrush"] = Brushes.LightGray;
            var editor = new RichTextBox(document);
            using var session = new TableStyleSession(editor, table);

            session.Preview(new TableAppearance { HeaderRow = !column, HeaderColumn = column });
            var preview = Assert.IsType<Table>(session.PreviewDocument.Blocks.FirstBlock);
            var start = preview.RowGroups[0].Rows[0].Cells[0].ContentStart.GetNextInsertionPosition(LogicalDirection.Forward)!;
            Assert.Equal(FontWeights.Bold, start.Parent.GetValue(TextElement.FontWeightProperty));
            Assert.Equal(FontWeights.Normal, run.FontWeight);
            session.Cancel();
            Assert.Equal(FontWeights.Normal, start.Parent.GetValue(TextElement.FontWeightProperty));
            Assert.Contains("Keep this heading", new TextRange(session.PreviewDocument.ContentStart, session.PreviewDocument.ContentEnd).Text);
        });

        [Fact]
        public void PreviewUsesTheCompleteRichPackageForEmbeddedMedia() => Sta.Run(() =>
        {
            var cell = new TableCell(new Paragraph(new InlineUIContainer(new Border { Child = new TextBlock { Text = "recording" } })));
            var table = new Table();
            table.Columns.Add(new TableColumn());
            table.RowGroups.Add(new TableRowGroup { Rows = { new TableRow { Cells = { cell } } } });
            var editor = new RichTextBox(new FlowDocument(table));
            var pictureCell = new TableCell(new Paragraph(new InlineUIContainer(new Image {
                Source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4),
                ToolTip = "recording preview" })));
            var picturedTable = new Table();
            picturedTable.Columns.Add(new TableColumn());
            picturedTable.RowGroups.Add(new TableRowGroup { Rows = { new TableRow { Cells = { pictureCell } } } });
            var pictured = new FlowDocument(picturedTable);
            using var package = new MemoryStream();
            new TextRange(pictured.ContentStart, pictured.ContentEnd).Save(package, DataFormats.XamlPackage);

            using var session = new TableStyleSession(editor, table, package.ToArray());

            var previewTable = Assert.IsType<Table>(session.PreviewDocument.Blocks.FirstBlock);
            var paragraph = Assert.IsType<Paragraph>(previewTable.RowGroups[0].Rows[0].Cells[0].Blocks.FirstBlock);
            var embedded = Assert.IsType<InlineUIContainer>(paragraph.Inlines.FirstInline);
            Assert.IsType<Image>(embedded.Child);
            Assert.IsType<Border>(Assert.IsType<InlineUIContainer>(Assert.IsType<Paragraph>(cell.Blocks.FirstBlock).Inlines.FirstInline).Child);
        });

        [Fact]
        public void AcceptingTheCurrentStyleDoesNotEditTheTable() => Sta.Run(() =>
        {
            var table = new Table();
            table.Columns.Add(new TableColumn());
            table.RowGroups.Add(new TableRowGroup { Rows = {
                new TableRow { Cells = { new TableCell(new Paragraph(new Run("unchanged"))) } } } });
            var document = new FlowDocument(table);
            document.Resources["TextBrush"] = Brushes.Black;
            document.Resources["TableHeaderBrush"] = Brushes.LightGray;
            TableStyle.ApplyAppearance(table, new TableAppearance());
            var editor = new RichTextBox(document);
            using var session = new TableStyleSession(editor, table);
            session.Preview(TableStyle.ReadAppearance(table));

            Assert.Equal(TableStyleCommitResult.Unchanged, session.Commit());
            Assert.Equal("unchanged", new TextRange(table.ContentStart, table.ContentEnd).Text.TrimEnd('\r', '\n'));
        });

        [Fact]
        public void SwitchingTheNoteRejectsThePendingTableStyle() => Sta.Run(() =>
        {
            var table = new Table();
            table.Columns.Add(new TableColumn());
            table.RowGroups.Add(new TableRowGroup { Rows = {
                new TableRow { Cells = { new TableCell(new Paragraph(new Run("old table"))) } } } });
            var document = new FlowDocument(table);
            document.Resources["TextBrush"] = Brushes.Black;
            document.Resources["TableHeaderBrush"] = Brushes.LightGray;
            var editor = new RichTextBox(document);
            using var session = new TableStyleSession(editor, table);
            session.Preview(new TableAppearance { HeaderRow = true, Thickness = 3 });
            document.Blocks.Clear();
            document.Blocks.Add(new Paragraph(new Run("another note")));

            Assert.Equal(TableStyleCommitResult.TargetChanged, session.Commit());
            Assert.Equal("another note\r\n", new TextRange(document.ContentStart, document.ContentEnd).Text);
        });

        [Theory]
        [InlineData("ColumnWidth")]
        [InlineData("FitPage")]
        [InlineData("FitContent")]
        public void ColumnSizingPreviewsCancelsAndAcceptsAsOneChange(string modeName) => Sta.Run(() =>
        {
            var table = new Table();
            table.Columns.Add(new TableColumn { Width = new GridLength(80) });
            table.Columns.Add(new TableColumn { Width = new GridLength(120) });
            table.RowGroups.Add(new TableRowGroup { Rows = { new TableRow { Cells = {
                new TableCell(new Paragraph(new Run("A"))), new TableCell(new Paragraph(new Run("B"))) } } } });
            var document = new FlowDocument(table) { PagePadding = new Thickness(0) };
            document.Resources["TextBrush"] = Brushes.Black;
            document.Resources["TableHeaderBrush"] = Brushes.LightGray;
            var editor = new RichTextBox(document);
            InputMethod.SetIsInputMethodEnabled(editor, false);
            var host = new Window { Content = editor, Width = 320, Height = 240, Opacity = 0,
                ShowInTaskbar = false, ShowActivated = false };
            host.Show();
            try
            {
                editor.IsUndoEnabled = false;
                editor.IsUndoEnabled = true;
                using var session = new TableStyleSession(editor, table);
                var sizing = new TableSizing { Mode = (TableSizingMode)Enum.Parse(typeof(TableSizingMode), modeName), Column = 1, Width = 180 };
                void AssertSizing(Table target)
                {
                    if (modeName == "ColumnWidth") Assert.Equal(180, target.Columns[1].Width.Value);
                    else if (modeName == "FitPage") Assert.Equal(new GridLength(1, GridUnitType.Star), target.Columns[1].Width);
                    else
                    {
                        Assert.True(target.Columns[1].Width.IsAbsolute);
                        Assert.InRange(target.Columns[1].Width.Value, 24, 79);
                    }
                }
                session.Preview(new TableAppearance(), sizing);
                var preview = Assert.IsType<Table>(session.PreviewDocument.Blocks.FirstBlock);
                AssertSizing(preview);
                Assert.True(table.Columns[1].Width.Value == 120, "Preview changed the source width.");
                session.Cancel();
                Assert.True(preview.Columns[1].Width.Value == 120, "Cancel did not restore preview width.");
                session.Preview(new TableAppearance(), sizing);
                session.Commit();
                AssertSizing(table);
                editor.Undo();
                var restored = Assert.IsType<Table>(document.Blocks.FirstBlock);
                Assert.Equal(80, restored.Columns[0].Width.Value);
                Assert.True(restored.Columns[1].Width.Value == 120, "Undo did not restore source width.");
                Assert.Equal(new Thickness(0), restored.BorderThickness);
                Assert.Equal("A", new TextRange(restored.RowGroups[0].Rows[0].Cells[0].ContentStart,
                    restored.RowGroups[0].Rows[0].Cells[0].ContentEnd).Text);
                Assert.Equal("B", new TextRange(restored.RowGroups[0].Rows[0].Cells[1].ContentStart,
                    restored.RowGroups[0].Rows[0].Cells[1].ContentEnd).Text);
            }
            finally { host.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });

        [Fact]
        public void AcceptMakesAllPreviewChoicesOneUndoableChange() => Sta.Run(() =>
        {
            var table = new Table();
            table.Columns.Add(new TableColumn());
            table.RowGroups.Add(new TableRowGroup { Rows = {
                new TableRow { Cells = { new TableCell(new Paragraph(new Run("cell"))) } } } });
            var document = new FlowDocument(table);
            document.Resources["TextBrush"] = Brushes.Black;
            document.Resources["TableHeaderBrush"] = Brushes.LightGray;
            var tail = new Paragraph(new Run("before"));
            document.Blocks.Add(tail);
            var editor = new RichTextBox(document);
            InputMethod.SetIsInputMethodEnabled(editor, false);
            var host = new Window { Content = editor, Width = 320, Height = 240, Opacity = 0,
                ShowInTaskbar = false, ShowActivated = false };
            host.Show();
            try
            {
                TableStyle.ApplyAppearance(table, new TableAppearance());
                editor.IsUndoEnabled = false;
                editor.IsUndoEnabled = true;
                editor.CaretPosition = tail.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
                editor.BeginChange();
                editor.Selection.Text = " earlier typing";
                editor.EndChange();

                using var session = new TableStyleSession(editor, table);
                session.Preview(new TableAppearance { Thickness = 2, Pattern = TableLinePattern.Dotted });
                session.Preview(new TableAppearance { Thickness = 3, Pattern = TableLinePattern.Dashed, HeaderRow = true });
                session.Commit();
                Assert.Equal(3, TableStyle.ReadAppearance(table).Thickness);
                Assert.Equal(TableLinePattern.Dashed, TableStyle.ReadAppearance(table).Pattern);
                Assert.True(TableStyle.ReadAppearance(table).HeaderRow);

                editor.Undo();
                Assert.Equal(1, TableStyle.ReadAppearance(table).Thickness);
                Assert.Equal(TableLinePattern.Solid, TableStyle.ReadAppearance(table).Pattern);
                Assert.False(TableStyle.ReadAppearance(table).HeaderRow);
                Assert.Contains("earlier typing", new TextRange(document.ContentStart, document.ContentEnd).Text);
                editor.Undo();
                Assert.DoesNotContain("earlier typing", new TextRange(document.ContentStart, document.ContentEnd).Text);
            }
            finally { host.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });

        [Fact]
        public void CancelRestoresPreviewAndPreservesEarlierTypingUndo() => Sta.Run(() =>
        {
            var table = new Table();
            table.Columns.Add(new TableColumn());
            table.RowGroups.Add(new TableRowGroup { Rows = {
                new TableRow { Cells = { new TableCell(new Paragraph(new Run("cell"))) } } } });
            var document = new FlowDocument(table);
            document.Resources["TextBrush"] = Brushes.Black;
            document.Resources["TableHeaderBrush"] = Brushes.LightGray;
            var tail = new Paragraph(new Run("before"));
            document.Blocks.Add(tail);
            var editor = new RichTextBox(document);
            InputMethod.SetIsInputMethodEnabled(editor, false);
            var host = new Window { Content = editor, Width = 320, Height = 240, Opacity = 0,
                ShowInTaskbar = false, ShowActivated = false };
            host.Show();
            try
            {
                TableStyle.ApplyAppearance(table, new TableAppearance());
                editor.IsUndoEnabled = false;
                editor.IsUndoEnabled = true;
                editor.CaretPosition = tail.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
                editor.BeginChange();
                editor.Selection.Text = " earlier typing";
                editor.EndChange();
                Assert.True(editor.CanUndo);

                using var session = new TableStyleSession(editor, table);
                session.Preview(new TableAppearance { Thickness = 3, Pattern = TableLinePattern.Dashed, HeaderRow = true });
                var previewTable = Assert.IsType<Table>(session.PreviewDocument.Blocks.FirstBlock);
                Assert.Equal(new Thickness(3, 3, 0, 0), previewTable.BorderThickness);
                Assert.Equal(FontWeights.Bold, previewTable.RowGroups[0].Rows[0].FontWeight);
                Assert.Equal(new Thickness(1, 1, 0, 0), table.BorderThickness);
                session.Cancel();

                Assert.Equal(new Thickness(1, 1, 0, 0), table.BorderThickness);
                Assert.Equal(FontWeights.Normal, table.RowGroups[0].Rows[0].FontWeight);
                editor.Undo();
                Assert.DoesNotContain("earlier typing", new TextRange(document.ContentStart, document.ContentEnd).Text);
                Assert.Contains("cell", new TextRange(document.ContentStart, document.ContentEnd).Text);
            }
            finally { host.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
    }
}
