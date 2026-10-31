using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Services;
using KillerNotes.Shell;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    // A document either becomes one complete note or nothing: a failed import leaves no empty row
    // behind, and a retry adds exactly one.
    [Collection(NoteStoreCollection.Name)]
    public sealed class DocumentImportTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "kn-doc-import-" + Guid.NewGuid().ToString("N"));

        public DocumentImportTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void ADocumentThatWillNotSerializeAddsNoRow() => Sta.Run(() =>
        {
            using var store = new TempStore();
            // A picture that was never loaded: the package writer cannot encode it.
            var doc = new FlowDocument(new Paragraph(new InlineUIContainer(new Image { Source = new BitmapImage() })));

            Assert.ThrowsAny<Exception>(() => MainWindow.CreateNoteFromDocument("broken", doc));

            Assert.Empty(NoteStore.List());
        });

        [Fact]
        public void AFailedInsertAddsNoRowAndARetryAddsOneCompleteNote() => Sta.Run(() =>
        {
            using var store = new TempStore();
            string rtf = Path.Combine(_dir, "letter.rtf");
            File.WriteAllText(rtf, @"{\rtf1\ansi Dear reader}");
            Raw(store, "CREATE TRIGGER no_saves BEFORE UPDATE ON notes BEGIN SELECT RAISE(ABORT, 'disk full'); END; " +
                       "CREATE TRIGGER no_notes BEFORE INSERT ON notes WHEN new.title = 'never' BEGIN SELECT RAISE(ABORT, 'disk full'); END;");

            Assert.ThrowsAny<SqliteException>(() => MainWindow.CreateNoteFromDocument("never",
                new FlowDocument(new Paragraph(new Run("words")))));
            Assert.Empty(NoteStore.List());

            long id = MainWindow.ImportRtf(rtf);

            var note = Assert.Single(NoteStore.List());
            Assert.Equal(id, note.Id);
            Assert.Equal("letter", note.Title);
            Assert.Contains("Dear reader", NoteStore.LoadPlain(id));
            Assert.NotNull(NoteStore.LoadContent(id));
            Assert.Single(NoteStore.List("reader"));
        });

        [Fact]
        public void AnImportedImageIsACompleteNote() => Sta.Run(() =>
        {
            using var store = new TempStore();
            string png = Path.Combine(_dir, "shot.png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4)));
            using (var file = File.Create(png)) enc.Save(file);

            long id = MainWindow.ImportImage(png);

            Assert.Equal("shot", Assert.Single(NoteStore.List()).Title);
            Assert.NotNull(NoteStore.LoadContent(id));
        });

        [Fact]
        public void NewNotesLandAtTheBottomOfACustomOrder() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long first = NoteStore.Create("first");

            long imported = MainWindow.CreateNoteFromDocument("second", new FlowDocument(new Paragraph(new Run("words"))));

            var notes = NoteStore.List();
            Assert.True(notes.Single(n => n.Id == imported).SortOrder > notes.Single(n => n.Id == first).SortOrder);
        });

        private static void Raw(TempStore store, string sql)
        {
            var csb = new SqliteConnectionStringBuilder { DataSource = store.DbPath, Pooling = false };
            using var raw = new SqliteConnection(csb.ConnectionString);
            raw.Open();
            using var cmd = raw.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
