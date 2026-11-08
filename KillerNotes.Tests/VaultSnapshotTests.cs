using System;
using System.IO;
using System.Threading;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public sealed class VaultSnapshotTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("snapshot password")]
        public void ExportRetainsTheCapturedBodyWhenItsLiveNoteIsDeleted(string? password) => Sta.Run(() =>
        {
            using var store = new TempStore(password);
            long id = NoteStore.Create("Snapshot", 1);
            NoteStore.Save(id, "Snapshot", MarkdownBlob.Encode("The captured body"), "The captured body");
            var notes = NoteStore.List();
            using var source = NoteStore.OpenVaultSource();
            NoteStore.Delete(id);
            string root = Path.Combine(Path.GetDirectoryName(store.DbPath)!, "export");

            var result = VaultExporter.Export(root, notes, source, VaultExportText.From(key => key),
                null, CancellationToken.None);

            Assert.Equal(1, result.Written);
            Assert.Equal(0, result.Failed);
            Assert.Contains("The captured body", File.ReadAllText(Path.Combine(root, "Snapshot.md")));
            Assert.Empty(NoteStore.List());
        });

        [Fact]
        public void SnapshotKeepsMetadataWithItsContentAndRemovesItsTemporaryFiles() => Sta.Run(() =>
        {
            using var store = new TempStore("snapshot password");
            long id = NoteStore.Create("Original", 1);
            NoteStore.Save(id, "Original", MarkdownBlob.Encode("Original body"), "Original body");
            string folder;
            using (var source = NoteStore.OpenVaultSource())
            {
                folder = source.SnapshotFolder;
                Assert.True(NoteStore.IsEncryptedFile(Path.Combine(folder, "snapshot.db")));
                NoteStore.SetFormat(id, 0, MarkdownBlob.Encode("Changed body"), "Changed body");
                NoteStore.Save(id, "Changed", MarkdownBlob.Encode("Changed body"), "Changed body");

                var note = Assert.Single(source.Notes);
                Assert.Equal("Original", note.Title);
                Assert.Equal(1, note.Format);
                Assert.Equal("Original body", MarkdownBlob.Decode(source.LoadContent(id)));
            }
            Assert.False(Directory.Exists(folder));
        });

        [Fact]
        public void SnapshotCaptureRefusesAClosedDatabaseSession()
        {
            using var store = new TempStore();
            var capture = NoteStore.CaptureVaultSource();
            NoteStore.Close();
            NoteStore.Open();

            Assert.Throws<OperationCanceledException>(() => capture(CancellationToken.None));
        }
    }
}
