using System;
using System.IO;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The remembered caret and scroll position (1.1.1) lived in caret_pos and scroll_pos, columns
    /// added to the notes table after the content blob. SQLite rewrites a whole row to change one
    /// of its columns and walks every overflow page of the blob to reach a column stored after
    /// it, so a note with pictures paid for its megabytes each time it was left (about a second
    /// on an encrypted notepad) and again each time it was opened. Positions now live in their own
    /// small table, and nothing about remembering them touches the note's row.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class NotePositionTests : IDisposable
    {
        private readonly string _dir;
        private string DbPath => Path.Combine(_dir, "pos.db");

        public NotePositionTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            NoteStore.Close();
            NoteStore.DbDirOverride = _dir;
            NoteStore.DemoDbFile = "pos.db";
            NoteStore.Open();
        }

        public void Dispose()
        {
            NoteStore.Close();
            NoteStore.DbDirOverride = null;
            NoteStore.DemoDbFile = null;
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private object? Scalar(string sql)
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ConnectionString);
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteScalar();
        }

        private void Exec(string sql)
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ConnectionString);
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        [Fact]
        public void RememberingAPositionLeavesTheNoteRowAlone()
        {
            long id = NoteStore.Create("Screenshots");
            string modifiedBefore = (string)Scalar($"SELECT modified FROM notes WHERE id = {id}")!;

            NoteStore.SetNotePosition(id, 42, 120.5);

            Assert.Equal((42, 120.5), NoteStore.GetNotePosition(id));
            Assert.Equal(0L, Scalar($"SELECT caret_pos FROM notes WHERE id = {id}"));
            Assert.Equal(42L, Scalar($"SELECT caret FROM note_positions WHERE note_id = {id}"));
            Assert.Equal(modifiedBefore, Scalar($"SELECT modified FROM notes WHERE id = {id}"));
        }

        [Fact]
        public void APositionSavedByAnOlderVersionCarriesOver()
        {
            long id = NoteStore.Create("Old note");
            NoteStore.Close();
            Exec($"UPDATE notes SET caret_pos = 7, scroll_pos = 33 WHERE id = {id}");
            Exec("DROP TABLE IF EXISTS note_positions");

            NoteStore.Open();

            Assert.Equal((7, 33.0), NoteStore.GetNotePosition(id));
        }

        // A notepad another PC holds opens read-only, and a read-only open never builds the
        // schema, so a file no 1.7.0 copy has written has no note_positions table at all.
        [Fact]
        public void ANotepadWithoutThePositionTableStillReadsItsPositions()
        {
            long id = NoteStore.Create("Shared notepad");
            Exec($"UPDATE notes SET caret_pos = 9, scroll_pos = 18 WHERE id = {id}");
            Exec("DROP TABLE note_positions");

            Assert.Equal((9, 18.0), NoteStore.GetNotePosition(id));
        }

        [Fact]
        public void UndoingADeleteBringsThePositionBack()
        {
            long id = NoteStore.Create("Undo me");
            NoteStore.SetNotePosition(id, 12, 300);
            var row = NoteStore.CaptureRow(id)!;
            NoteStore.Delete(id);

            NoteStore.RestoreRow(row);

            Assert.Equal((12, 300.0), NoteStore.GetNotePosition(id));
        }

        [Fact]
        public void UndoingADeleteKeepsAMarkdownNoteMarkdown()
        {
            long id = NoteStore.Create("Markdown", format: 1);
            var row = NoteStore.CaptureRow(id)!;
            NoteStore.Delete(id);

            NoteStore.RestoreRow(row);

            Assert.Equal(1, NoteStore.GetFormat(id));
        }

        [Fact]
        public void DeletingForeverForgetsThePosition()
        {
            long id = NoteStore.Create("Gone");
            NoteStore.SetNotePosition(id, 5, 10);

            NoteStore.DeleteForever(id);

            Assert.Equal(0L, Scalar($"SELECT count(*) FROM note_positions WHERE note_id = {id}"));
        }
    }
}
