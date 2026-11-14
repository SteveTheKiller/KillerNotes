using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public sealed class ReleaseStoreSafetyTests
    {
        [Fact]
        public void SavingAPermanentlyDeletedNoteReportsFailure()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("copy");
            NoteStore.DeleteForever(id);
            Assert.Throws<InvalidOperationException>(() => NoteStore.Save(id, "edited", new byte[] { 1 }, "edited"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UndoCreatedCopyPreservesEditsInTrash(bool edited)
        {
            using var store = new TempStore();
            long id = NoteStore.Create("copy");
            var original = NoteStore.CaptureRow(id)!;
            if (edited) NoteStore.Save(id, "edited", new byte[] { 1 }, "edited");
            NoteStore.RemoveCreatedCopy(id, original);
            if (edited) Assert.Equal("edited", Assert.Single(NoteStore.ListTrash()).Title);
            else Assert.Null(NoteStore.CaptureRow(id));
        }

        [Fact]
        public void UndoCopyPreservesHistoryEvenWhenBodyWasRestored()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("copy", new byte[] { 1 }, "original");
            var original = NoteStore.CaptureRow(id)!;
            NoteStore.Save(id, "edited", new byte[] { 2 }, "edited");
            NoteStore.Save(id, "copy", new byte[] { 1 }, "original");
            NoteStore.RemoveCreatedCopy(id, original);
            Assert.Equal("copy", Assert.Single(NoteStore.ListTrash()).Title);
            Assert.NotEmpty(NoteStore.ListHistory(id));
        }

        [Fact]
        public void UndoCopyPreservesAChangedLegacySketchPayload()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("copy");
            NoteStore.SaveSketches(id, new Dictionary<int, byte[]> { [0] = new byte[] { 1 } });
            var original = NoteStore.CaptureRow(id)!;
            NoteStore.SaveSketches(id, new Dictionary<int, byte[]> { [0] = new byte[] { 2 } });
            NoteStore.RemoveCreatedCopy(id, original);
            Assert.Equal(id, Assert.Single(NoteStore.ListTrash()).Id);
            Assert.Equal(new byte[] { 2 }, NoteStore.LoadSketches(id)[0]);
        }

        [Fact]
        public void OptionalTableReorderFailureDoesNotPreventOpeningNotes()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("retained");
            NoteStore.Close();
            EditDatabase(store.DbPath, "ALTER TABLE notes ADD COLUMN later TEXT; CREATE VIEW notes_reorder AS SELECT id FROM notes;");
            NoteStore.Open();
            Assert.Equal("retained", NoteStore.GetNote(id)!.Title);
        }

        private static void EditDatabase(string path, string sql)
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        [Fact]
        public void InterruptedPositionMigrationRetriesOnNextOpen()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("reader");
            NoteStore.Close();
            EditDatabase(store.DbPath, "DROP TABLE note_positions; UPDATE notes SET caret_pos = 17, scroll_pos = 42; " +
                "CREATE VIEW note_positions AS SELECT 1 AS note_id, 0 AS caret, 0 AS scroll;");
            NoteStore.Open();
            Assert.Equal("reader", NoteStore.GetNote(id)!.Title);
            NoteStore.SetNotePosition(id, 18, 43);
            Assert.Equal((18, 43d), NoteStore.GetNotePosition(id));
            NoteStore.Close();
            EditDatabase(store.DbPath, "DROP VIEW note_positions;");
            NoteStore.Open();
            Assert.Equal((18, 43d), NoteStore.GetNotePosition(id));
        }

        [Fact]
        public void FailedPositionCopyRollsBackItsTableAndRetriesAllRows()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("reader");
            NoteStore.Create("invalid legacy position");
            NoteStore.Close();
            EditDatabase(store.DbPath, "DROP TABLE note_positions; " +
                "ALTER TABLE notes RENAME COLUMN caret_pos TO saved_caret; " +
                "ALTER TABLE notes ADD COLUMN caret_pos INTEGER; " +
                "UPDATE notes SET caret_pos = 17, scroll_pos = 42 WHERE id = " + id + "; " +
                "UPDATE notes SET scroll_pos = 1 WHERE id <> " + id + ";");
            NoteStore.Open();
            Assert.Equal((17, 42d), NoteStore.GetNotePosition(id));
            NoteStore.Close();
            EditDatabase(store.DbPath, "UPDATE notes SET caret_pos = 0 WHERE caret_pos IS NULL;");
            NoteStore.Open();
            Assert.Equal((17, 42d), NoteStore.GetNotePosition(id));
        }

        [Fact]
        public void RejectedOrphanGroupRepairDoesNotPreventOpeningNotes()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("orphan");
            NoteStore.Close();
            EditDatabase(store.DbPath, "UPDATE notes SET notebook = 'Parent/Child'; " +
                "CREATE TRIGGER reject_groups BEFORE INSERT ON groups BEGIN SELECT RAISE(ABORT, 'injected repair failure'); END;");
            NoteStore.Open();
            Assert.Equal("orphan", NoteStore.GetNote(id)!.Title);
        }

        [Fact]
        public void ReusedLegacyNoteIdDoesNotInheritAnOrphanReadingPosition()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("removed by older version");
            NoteStore.SetNotePosition(id, 70, 100);
            NoteStore.Close();
            EditDatabase(store.DbPath, "DELETE FROM notes;");
            NoteStore.Open();
            long replacement = NoteStore.Create("new note");
            Assert.Equal(id, replacement);
            Assert.Equal((0, 0d), NoteStore.GetNotePosition(replacement));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CopiedRichNoteCanBeFoundByStoredSketchLabels(bool expand) => Sta.Run(() =>
        {
            using var store = new TempStore();
            var doc = new FlowDocument(new Paragraph(new Run("Body {date}")));
            using var body = new MemoryStream();
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            range.Save(body, DataFormats.XamlPackage);
            long source = NoteStore.Create("source", body.ToArray(), range.Text + "\nUniqueSketchLabel");
            long copy = NoteStore.CreateFromTemplate(source, expand: expand);
            Assert.Contains(NoteStore.List("UniqueSketchLabel"), n => n.Id == copy);
        });
    }
}
