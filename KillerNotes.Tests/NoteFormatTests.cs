using System.IO;
using System.Linq;
using System.Text;
using KillerNotes.Models;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The notes.format column (1.3.0): 0 = rich text, 1 = markdown.
    ///
    /// The case worth guarding is SHARING. format is declared in SchemaSql rather than only in
    /// EnsureColumns specifically because ExportNote runs SchemaSql on its own - miss that and
    /// every .knote silently drops the column, so a markdown note mails out and comes back as
    /// rich text with its source rendered as body copy. Nothing in the UI would show the loss.
    ///
    /// No STA here: none of this touches the WPF text stack. TempStore comes from
    /// NoteStoreTests.cs and points the static store at a throwaway folder.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public class NoteFormatTests
    {
        private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

        [Fact]
        public void NewNotesAreRichTextUnlessAskedOtherwise()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("plain");
            Assert.Equal(Note.FormatRich, NoteStore.GetFormat(id));
        }

        [Fact]
        public void CreateCanMakeAMarkdownNote()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("md", Note.FormatMarkdown);
            Assert.Equal(Note.FormatMarkdown, NoteStore.GetFormat(id));
        }

        [Fact]
        public void ListCarriesTheFormatOntoTheRow()
        {
            using var store = new TempStore();
            long rich = NoteStore.Create("rich");
            long md = NoteStore.Create("md", Note.FormatMarkdown);

            var rows = NoteStore.List();
            Assert.False(rows.Single(n => n.Id == rich).IsMarkdown);
            Assert.True(rows.Single(n => n.Id == md).IsMarkdown);
        }

        [Fact]
        public void SetFormatMovesTheFormatAndTheBodyTogether()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("note");
            NoteStore.Save(id, "note", Utf8("<rich blob>"), "rich text");

            NoteStore.SetFormat(id, Note.FormatMarkdown, Utf8("# now markdown"), "# now markdown");

            Assert.Equal(Note.FormatMarkdown, NoteStore.GetFormat(id));
            Assert.Equal("# now markdown", new UTF8Encoding(false).GetString(NoteStore.LoadContent(id)!));
        }

        [Fact]
        public void GetFormatIsRichForANoteThatIsNotThere()
        {
            using var store = new TempStore();
            // Rich is the safe default: a caller about to decode content must not be told
            // "markdown" for a row it cannot read.
            Assert.Equal(Note.FormatRich, NoteStore.GetFormat(999999));
        }

        [Fact]
        public void ExportedKnoteKeepsAMarkdownNoteMarkdownOnTheWayBackIn()
        {
            string knote = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".knote");
            try
            {
                long id;
                using (var store = new TempStore())
                {
                    id = NoteStore.Create("shared", Note.FormatMarkdown);
                    NoteStore.Save(id, "shared", Utf8("# hello"), "# hello");
                    NoteStore.ExportNote(id, knote, password: null);
                }

                using (var receiver = new TempStore())
                {
                    Assert.Equal(1, NoteStore.ImportNotes(knote, password: null));
                    var imported = Assert.Single(NoteStore.List());
                    Assert.True(imported.IsMarkdown, "a markdown note must not arrive as rich text");
                    Assert.Equal("# hello", new UTF8Encoding(false).GetString(NoteStore.LoadContent(imported.Id)!));
                }
            }
            finally { try { File.Delete(knote); } catch { /* best effort */ } }
        }

        [Fact]
        public void ExportedKnoteKeepsARichNoteRich()
        {
            string knote = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".knote");
            try
            {
                using (var store = new TempStore())
                {
                    long id = NoteStore.Create("rich one");
                    NoteStore.Save(id, "rich one", Utf8("<blob>"), "body");
                    NoteStore.ExportNote(id, knote, password: null);
                }
                using (var receiver = new TempStore())
                {
                    NoteStore.ImportNotes(knote, password: null);
                    Assert.False(Assert.Single(NoteStore.List()).IsMarkdown);
                }
            }
            finally { try { File.Delete(knote); } catch { /* best effort */ } }
        }

        /// <summary>
        /// Back-compat: a .knote written before 1.3.0 has no format column at all. ImportNotes
        /// probes for it and substitutes 0, because every note in such a file is rich text.
        /// Simulated by exporting and then dropping the column, which is exactly the shape of an
        /// older file.
        /// </summary>
        [Fact]
        public void KnoteFromBeforeTheFormatColumnStillImports()
        {
            string knote = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".knote");
            try
            {
                using (var store = new TempStore())
                {
                    long id = NoteStore.Create("legacy");
                    NoteStore.Save(id, "legacy", Utf8("<blob>"), "old body");
                    NoteStore.ExportNote(id, knote, password: null);
                }

                var csb = new SqliteConnectionStringBuilder { DataSource = knote };
                using (var raw = new SqliteConnection(csb.ConnectionString))
                {
                    raw.Open();
                    using var cmd = raw.CreateCommand();
                    // The FTS triggers reference the notes table, so drop them before altering it.
                    cmd.CommandText =
                        "DROP TRIGGER IF EXISTS notes_ai; DROP TRIGGER IF EXISTS notes_ad; " +
                        "DROP TRIGGER IF EXISTS notes_au; ALTER TABLE notes DROP COLUMN format;";
                    cmd.ExecuteNonQuery();
                }
                SqliteConnection.ClearAllPools();

                using (var receiver = new TempStore())
                {
                    Assert.Equal(1, NoteStore.ImportNotes(knote, password: null));
                    var imported = Assert.Single(NoteStore.List());
                    Assert.False(imported.IsMarkdown);
                    Assert.Equal("legacy", imported.Title);
                }
            }
            finally { try { File.Delete(knote); } catch { /* best effort */ } }
        }

        [Fact]
        public void FormatSurvivesClosingAndReopeningTheDatabase()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("md", Note.FormatMarkdown);
            string path = NoteStore.DbPath;

            NoteStore.Close();
            NoteStore.Open(null);

            Assert.Equal(Note.FormatMarkdown, NoteStore.GetFormat(id));
            Assert.Equal(path, NoteStore.DbPath);
        }
    }
}
