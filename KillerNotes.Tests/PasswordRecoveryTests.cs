using System;
using System.IO;
using System.Linq;
using System.Text;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public sealed class PasswordRecoveryTests
    {
        [Theory]
        [InlineData(null, "new key")]
        [InlineData("old key", "new key")]
        [InlineData("old key", null)]
        public void PasswordChangeRecoversWhenReplaceMovesTheOriginalThenFails(string? oldKey, string? newKey)
        {
            using var store = new TempStore(oldKey);
            long id = NoteStore.Create("Surviving note");
            byte[] content = Encoding.UTF8.GetBytes("Retained content");
            NoteStore.Save(id, "Surviving note", content, "Retained text");
            int replacements = 0;

            Exception? error = Record.Exception(() => NoteStore.SetPassword(newKey, (source, destination, backup) =>
            {
                if (++replacements == 1)
                {
                    File.Move(destination, backup);
                    throw new IOException("Replacement failed after renaming the original.", unchecked((int)0x80070499));
                }
                File.Replace(source, destination, backup);
            }));

            Assert.True(NoteStore.IsOpen);
            Assert.Equal("Surviving note", Assert.Single(NoteStore.List()).Title);
            Assert.Equal(content, NoteStore.LoadContent(id));
            Assert.Null(error);
            Assert.Equal(2, replacements);
            Assert.Equal(newKey != null, NoteStore.HasPassword);
            NoteStore.Close();
            Assert.Throws<SqliteException>(() => NoteStore.Open("incorrect key"));
            NoteStore.Open(newKey);
            Assert.Equal(id, Assert.Single(NoteStore.List()).Id);
            Assert.False(File.Exists(store.DbPath + ".bak"));
            Assert.False(File.Exists(store.DbPath + ".rekey"));
        }

        [Theory]
        [InlineData(null, "new key")]
        [InlineData("old key", "new key")]
        public void AFailedExportStageCanBeRetriedAtOnce(string? oldKey, string newKey)
        {
            using var store = new TempStore(oldKey);
            long id = NoteStore.Create("Kept");
            // A folder where the staged copy's journal must go: the export fails while writing.
            string obstruction = store.DbPath + ".rekey-journal";
            Directory.CreateDirectory(obstruction);
            try
            {
                Assert.ThrowsAny<Exception>(() => NoteStore.SetPassword(newKey));
                Assert.True(NoteStore.IsOpen);
                Assert.Equal(id, Assert.Single(NoteStore.List()).Id);
            }
            finally { Directory.Delete(obstruction, true); }

            NoteStore.SetPassword(newKey);   // same process, obstruction gone
            Assert.True(NoteStore.HasPassword);
            NoteStore.Close();
            NoteStore.Open(newKey);
            Assert.Equal(id, Assert.Single(NoteStore.List()).Id);
            Assert.False(File.Exists(store.DbPath + ".rekey"));
        }

        [Fact]
        public void FailedRestorationKeepsBothCompleteDatabasesWithoutCreatingAnEmptyOne()
        {
            using var store = new TempStore("old key");
            NoteStore.Create("Recovery note");
            FileStream? heldBackup = null;
            try
            {
                Assert.Throws<IOException>(() => NoteStore.SetPassword("new key", (source, destination, backup) =>
                {
                    File.Move(destination, backup);
                    heldBackup = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.None);
                    throw new IOException("Replacement failed after renaming the original.", unchecked((int)0x80070499));
                }));
                Assert.False(NoteStore.IsOpen);
                Assert.False(File.Exists(store.DbPath));
                Assert.True(File.Exists(store.DbPath + ".bak"));
                Assert.True(File.Exists(store.DbPath + ".rekey"));
            }
            finally { heldBackup?.Dispose(); }

            foreach (var copy in new[] { (Path: store.DbPath + ".bak", Key: "old key"), (Path: store.DbPath + ".rekey", Key: "new key") })
            {
                using var database = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = copy.Path, Password = copy.Key, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ConnectionString);
                database.Open();
                using var query = database.CreateCommand();
                query.CommandText = "SELECT title FROM notes";
                Assert.Equal("Recovery note", query.ExecuteScalar());
            }
            File.Copy(store.DbPath + ".bak", store.DbPath);
            NoteStore.Open("old key");
            Assert.Equal("Recovery note", Assert.Single(NoteStore.List()).Title);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("old key")]
        public void FailedRestorationRenameRetiresOnlyItsOwnCopyBeforeALaterPasswordChange(string? oldKey)
        {
            using var store = new TempStore(oldKey);
            long id = NoteStore.Create("Recovery lifecycle note");
            string directory = Path.GetDirectoryName(store.DbPath)!;
            string unrelated = store.DbPath + ".restore-unrelated";
            File.WriteAllText(unrelated, "Independent retained file");
            Assert.Throws<IOException>(() => NoteStore.SetPassword("interrupted key", (source, destination, backup) =>
            {
                File.Move(destination, backup);
                Directory.CreateDirectory(destination);
                throw new IOException("Replacement failed after renaming the original.", unchecked((int)0x80070499));
            }));
            Assert.False(NoteStore.IsOpen);
            Assert.True(Directory.Exists(store.DbPath));
            foreach (var copy in new[] { (Path: store.DbPath + ".bak", Key: oldKey), (Path: store.DbPath + ".rekey", Key: "interrupted key") })
            {
                using var database = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = copy.Path, Password = copy.Key ?? "", Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ConnectionString);
                database.Open();
                using var query = database.CreateCommand();
                query.CommandText = "SELECT title FROM notes";
                Assert.Equal("Recovery lifecycle note", query.ExecuteScalar());
            }
            string[] afterFailure = Directory.GetFiles(directory, Path.GetFileName(store.DbPath) + ".restore-*")
                .Where(path => path != unrelated).ToArray();
            Directory.Delete(store.DbPath);
            File.Move(store.DbPath + ".bak", store.DbPath);
            NoteStore.Open(oldKey);
            Assert.Equal(id, Assert.Single(NoteStore.List()).Id);
            NoteStore.SetPassword("final key");
            NoteStore.Close();
            Assert.Throws<SqliteException>(() => NoteStore.Open(oldKey));
            NoteStore.Open("final key");
            Assert.Equal(id, Assert.Single(NoteStore.List()).Id);
            string[] afterSuccess = Directory.GetFiles(directory, Path.GetFileName(store.DbPath) + ".restore-*")
                .Where(path => path != unrelated).ToArray();
            Assert.Equal("Independent retained file", File.ReadAllText(unrelated));
            Assert.False(File.Exists(store.DbPath + ".bak"));
            Assert.False(File.Exists(store.DbPath + ".rekey"));
            Assert.True(afterFailure.Length == 0 && afterSuccess.Length == 0,
                $"Owned restoration copies survived: after failure {afterFailure.Length}, after later password change {afterSuccess.Length}.");
        }

        [Fact]
        public void CleanupDenialReportsEveryRetainedCopyInTheTopLevelMessage()
        {
            using var store = new TempStore("old key");
            NoteStore.Create("Diagnostic recovery note");
            var retained = new System.Collections.Generic.List<string>();
            var error = Assert.Throws<IOException>(() => NoteStore.SetPassword("new key", (source, destination, backup) =>
            {
                File.Move(destination, backup);
                Directory.CreateDirectory(destination);
                throw new IOException("Replacement failed after renaming the original.", unchecked((int)0x80070499));
            }, path =>
            {
                retained.Add(path);
                throw new UnauthorizedAccessException("Owned-copy cleanup was refused.");
            }));
            Assert.False(NoteStore.IsOpen);
            Assert.Equal(2, retained.Distinct().Count());
            foreach (var copy in retained.Select(path => (Path: path, Key: "old key")).Concat(new[]
            {
                (Path: store.DbPath + ".bak", Key: "old key"), (Path: store.DbPath + ".rekey", Key: "new key")
            }))
            {
                Assert.True(File.Exists(copy.Path));
                using var database = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = copy.Path, Password = copy.Key, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ConnectionString);
                database.Open();
                using var query = database.CreateCommand();
                query.CommandText = "SELECT title FROM notes";
                Assert.Equal("Diagnostic recovery note", query.ExecuteScalar());
            }
            Assert.IsType<AggregateException>(error.InnerException);
            foreach (string path in retained.Concat(new[] { store.DbPath + ".bak", store.DbPath + ".rekey" }))
                Assert.Contains(path, error.Message);
        }

        [Fact]
        public void SharingFailuresStillTryEightReplacementsBeforeTheMoveFallback()
        {
            using var store = new TempStore("old key");
            NoteStore.Create("Fallback note");
            int replacements = 0;
            NoteStore.SetPassword("new key", (source, destination, backup) =>
            {
                replacements++;
                throw new IOException("Synthetic sharing failure.", unchecked((int)0x80070020));
            });
            Assert.Equal(8, replacements);
            Assert.Equal("Fallback note", Assert.Single(NoteStore.List()).Title);
            NoteStore.Close();
            Assert.Throws<SqliteException>(() => NoteStore.Open("old key"));
            NoteStore.Open("new key");
            Assert.Equal("Fallback note", Assert.Single(NoteStore.List()).Title);
        }
    }
}
