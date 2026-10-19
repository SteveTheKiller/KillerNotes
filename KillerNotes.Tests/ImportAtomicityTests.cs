using System;
using System.IO;
using KillerNotes.Models;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Importing a shared file is all or nothing. A file that fails partway through must leave
    /// the open database exactly as it was, so a retry after fixing the file does not duplicate
    /// the notes that made it in before the failure.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class ImportAtomicityTests
    {
        [Fact]
        public void A_file_that_fails_partway_imports_nothing() => Sta.Run(() =>
        {
            string knote = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".knote");
            try
            {
                using (new TempStore())
                {
                    long id = NoteStore.Create("first", Note.FormatMarkdown);
                    NoteStore.SetFormat(id, Note.FormatMarkdown, MarkdownBlob.Encode("one"), "one");
                    NoteStore.ExportNote(id, knote, password: null);
                }

                // A second note whose recording row cannot be read: the first note copies cleanly,
                // the second throws while its attachments are copied.
                var csb = new SqliteConnectionStringBuilder { DataSource = knote };
                using (var raw = new SqliteConnection(csb.ConnectionString))
                {
                    raw.Open();
                    using var cmd = raw.CreateCommand();
                    cmd.CommandText =
                        "INSERT INTO notes(title, notebook, tags, created, modified, content, plain) " +
                        "SELECT 'second', notebook, tags, created, modified, content, plain FROM notes; " +
                        "DROP TABLE IF EXISTS recordings; " +
                        "CREATE TABLE recordings(note_id INTEGER, ord INTEGER, payload BLOB, duration INTEGER); " +
                        "INSERT INTO recordings(note_id, ord, payload, duration) " +
                        "SELECT MAX(id), 0, x'00', NULL FROM notes;";
                    cmd.ExecuteNonQuery();
                }
                SqliteConnection.ClearAllPools();

                using (new TempStore())
                {
                    Assert.ThrowsAny<Exception>(() => NoteStore.ImportNotes(knote, password: null));
                    Assert.Empty(NoteStore.List());
                }
            }
            finally { try { File.Delete(knote); } catch { /* best effort */ } }
        });
    }
}
