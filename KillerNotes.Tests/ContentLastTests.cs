using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The content blob has to be the last column of notes. SQLite stores a row's values in
    /// column order and reaches a value after a large blob only by walking the blob's overflow
    /// pages, so every column added after content (color, pin, format, sort order, trash flag,
    /// preview text) cost a walk through every picture on every sidebar refresh: 1.2 s on an
    /// encrypted notepad with 40 four-megabyte notes. Opening a notepad rebuilds the table with
    /// content last, once, keeping every row, id, search entry and trigger.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class ContentLastTests : IDisposable
    {
        private readonly string _dir;
        private string DbPath => Path.Combine(_dir, "last.db");

        public ContentLastTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            NoteStore.Close();
            NoteStore.DbDirOverride = _dir;
            NoteStore.DemoDbFile = "last.db";
        }

        public void Dispose()
        {
            NoteStore.Close();
            NoteStore.DbDirOverride = null;
            NoteStore.DemoDbFile = null;
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private List<string> Columns()
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ConnectionString);
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA table_info(notes)";
            using var r = cmd.ExecuteReader();
            var cols = new List<string>();
            while (r.Read()) cols.Add(r.GetString(1));
            return cols;
        }

        [Fact]
        public void ANewNotepadKeepsContentLast()
        {
            NoteStore.Open();
            NoteStore.Close();

            Assert.Equal("content", Columns().Last());
        }

        [Fact]
        public void AnExistingNotepadIsRebuiltWithEverythingKept()
        {
            NoteStore.Open();
            long a = NoteStore.Create("Screenshots", new byte[] { 1, 2, 3 }, "giraffe photo");
            long b = NoteStore.Create("Second", new byte[] { 4 }, "plain words");
            NoteStore.SetTitleColor(a, "#FF0000");
            NoteStore.SetPinned(a, true);
            NoteStore.SetNoteTags(b, "client");
            NoteStore.Close();
            // An older layout: content back in the middle of the row, the way every notepad from
            // before 1.7.0 has it. SQLite cannot move a column, so put one after it instead.
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ConnectionString))
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = "ALTER TABLE notes ADD COLUMN zz_later TEXT NOT NULL DEFAULT ''";
                cmd.ExecuteNonQuery();
            }
            Assert.NotEqual("content", Columns().Last());

            NoteStore.Open();

            Assert.Equal(new byte[] { 1, 2, 3 }, NoteStore.LoadContent(a));
            var notes = NoteStore.List();
            Assert.Equal(2, notes.Count);
            var first = notes.Single(n => n.Id == a);
            Assert.Equal("#FF0000", first.TitleColor);
            Assert.True(first.Pinned);
            Assert.Equal("client", notes.Single(n => n.Id == b).Tags);
            Assert.Contains(NoteStore.List("giraffe"), n => n.Id == a);

            // Triggers came back: a new note and an edit still reach search.
            long c = NoteStore.Create("Third", new byte[] { 5 }, "zebra stripes");
            Assert.Contains(NoteStore.List("zebra"), n => n.Id == c);
            NoteStore.Close();
            Assert.Equal("content", Columns().Last());
            Assert.Contains("zz_later", Columns());
            // The per-connection TEMP triggers came back as TEMP: none landed in the file.
            using var check = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath, Pooling = false }.ConnectionString);
            check.Open();
            using var q = check.CreateCommand();
            q.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'trigger' AND substr(name, 1, 3) = 'kn_'";
            Assert.Equal(0L, q.ExecuteScalar());
        }
    }
}
