using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A note open from the trash (or from a read-only database) shows its content but never saves
    /// an edit, so a gesture that changes the displayed document would show a change that silently
    /// vanishes the next time the note is opened. Each gesture asks first.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class TrashedNoteGestureTests
    {
        [Fact]
        public void TheTableMenuLeavesATrashedNoteAlone() => Sta.Run(() =>
        {
            var (shell, editor) = EditorShell.Make();
            var cell = new TableCell(new Paragraph(new Run("c")));
            var table = new Table { RowGroups = { new TableRowGroup { Rows = { new TableRow { Cells = { cell } } } } } };
            table.Columns.Add(new TableColumn());
            editor.Document = new FlowDocument(table);
            EditorShell.Set(shell, "_currentInTrash", true);
            EditorShell.Set(shell, "_tblMenuCell", cell);

            EditorShell.Invoke(shell, "TableRowBelow_Click", shell, new RoutedEventArgs());

            Assert.Single(table.RowGroups[0].Rows);
        });

        [Fact]
        public void TheTableMenuStillEditsAnOpenNote() => Sta.Run(() =>
        {
            var (shell, editor) = EditorShell.Make();
            var cell = new TableCell(new Paragraph(new Run("c")));
            var table = new Table { RowGroups = { new TableRowGroup { Rows = { new TableRow { Cells = { cell } } } } } };
            table.Columns.Add(new TableColumn());
            editor.Document = new FlowDocument(table);
            EditorShell.Set(shell, "_tblMenuCell", cell);

            EditorShell.Invoke(shell, "TableRowBelow_Click", shell, new RoutedEventArgs());

            Assert.Equal(2, table.RowGroups[0].Rows.Count);
        });

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void DraggingAnImageOnlyStartsOnANoteThatCanBeEdited(bool trashed, bool armed) => Sta.Run(() =>
        {
            var (shell, editor) = EditorShell.Make();
            var image = new Image { Source = BitmapSource.Create(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[16], 8) };
            editor.Document = new FlowDocument(new Paragraph(new InlineUIContainer(image)));
            EditorShell.Set(shell, "_currentInTrash", trashed);
            var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                Source = image,
            };

            EditorShell.Invoke(shell, "Editor_FloatDragPress", editor, press);

            Assert.Equal(armed, EditorShell.Get(shell, "_armedObject") != null);
        });

        [Fact]
        public void ReEmbeddingARecordingInATrashedNoteSaysSoAndChangesNothing() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("rec");
            var (shell, editor) = EditorShell.Make();
            EditorShell.Set(shell, "_currentId", id);
            editor.Document = new FlowDocument(new Paragraph(new Run("")));
            editor.CaretPosition = editor.Document.ContentStart;
            EditorShell.Invoke(shell, "EmbedRecordingInNote", Bytes(1), 1200, null, -1L, NoteStore.DatabaseId);
            var chip = Chips(editor.Document).Distinct().Single();
            object? before = chip.Tag;
            EditorShell.Set(shell, "_currentInTrash", true);

            Assert.Throws<InvalidOperationException>(() =>
                EditorShell.Invoke(shell, "EmbedRecordingInNote", Bytes(2), 1500, RecordingRef.From(chip.Tag)!.Hash, id, NoteStore.DatabaseId));

            Assert.Equal(before, chip.Tag);
        });

        private static System.Collections.Generic.IEnumerable<Border> Chips(FlowDocument doc)
        {
            for (var p = doc.ContentStart; p != null && p.CompareTo(doc.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
                if (p.GetAdjacentElement(LogicalDirection.Forward) is InlineUIContainer { Child: Border b } && RecordingRef.From(b.Tag) != null)
                    yield return b;
        }

        private static byte[] Bytes(byte seed) => Enumerable.Range(0, 48).Select(i => (byte)(i * 3 + seed)).ToArray();
    }
}
