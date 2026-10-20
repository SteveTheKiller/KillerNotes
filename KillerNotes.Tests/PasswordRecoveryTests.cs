using System;
using System.IO;
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
