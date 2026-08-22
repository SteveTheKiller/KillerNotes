using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Models;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// BACKWARD COMPATIBILITY. Do not weaken these to make a change pass.
    ///
    /// Tear-off .knote sheets and whole .kndb databases get handed between techs who are not on
    /// the same version, and the receiving app is whatever they already had installed. Every
    /// KillerNotes before 1.3.0 reads a note's content blob as a XamlPackage unconditionally,
    /// because it predates the notes.format column, and TextRange.Load throws ArgumentException on
    /// anything else. That throw happens on the startup path, before the window exists, so a
    /// single bad blob crashed the receiving app on every launch with no way back in short of
    /// editing the registry or deleting the database. It happened for real on 2026-08-22.
    ///
    /// So the contract is: every content blob this app writes must be loadable by a build that
    /// knows nothing about formats. OldLoader below IS that build, in three lines, and every test
    /// here runs the bytes through it. If someone changes markdown storage back to raw source,
    /// these fail, and they are supposed to.
    ///
    /// In the NoteStore collection because they drive the static store, and on STA because the
    /// XamlPackage reader and writer both require it.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class KnoteCompatibilityTests
    {
        private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

        /// <summary>Exactly what a pre-1.3.0 build does with a note's content: hand the bytes to
        /// the XamlPackage deserializer, with no idea the format column exists. Throws the same
        /// ArgumentException the old build would have died on.</summary>
        private static string OldLoader(byte[] blob)
        {
            var doc = new FlowDocument();
            using var ms = new MemoryStream(blob);
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            range.Load(ms, DataFormats.XamlPackage);
            return range.Text;
        }

        /// <summary>Writes a markdown note the way the app writes one, through MarkdownBlob, so
        /// these tests exercise the real storage path rather than a hand-made blob.</summary>
        private static long CreateMarkdownNote(string source)
        {
            long id = NoteStore.Create("shared", Note.FormatMarkdown);
            NoteStore.SetFormat(id, Note.FormatMarkdown, MarkdownBlob.Encode(source), source);
            return id;
        }

        private const string Source = "# runbook\n\n- reboot the switch\n- wait 90s\n\n\tvlan 20";

        [Fact]
        public void AMarkdownNoteSurvivesAKnoteRoundTripIntoAnOldBuild() => Sta.Run(() =>
        {
            string knote = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".knote");
            try
            {
                using (new TempStore())
                {
                    long id = CreateMarkdownNote(Source);
                    NoteStore.ExportNote(id, knote, password: null);
                }

                using (new TempStore())
                {
                    Assert.Equal(1, NoteStore.ImportNotes(knote, password: null));
                    var imported = Assert.Single(NoteStore.List());
                    byte[] blob = NoteStore.LoadContent(imported.Id)!;

                    // The receiving build is old: it does not look at format, it just loads.
                    string text = OldLoader(blob);
                    Assert.Contains("# runbook", text);
                    Assert.Contains("reboot the switch", text);
                }
            }
            finally { try { File.Delete(knote); } catch { /* best effort */ } }
        });

        /// <summary>
        /// The full old-build simulation: strip the format column from the .knote, the way a file
        /// written before 1.3.0 would be, import it, and load it blind. This is the shape of the
        /// crash that started all of this, and it must now come back as readable text.
        /// </summary>
        [Fact]
        public void AKnoteStrippedOfItsFormatColumnStillOpensBlind() => Sta.Run(() =>
        {
            string knote = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".knote");
            try
            {
                using (new TempStore())
                {
                    long id = CreateMarkdownNote(Source);
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

                using (new TempStore())
                {
                    Assert.Equal(1, NoteStore.ImportNotes(knote, password: null));
                    var imported = Assert.Single(NoteStore.List());
                    Assert.False(imported.IsMarkdown);   // no format column, so it arrives rich
                    Assert.Contains("# runbook", OldLoader(NoteStore.LoadContent(imported.Id)!));
                }
            }
            finally { try { File.Delete(knote); } catch { /* best effort */ } }
        });

        /// <summary>The strongest form of the contract: after a database has been opened by this
        /// build, NOTHING in it can crash the old loader. Covers rich notes, migrated markdown and
        /// freshly written markdown in one sweep.</summary>
        [Fact]
        public void NoBlobInAMigratedDatabaseCanCrashTheOldLoader() => Sta.Run(() =>
        {
            using var store = new TempStore();

            long rich = NoteStore.Create("rich");
            NoteStore.Save(rich, "rich", RichBlob("plain words"), "plain words");
            CreateMarkdownNote(Source);

            // A raw markdown note left behind by an earlier 1.3.0 build, the exact hazard.
            long legacy = NoteStore.Create("legacy md", Note.FormatMarkdown);
            NoteStore.SetFormat(legacy, Note.FormatMarkdown, Utf8("# raw\n\ttab"), "# raw\n\ttab");

            MarkdownMigration.RewrapRawMarkdown();

            var ids = NoteStore.List().Select(n => n.Id).ToList();
            Assert.Equal(3, ids.Count);
            foreach (long id in ids)
            {
                byte[]? blob = NoteStore.LoadContent(id);
                if (blob == null) continue;
                OldLoader(blob);   // the assertion is that this does not throw
            }
        });

        /// <summary>Guards the invariant at its source: whatever the app stores for a markdown
        /// note is a package. A regression here is what would make every test above meaningless.</summary>
        [Fact]
        public void TheAppNeverStoresARawMarkdownBlob() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = CreateMarkdownNote(Source);
            Assert.True(MarkdownBlob.IsPackage(NoteStore.LoadContent(id)));
        });

        /// <summary>An encrypted share is the same contract with a password on the file.</summary>
        [Fact]
        public void APasswordProtectedKnoteKeepsTheSameGuarantee() => Sta.Run(() =>
        {
            string knote = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".knote");
            try
            {
                using (new TempStore())
                {
                    long id = CreateMarkdownNote(Source);
                    NoteStore.ExportNote(id, knote, password: "share me");
                }
                using (new TempStore())
                {
                    Assert.Equal(1, NoteStore.ImportNotes(knote, password: "share me"));
                    var imported = Assert.Single(NoteStore.List());
                    Assert.Contains("# runbook", OldLoader(NoteStore.LoadContent(imported.Id)!));
                }
            }
            finally { try { File.Delete(knote); } catch { /* best effort */ } }
        });

        private static byte[] RichBlob(string text)
        {
            var doc = new FlowDocument(new Paragraph(new Run(text)));
            using var ms = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.XamlPackage);
            return ms.ToArray();
        }
    }
}
