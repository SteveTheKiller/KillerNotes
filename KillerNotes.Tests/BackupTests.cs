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
    /// Backups (1.3.1): the naming, listing and pruning rules, the schedule rule, and the
    /// live copy of an encrypted database. Registry-backed settings are never read here; the
    /// pure overloads take everything as parameters.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class BackupTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));

        public BackupTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private static readonly DateTime When = new(2026, 9, 1, 14, 5, 0);

        [Fact]
        public void BackupNamesCarryTheStemAndTheStamp()
        {
            Assert.Equal("notes-2026-09-01-1405.kndb", BackupService.FileNameFor("notes.db", When));
            Assert.Equal("work", BackupService.Stem("work.db"));
        }

        [Fact]
        public void ListingIsNewestFirstAndOnlyThisDatabase()
        {
            foreach (string n in new[] { "notes-2026-08-30-0900.kndb", "notes-2026-09-01-1405.kndb", "notes-2026-08-31-2300.kndb",
                                         "other-2026-09-01-1405.kndb", "notes-copy.kndb", "notes-extra-2026-09-01-1405.kndb" })
                File.WriteAllText(Path.Combine(_dir, n), "x");

            var names = BackupService.ListBackups(_dir, "notes.db").Select(f => f.Name).ToList();
            Assert.Equal(["notes-2026-09-01-1405.kndb", "notes-2026-08-31-2300.kndb", "notes-2026-08-30-0900.kndb"], names);
        }

        [Fact]
        public void PruneKeepsTheNewestCopies()
        {
            for (int d = 1; d <= 6; d++)
                File.WriteAllText(Path.Combine(_dir, $"notes-2026-09-0{d}-1200.kndb"), "x");
            File.WriteAllText(Path.Combine(_dir, "other-2026-09-01-1200.kndb"), "x");

            Assert.Equal(4, BackupService.Prune(_dir, "notes.db", 2));
            var left = BackupService.ListBackups(_dir, "notes.db").Select(f => f.Name).ToList();
            Assert.Equal(["notes-2026-09-06-1200.kndb", "notes-2026-09-05-1200.kndb"], left);
            Assert.True(File.Exists(Path.Combine(_dir, "other-2026-09-01-1200.kndb")));   // untouched
        }

        [Fact]
        public void DueWhenNeverBackedUpOrOlderThanTheInterval()
        {
            Assert.True(BackupService.IsDue(null, 24, When));
            Assert.True(BackupService.IsDue(When.AddHours(-25), 24, When));
            Assert.False(BackupService.IsDue(When.AddHours(-23), 24, When));
            Assert.False(BackupService.IsDue(null, 0, When));   // off
        }

        [Fact]
        public void ALiveCopyOfAnEncryptedDatabaseOpensWithTheSamePassword()
        {
            using var store = new TempStore("hunter2");
            long id = NoteStore.Create("Secret");
            NoteStore.Save(id, "Secret", Encoding.UTF8.GetBytes("bytes"), "secret words");

            string dest = Path.Combine(_dir, "copy.kndb");
            NoteStore.BackupTo(dest);
            Assert.True(File.Exists(dest));
            Assert.True(NoteStore.IsEncryptedFile(dest));

            var csb = new SqliteConnectionStringBuilder { DataSource = dest, Password = "hunter2", Mode = SqliteOpenMode.ReadOnly };
            using var db = new SqliteConnection(csb.ConnectionString);
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT title FROM notes";
            Assert.Equal("Secret", (string)cmd.ExecuteScalar()!);
        }

        [Fact]
        public void BackupNowWritesPrunesAndRestoreMakesANewDatabase()
        {
            using var store = new TempStore();
            NoteStore.Create("Kept");
            string folder = Path.Combine(_dir, "backups");

            string first = BackupService.BackupNow(folder, NoteStore.ActiveDbFile, When, keep: 1);
            string second = BackupService.BackupNow(folder, NoteStore.ActiveDbFile, When.AddDays(1), keep: 1);
            Assert.False(File.Exists(first));   // pruned down to the newest one
            Assert.True(File.Exists(second));

            string restored = BackupService.RestoreToDataFolder(second, When.AddDays(2));
            Assert.StartsWith("test-restored-20260903-1405", restored);
            Assert.EndsWith(".db", restored);
            Assert.True(File.Exists(Path.Combine(NoteStore.DbDir, restored)));
            Assert.NotEqual(NoteStore.ActiveDbFile, restored);   // never over the original
        }
    }
}
