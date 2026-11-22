using System;
using System.Collections.Generic;
using System.IO;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A password change re-keys the backups in the backup folder, so history taken before the
    /// change stops opening with the old password (or with none, for a vault that had none).
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class BackupRekeyTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, string> _settings = new();

        public BackupRekeyTests()
        {
            Directory.CreateDirectory(_dir);
            BackupService.Settings = new BackupSettings(
                name => _settings.TryGetValue(name, out var v) ? v : null,
                (name, value) => _settings[name] = value,
                name => _settings.Remove(name));
        }

        public void Dispose()
        {
            BackupService.Settings = BackupSettings.App;
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private static long CountNotes(string path, string? password)
        {
            var csb = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            if (password != null) csb.Password = password;
            using var db = new SqliteConnection(csb.ConnectionString);
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM notes";
            return (long)cmd.ExecuteScalar()!;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("old secret")]
        public void ChangingThePasswordRekeysEveryEarlierBackup(string? oldPassword)
        {
            using var store = new TempStore(oldPassword);
            for (int i = 0; i < 3; i++) NoteStore.Create("Note " + i);
            string file = NoteStore.ActiveDbFile;
            string folder = Path.Combine(_dir, "b");
            string first = BackupService.BackupNow(folder, file, new DateTime(2026, 9, 1, 10, 0, 0), 5);
            string second = BackupService.BackupNow(folder, file, new DateTime(2026, 9, 1, 11, 0, 0), 5);

            NoteStore.SetPassword("new secret");
            var result = BackupService.RekeyBackups(folder, file, oldPassword, "new secret");

            Assert.Equal(2, result.Rekeyed);
            Assert.Equal(0, result.Skipped);
            foreach (string backup in new[] { first, second })
            {
                Assert.Equal(3, CountNotes(backup, "new secret"));
                Assert.Throws<SqliteException>(() => CountNotes(backup, oldPassword));
            }
        }

        [Fact]
        public void ABackupUnderAnOlderPasswordIsLeftAloneAndCounted()
        {
            using var store = new TempStore("first");
            NoteStore.Create("Note");
            string file = NoteStore.ActiveDbFile;
            string folder = Path.Combine(_dir, "b");
            string older = BackupService.BackupNow(folder, file, new DateTime(2026, 9, 1, 10, 0, 0), 5);
            NoteStore.SetPassword("second");
            string current = BackupService.BackupNow(folder, file, new DateTime(2026, 9, 1, 11, 0, 0), 5);

            NoteStore.SetPassword("third");
            var result = BackupService.RekeyBackups(folder, file, "second", "third");

            Assert.Equal(1, result.Rekeyed);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(1, CountNotes(current, "third"));
            Assert.Equal(1, CountNotes(older, "first"));
        }

        [Fact]
        public void RemovingThePasswordLeavesTheBackupsReadableWithoutOne()
        {
            using var store = new TempStore("old");
            NoteStore.Create("Note");
            string file = NoteStore.ActiveDbFile;
            string folder = Path.Combine(_dir, "b");
            string backup = BackupService.BackupNow(folder, file, new DateTime(2026, 9, 1, 10, 0, 0), 5);

            NoteStore.SetPassword(null);
            var result = BackupService.RekeyBackups(folder, file, "old", null);

            Assert.Equal(1, result.Rekeyed);
            Assert.False(NoteStore.IsEncryptedFile(backup));
            Assert.Equal(1, CountNotes(backup, null));
        }
    }
}
