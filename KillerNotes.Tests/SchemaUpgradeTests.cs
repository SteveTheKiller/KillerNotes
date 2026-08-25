using System;
using System.IO;
using System.Linq;
using System.Text;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Opens a database shaped like a 1.0.0 file with today's store and proves nothing is
    /// harmed. This is the upgrade every long-time user's real notes go through the first time
    /// they run 1.3.0, so it is tested against a file BUILT to the old shape by hand rather
    /// than one the current code made for itself - the current code cannot produce the past.
    ///
    /// The old shape here is the 1.0.0 schema: notes without any of the later columns
    /// (title_color, spellcheck, syntax, sort_order, caret_pos, scroll_pos, format), no
    /// note_links, no graph_layouts, no recordings, groups without color. FTS and its triggers
    /// are included because 1.0.0 shipped with them.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class SchemaUpgradeTests : IDisposable
    {
        private readonly string _dir;

        public SchemaUpgradeTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            NoteStore.DbDirOverride = _dir;
            NoteStore.DemoDbFile = "old.db";
        }

        public void Dispose()
        {
            NoteStore.Close();
            NoteStore.DbDirOverride = null;
            NoteStore.DemoDbFile = null;
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        /// <summary>The bytes a 1.0.0 build would have left on disk. Two notes with distinct
        /// content blobs, one tag, one group, one sketch, so every old table has a row to lose.</summary>
        private void CraftOldDatabase(byte[] blobA, byte[] blobB)
        {
            string path = Path.Combine(_dir, "old.db");
            var csb = new SqliteConnectionStringBuilder { DataSource = path };
            using var db = new SqliteConnection(csb.ConnectionString);
            db.Open();
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = @"
CREATE TABLE notes(
    id       INTEGER PRIMARY KEY,
    title    TEXT NOT NULL DEFAULT '',
    notebook TEXT NOT NULL DEFAULT '',
    tags     TEXT NOT NULL DEFAULT '',
    created  TEXT NOT NULL,
    modified TEXT NOT NULL,
    content  BLOB,
    plain    TEXT NOT NULL DEFAULT ''
);
CREATE VIRTUAL TABLE notes_fts USING fts5(title, plain, tags, content='notes', content_rowid='id');
CREATE TRIGGER notes_ai AFTER INSERT ON notes BEGIN
    INSERT INTO notes_fts(rowid, title, plain, tags) VALUES (new.id, new.title, new.plain, new.tags);
END;
CREATE TRIGGER notes_ad AFTER DELETE ON notes BEGIN
    INSERT INTO notes_fts(notes_fts, rowid, title, plain, tags) VALUES ('delete', old.id, old.title, old.plain, old.tags);
END;
CREATE TRIGGER notes_au AFTER UPDATE ON notes BEGIN
    INSERT INTO notes_fts(notes_fts, rowid, title, plain, tags) VALUES ('delete', old.id, old.title, old.plain, old.tags);
    INSERT INTO notes_fts(rowid, title, plain, tags) VALUES (new.id, new.title, new.plain, new.tags);
END;
CREATE TABLE tags(name TEXT PRIMARY KEY COLLATE NOCASE, color TEXT NOT NULL);
CREATE TABLE groups(name TEXT PRIMARY KEY COLLATE NOCASE, sort_order INTEGER NOT NULL DEFAULT 0, collapsed INTEGER NOT NULL DEFAULT 0);
CREATE TABLE sketches(note_id INTEGER NOT NULL, ord INTEGER NOT NULL, payload BLOB NOT NULL, PRIMARY KEY(note_id, ord));";
                cmd.ExecuteNonQuery();
            }
            using (var ins = db.CreateCommand())
            {
                ins.CommandText = @"
INSERT INTO notes(id, title, notebook, tags, created, modified, content, plain)
VALUES (1, 'Server rack', 'Work', 'Red', '2026-01-05 09:00:00', '2026-01-06 10:00:00', $a, 'rack layout for IDF-2'),
       (2, 'Groceries',   '',     '',    '2026-02-01 08:00:00', '2026-02-01 08:05:00', $b, 'milk and coffee');
INSERT INTO tags(name, color) VALUES ('Red', '#DD504B');
INSERT INTO groups(name, sort_order, collapsed) VALUES ('Work', 0, 0);
INSERT INTO sketches(note_id, ord, payload) VALUES (1, 0, $s);";
                ins.Parameters.AddWithValue("$a", blobA);
                ins.Parameters.AddWithValue("$b", blobB);
                ins.Parameters.AddWithValue("$s", new byte[] { 9, 9, 9 });
                ins.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();
        }

        [Fact]
        public void AnOldDatabaseUpgradesWithoutLosingARow()
        {
            byte[] blobA = Encoding.UTF8.GetBytes("blob A bytes, not a real package");
            byte[] blobB = Encoding.UTF8.GetBytes("blob B bytes");
            CraftOldDatabase(blobA, blobB);

            NoteStore.Open(null);

            // Both notes survive with their content byte for byte. This is the sentence
            // "no databases harmed" as an assert.
            Assert.Equal(blobA, NoteStore.LoadContent(1));
            Assert.Equal(blobB, NoteStore.LoadContent(2));

            // The added columns land with their defaults rather than damaging the rows:
            // everything old is rich text with syntax highlighting off.
            Assert.Equal(0, NoteStore.GetFormat(1));
            Assert.Equal(0, NoteStore.GetFormat(2));
            var row = NoteStore.CaptureRow(1);
            Assert.NotNull(row);
            Assert.Equal("Server rack", row!.Title);
            Assert.Equal("2026-01-05 09:00:00", row.Created);
            Assert.Equal("2026-01-06 10:00:00", row.Modified);
            Assert.Equal("", row.TitleColor);
            Assert.False(row.SpellCheck);
            Assert.False(row.SyntaxHighlight);

            // The 1.3.0 tables exist and start empty, so the link and layout features work on an
            // upgraded database exactly as on a new one.
            Assert.Empty(NoteStore.Backlinks("Server rack"));
            Assert.Empty(NoteStore.ListGraphLayouts());
            Assert.False(NoteStore.GraphLayoutExists("anything"));
        }

        [Fact]
        public void UpgradingTwiceChangesNothing()
        {
            byte[] blob = Encoding.UTF8.GetBytes("stable bytes");
            CraftOldDatabase(blob, blob);

            NoteStore.Open(null);
            NoteStore.SetLinks(1, new[] { "Groceries" });
            NoteStore.Close();

            // The second open runs the same EnsureSchema and EnsureColumns again. Everything is
            // guarded by IF NOT EXISTS or a PRAGMA check, so nothing may throw and nothing the
            // first open (or the user since) wrote may be disturbed.
            NoteStore.Open(null);
            Assert.Equal(blob, NoteStore.LoadContent(1));
            Assert.Single(NoteStore.Backlinks("Groceries"));
        }
    }
}
