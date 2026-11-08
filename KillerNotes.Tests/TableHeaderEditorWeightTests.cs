using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The note editor gives every Paragraph an implicit FontWeight="Normal" style, and a style
    /// setter outranks a value inherited from the table row or cell. These tests load that style
    /// from MainWindow.xaml so header emphasis is checked against the editor the user types in.
    /// </summary>
    public sealed class TableHeaderEditorWeightTests
    {
        private static string Root()
        {
            for (var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "KillerNotes.csproj"))) return directory.FullName;
            throw new InvalidOperationException("Cannot locate the application sources");
        }

        private static Style EditorParagraphStyle()
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var document = XDocument.Load(Path.Combine(Root(), "Shell", "MainWindow.xaml"));
            XElement editor = document.Descendants().Single(e => e.Name.LocalName == "RichTextBox" && (string?)e.Attribute(x + "Name") == "Editor");
            XElement style = editor.Elements().Single(e => e.Name.LocalName == "RichTextBox.Resources").Elements()
                .Single(e => e.Name.LocalName == "Style" && (string?)e.Attribute("TargetType") == "Paragraph");
            return (Style)XamlReader.Parse(style.ToString());
        }

        private static RichTextBox Editor(FlowDocument document)
        {
            var editor = new RichTextBox(document);
            editor.Resources.Add(typeof(Paragraph), EditorParagraphStyle());
            document.Resources["TextBrush"] = Brushes.Black;
            document.Resources["TableHeaderBrush"] = Brushes.LightGray;
            return editor;
        }

        private static (Table Table, Run[,] Runs) Grid()
        {
            var runs = new Run[2, 2];
            var table = new Table();
            table.Columns.Add(new TableColumn());
            table.Columns.Add(new TableColumn());
            var group = new TableRowGroup();
            for (int r = 0; r < 2; r++)
            {
                var row = new TableRow();
                for (int c = 0; c < 2; c++)
                {
                    runs[r, c] = new Run($"cell {r}{c}");
                    row.Cells.Add(new TableCell(new Paragraph(runs[r, c])));
                }
                group.Rows.Add(row);
            }
            table.RowGroups.Add(group);
            return (table, runs);
        }

        private static FontWeight Weight(Run run) => (FontWeight)run.GetValue(TextElement.FontWeightProperty);

        private static Run[,] Reopen(FlowDocument source)
        {
            using var package = new MemoryStream();
            new TextRange(source.ContentStart, source.ContentEnd).Save(package, DataFormats.XamlPackage);
            package.Position = 0;
            var reopened = new FlowDocument();
            Editor(reopened);
            new TextRange(reopened.ContentStart, reopened.ContentEnd).Load(package, DataFormats.XamlPackage);
            Table table = reopened.Blocks.OfType<Table>().Single();
            TableStyle.RestoreGridBrushes(table);
            var runs = new Run[2, 2];
            var rows = table.RowGroups.SelectMany(g => g.Rows).ToArray();
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 2; c++)
                    runs[r, c] = rows[r].Cells[c].Blocks.OfType<Paragraph>().Single().Inlines.OfType<Run>().Single();
            return runs;
        }

        [Fact]
        public void AcceptedHeadersAreBoldInTheNoteEditorAndAfterReopening() => Sta.Run(() =>
        {
            var (table, runs) = Grid();
            var document = new FlowDocument(table);
            var editor = Editor(document);
            Assert.Equal(FontWeights.Normal, Weight(runs[0, 0]));

            using (var session = new TableStyleSession(editor, table))
            {
                session.Preview(new TableAppearance { HeaderRow = true, HeaderColumn = true });
                Assert.Equal(TableStyleCommitResult.Applied, session.Commit());
            }

            Assert.Equal(FontWeights.Bold, Weight(runs[0, 0]));
            Assert.Equal(FontWeights.Bold, Weight(runs[0, 1]));
            Assert.Equal(FontWeights.Bold, Weight(runs[1, 0]));
            Assert.Equal(FontWeights.Normal, Weight(runs[1, 1]));

            Run[,] reopened = Reopen(document);
            Assert.Equal(FontWeights.Bold, Weight(reopened[0, 0]));
            Assert.Equal(FontWeights.Bold, Weight(reopened[0, 1]));
            Assert.Equal(FontWeights.Bold, Weight(reopened[1, 0]));
            Assert.Equal(FontWeights.Normal, Weight(reopened[1, 1]));
        });

        [Fact]
        public void RemovingHeadersReturnsTheirTextToBodyWeightInTheNoteEditor() => Sta.Run(() =>
        {
            var (table, runs) = Grid();
            var document = new FlowDocument(table);
            var editor = Editor(document);
            TableStyle.ApplyAppearance(table, new TableAppearance { HeaderRow = true, HeaderColumn = true });

            using (var session = new TableStyleSession(editor, table))
            {
                session.Preview(new TableAppearance());
                Assert.Equal(TableStyleCommitResult.Applied, session.Commit());
            }

            foreach (Run run in runs) Assert.Equal(FontWeights.Normal, Weight(run));
            foreach (Run run in Reopen(document)) Assert.Equal(FontWeights.Normal, Weight(run));
        });
    }
}
