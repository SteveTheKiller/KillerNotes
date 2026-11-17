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
    /// Guards SQLCipher upgrades of the vendored e_sqlcipher.dll. The fixture under Fixtures was
    /// written by the previous native build (4.19.0), so every test here opens a database the
    /// current build did not create: the data, the FTS search, wrong-password refusal and a
    /// password change all have to survive the native swap.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class SqlCipherUpgradeCompatTests
    {
        private const string FixtureName = "sqlcipher-4.19.0-notes.db";
        private const string FixturePassword = "compat 4.19 key";
        private const string FixtureTitle = "Ledger from 4.19";
        private const string FixturePlain = "quartz reliquary backstage";
        private const string ExpectedCipherVersion = "4.20.0 community";

        private static string FixturePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", FixtureName);

        private sealed class FixtureCopy : IDisposable
        {
            private readonly string _dir;

            public FixtureCopy()
            {
                _dir = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_dir);
                File.Copy(FixturePath, Path.Combine(_dir, "test.db"));
                NoteStore.DbDirOverride = _dir;
                NoteStore.DemoDbFile = "test.db";
            }

            public void Dispose()
            {
                NoteStore.Close();
                NoteStore.DbDirOverride = null;
                NoteStore.DemoDbFile = null;
                try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void BundledNativeReportsExpectedCipherVersion()
        {
            _ = NoteStore.IsOpen;   // static constructor loads the embedded native
            using var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA cipher_version;";
            Assert.Equal(ExpectedCipherVersion, cmd.ExecuteScalar() as string);
        }

        [Fact]
        public void OlderBuildDatabaseOpensWithDataAndSearch()
        {
            using var copy = new FixtureCopy();
            Assert.True(NoteStore.IsEncryptedFile(NoteStore.DbPath));

            NoteStore.Open(FixturePassword);
            var note = Assert.Single(NoteStore.List());
            Assert.Equal(FixtureTitle, note.Title);
            Assert.Equal(Encoding.UTF8.GetBytes(FixturePlain), NoteStore.LoadContent(note.Id));
            Assert.Equal(FixtureTitle, Assert.Single(NoteStore.List("reliquary")).Title);
            Assert.Empty(NoteStore.List("nonexistentword"));
        }

        [Fact]
        public void OlderBuildDatabaseRefusesWrongPassword()
        {
            using var copy = new FixtureCopy();
            Assert.Throws<SqliteException>(() => NoteStore.Open("not the key"));
            Assert.False(NoteStore.IsOpen);
        }

        [Fact]
        public void OlderBuildDatabaseAcceptsPasswordChange()
        {
            using var copy = new FixtureCopy();
            NoteStore.Open(FixturePassword);
            NoteStore.SetPassword("rekeyed on 4.20");
            NoteStore.Close();

            Assert.Throws<SqliteException>(() => NoteStore.Open(FixturePassword));
            NoteStore.Open("rekeyed on 4.20");
            Assert.Equal(FixtureTitle, Assert.Single(NoteStore.List("quartz")).Title);
        }

        /// <summary>
        /// Writes a fresh fixture with whatever native is bundled. Run it only on the OLD build,
        /// right before swapping the DLL, by setting KILLERNOTES_WRITE_SQLCIPHER_FIXTURE to the
        /// output path. Without the variable it does nothing.
        /// </summary>
        [Fact]
        public void WriteFixtureWhenRequested()
        {
            string? target = Environment.GetEnvironmentVariable("KILLERNOTES_WRITE_SQLCIPHER_FIXTURE");
            if (string.IsNullOrEmpty(target)) return;

            using var store = new TempStore(password: FixturePassword);
            long id = NoteStore.Create(FixtureTitle);
            NoteStore.Save(id, FixtureTitle, Encoding.UTF8.GetBytes(FixturePlain), FixturePlain);
            string path = store.DbPath;
            NoteStore.Close();
            File.Copy(path, target, overwrite: true);
        }
    }
}
