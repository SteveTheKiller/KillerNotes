using System;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;
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
    
        // Every live save restarts an online backup from the first page. A vault that takes
        // longer to copy than the gap between saves must still finish instead of timing out.
        [Fact]
        public void ACopyThatKeepsRestartingStillFinishes()
        {
            using var store = new TempStore();
            string folder = Path.Combine(Path.GetTempPath(), "kn-backup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string from = Path.Combine(folder, "from.db"), to = Path.Combine(folder, "to.db");
                using (var source = new SqliteConnection("Data Source=" + from + ";Pooling=False"))
                {
                    source.Open();
                    Exec(source, "CREATE TABLE t(v BLOB); WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<1500) INSERT INTO t SELECT randomblob(3000) FROM n;");
                }
                using var writer = new SqliteConnection("Data Source=" + from + ";Pooling=False");
                writer.Open();
                using var reader = new SqliteConnection("Data Source=" + from + ";Mode=ReadOnly;Pooling=False");
                reader.Open();
                using var destination = new SqliteConnection("Data Source=" + to + ";Pooling=False");
                destination.Open();
                int saves = 0;
                using (var backup = SQLitePCL.raw.sqlite3_backup_init(destination.Handle!, "main", reader.Handle!, "main"))
                    VaultSnapshot.Copy(backup, () => { }, CancellationToken.None, TimeSpan.FromSeconds(20),
                        afterStep: () => { saves++; Exec(writer, "INSERT INTO t VALUES (randomblob(10))"); });

                Assert.True(saves > 0);
                using var count = destination.CreateCommand();
                count.CommandText = "SELECT count(*) FROM t";
                Assert.True((long)count.ExecuteScalar()! >= 1500);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
            }
        }

        // A snapshot folder that cannot be removed right away (a scanner holding the file) never
        // turns a finished export into a failure, and the next start clears what is left.
        [Fact]
        public void ASnapshotFolderThatIsStillHeldIsLeftForTheNextStart()
        {
            using var store = new TempStore();
            string root = Path.Combine(Path.GetTempPath(), "kn-sweep-" + Guid.NewGuid().ToString("N"));
            try
            {
                var source = NoteStore.OpenVaultSource();
                string folder = source.SnapshotFolder;
                using (new FileStream(Path.Combine(folder, "snapshot.db"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    source.Dispose();
                Assert.True(Directory.Exists(folder));
                Directory.CreateDirectory(root);
                string moved = Path.Combine(root, Path.GetFileName(folder));
                Directory.Move(folder, moved);

                VaultSnapshot.RemoveLeftovers(root);

                Assert.False(Directory.Exists(moved));
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch (IOException) { }
            }
        }

        private static void Exec(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
