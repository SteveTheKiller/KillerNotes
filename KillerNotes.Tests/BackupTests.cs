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
        public void LongDatabaseNamesBackUpTwiceAndRestore()
        {
            using var store = new TempStore();
            NoteStore.Create("Long name content");
            string source = store.DbPath;
            NoteStore.Close();
            string file = new string('n', 133) + ".db";
            File.Move(source, Path.Combine(NoteStore.DbDir, file));
            NoteStore.DemoDbFile = file;
            NoteStore.Open();
            string folder = Path.Combine(_dir, "long-names");
            string first = BackupService.BackupNow(folder, file, When, 2);
            string second = BackupService.BackupNow(folder, file, When, 2);
            Assert.NotEqual(first, second);
            Assert.Equal(2, BackupService.ListBackups(folder, file).Count);
            Assert.Equal(1, CountNotes(second));
            string restored = BackupService.RestoreToDataFolder(second, When);
            Assert.True(File.Exists(Path.Combine(NoteStore.DbDir, restored)));
        }

        [Fact]
        public void SameNamedSourcesRetainIndependentRestorableBackups()
        {
            string folder = Path.Combine(_dir, "shared-backups");
            string first;
            string second;
            using (var source = new TempStore())
            {
                NoteStore.Create("Source A");
                first = BackupService.BackupNow(folder, NoteStore.ActiveDbFile, When, 1);
            }
            using (var source = new TempStore())
            {
                NoteStore.Create("Source B");
                second = BackupService.BackupNow(folder, NoteStore.ActiveDbFile, When.AddDays(1), 1);
                Assert.True(File.Exists(first), "Source B pruned source A's only recovery copy");
                foreach (var pair in new[] { (first, "Source A"), (second, "Source B") })
                {
                    string restored = BackupService.RestoreToDataFolder(pair.Item1, When.AddDays(2));
                    NoteStore.Close();
                    NoteStore.DemoDbFile = restored;
                    NoteStore.Open();
                    Assert.Equal(pair.Item2, Assert.Single(NoteStore.List()).Title);
                }
            }
        }

        [Fact]
        public void BackupNamesCarryTheStemAndTheStamp()
        {
            string name = BackupService.FileNameFor("notes.db", When);
            Assert.StartsWith("notes~", name);
            Assert.EndsWith("-2026-09-01-1405.kndb", name);
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
                File.WriteAllText(Path.Combine(_dir, BackupService.FileNameFor("notes.db", new DateTime(2026, 9, d, 12, 0, 0))), "x");
            string legacy = Path.Combine(_dir, "notes-2026-09-01-1200.kndb");
            File.WriteAllText(legacy, "ambiguous legacy backup");
            File.WriteAllText(Path.Combine(_dir, "other-2026-09-01-1200.kndb"), "x");

            Assert.Equal(4, BackupService.Prune(_dir, "notes.db", 2));
            var left = BackupService.ListBackups(_dir, "notes.db", includeLegacy: false).Select(f => f.Name).ToList();
            Assert.Equal(new[] { BackupService.FileNameFor("notes.db", new DateTime(2026, 9, 6, 12, 0, 0)),
                BackupService.FileNameFor("notes.db", new DateTime(2026, 9, 5, 12, 0, 0)) }, left);
            Assert.True(File.Exists(legacy));
            Assert.True(File.Exists(Path.Combine(_dir, "other-2026-09-01-1200.kndb")));   // untouched
        }

        [Fact]
        public void PruneClearsPartialCopiesLeftByAKilledBackup()
        {
            string Make(string name, TimeSpan age)
            {
                string path = Path.Combine(_dir, name);
                File.WriteAllText(path, "x");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
                return path;
            }
            string guid = Guid.NewGuid().ToString("N");
            string stem = BackupService.GroupStem("notes.db");
            string stale = Make($"{stem}-2026-09-01-1200.kndb.{guid}.tmp", TimeSpan.FromDays(2));
            string staleNumbered = Make($"{stem}-2026-09-01-1200-01.kndb.{guid}.tmp", TimeSpan.FromDays(2));
            string inProgress = Make($"notes-2026-09-02-1200.kndb.{guid}.tmp", TimeSpan.FromMinutes(1));
            string otherDb = Make($"other-2026-09-01-1200.kndb.{guid}.tmp", TimeSpan.FromDays(2));
            string unrelated = Make("notes-2026-09-01-1200.kndb.mine.tmp", TimeSpan.FromDays(2));

            BackupService.Prune(_dir, "notes.db", 2);

            Assert.False(File.Exists(stale));
            Assert.False(File.Exists(staleNumbered));
            Assert.True(File.Exists(inProgress));   // possibly another host's backup still running
            Assert.True(File.Exists(otherDb));
            Assert.True(File.Exists(unrelated));
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

        [Fact]
        public void BackupsInTheSameMinuteKeepBothSnapshots()
        {
            using var store = new TempStore();
            string folder = Path.Combine(_dir, "same-minute");
            NoteStore.Create("First");
            string first = BackupService.BackupNow(folder, NoteStore.ActiveDbFile, When, keep: 2);
            NoteStore.Create("Second");
            string second = BackupService.BackupNow(folder, NoteStore.ActiveDbFile, When.AddSeconds(5), keep: 2);

            Assert.NotEqual(first, second);
            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
            Assert.Equal(2, BackupService.ListBackups(folder, NoteStore.ActiveDbFile).Count);
            Assert.Equal(1, CountNotes(first));
            Assert.Equal(2, CountNotes(second));
        }

        private static long CountNotes(string path)
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ConnectionString);
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM notes";
            return (long)cmd.ExecuteScalar()!;
        }
    }
}
