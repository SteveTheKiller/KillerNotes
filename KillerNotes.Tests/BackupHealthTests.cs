using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Backup health (1.6.0, KN-P05): a last-backup stamp and failure per notepad, a check of
    /// every copy before it counts, and a test restore that reads a backup back without touching
    /// the data folder. Settings go to an in-memory table, never the registry.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class BackupHealthTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, string> _settings = new(StringComparer.OrdinalIgnoreCase);
        private static readonly DateTime When = new(2026, 10, 21, 9, 30, 0);

        public BackupHealthTests()
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

        [Fact]
        public void EachNotepadKeepsItsOwnLastBackup()
        {
            using var store = new TempStore();
            BackupService.RememberBackup("a.db", When);
            BackupService.RememberBackup("b.db", When.AddHours(1));

            Assert.Equal(When, BackupService.LastBackup("a.db"));
            Assert.Equal(When.AddHours(1), BackupService.LastBackup("b.db"));
        }

        [Fact]
        public void TheOldSingleStampStillCountsForItsNotepad()
        {
            using var store = new TempStore();
            _settings[BackupService.LastSetting] = "a.db|2026-10-20 08:00:00";

            Assert.Equal(new DateTime(2026, 10, 20, 8, 0, 0), BackupService.LastBackup("a.db"));
            Assert.Null(BackupService.LastBackup("b.db"));
        }

        [Theory]
        [InlineData(0, true, null, null, "Off")]
        [InlineData(24, false, null, null, "Off")]
        [InlineData(24, true, null, null, "Ok")]          // just turned on; the timer takes the first
        [InlineData(24, true, -47, null, "Ok")]
        [InlineData(24, true, -49, null, "Overdue")]      // more than twice the interval
        [InlineData(24, true, -2, -1, "Failing")]         // failed after the last success
        [InlineData(24, true, -1, -2, "Ok")]              // a success since the failure
        [InlineData(24, true, null, -1, "Failing")]
        public void HealthFollowsTheLastSuccessAndFailure(int hours, bool folder, int? lastAgo, int? failedAgo, string expected)
        {
            DateTime? last = lastAgo is int l ? When.AddHours(l) : null;
            DateTime? failed = failedAgo is int f ? When.AddHours(f) : null;
            Assert.Equal(expected, BackupService.StateOf(hours, folder, last, failed, When).ToString());
        }

        [Fact]
        public void AFailedBackupIsRememberedWithItsReasonUntilOneSucceeds()
        {
            using var store = new TempStore();
            NoteStore.Create("Note");
            string blocked = Path.Combine(_dir, "not-a-folder");
            File.WriteAllText(blocked, "a file where the folder should be");

            Assert.ThrowsAny<IOException>(() => BackupService.BackupNow(blocked, NoteStore.ActiveDbFile, When, 5));
            var failure = BackupService.LastFailure(NoteStore.ActiveDbFile);
            Assert.NotNull(failure);
            Assert.Equal(When, failure!.Value.When);
            Assert.False(string.IsNullOrEmpty(failure.Value.Reason));
            Assert.Null(BackupService.LastBackup(NoteStore.ActiveDbFile));

            BackupService.BackupNow(Path.Combine(_dir, "good"), NoteStore.ActiveDbFile, When.AddMinutes(5), 5);
            Assert.Null(BackupService.LastFailure(NoteStore.ActiveDbFile));
            Assert.Equal(When.AddMinutes(5), BackupService.LastBackup(NoteStore.ActiveDbFile));
        }

        [Fact]
        public void ADamagedCopyFailsItsCheck()
        {
            using var store = new TempStore("pw");
            for (int i = 0; i < 40; i++) NoteStore.Save(NoteStore.Create("Note " + i), "Note " + i, new byte[2000], new string('x', 2000));
            string backup = BackupService.BackupNow(Path.Combine(_dir, "b"), NoteStore.ActiveDbFile, When, 5);
            BackupService.VerifyCopy(backup, "pw");

            var bytes = File.ReadAllBytes(backup);
            for (int i = bytes.Length / 2; i < bytes.Length / 2 + 4096; i++) bytes[i] ^= 0x5A;
            File.WriteAllBytes(backup, bytes);
            Assert.ThrowsAny<Exception>(() => BackupService.VerifyCopy(backup, "pw"));
        }

        [Fact]
        public void TestRestoreReadsEverythingBackWithoutTouchingTheBackupOrDataFolder() => Sta.Run(() =>
        {
            using var store = new TempStore("pw");
            long id = NoteStore.Create("With a sketch");
            var sketch = new Attachment(AttachmentKind.Sketch, new byte[] { 1, 2, 3 });
            AttachmentStoreTests.SaveWith(id, sketch);
            NoteStore.Create("Second");
            string backup = BackupService.BackupNow(Path.Combine(_dir, "b"), NoteStore.ActiveDbFile, When, 5);
            var dataFiles = Directory.GetFiles(NoteStore.DbDir);
            string before = Hash(backup);

            Assert.ThrowsAny<SqliteException>(() => BackupService.TestRestore(backup, "wrong"));
            var check = BackupService.TestRestore(backup, "pw");

            Assert.True(check.Passed);
            Assert.Equal(2, check.Notes);
            Assert.Equal(1, check.Attachments);
            Assert.Equal(0, check.DamagedAttachments);
            Assert.Equal(before, Hash(backup));
            Assert.Equal(dataFiles, Directory.GetFiles(NoteStore.DbDir));
            Assert.False(Directory.Exists(check.WorkFolder));
        });

        [Fact]
        public void TestRestoreFindsADamagedAttachment() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Recording");
            var recording = new Attachment(AttachmentKind.Recording, new byte[] { 4, 5, 6 }, 1000);
            AttachmentStoreTests.SaveWith(id, recording);
            string backup = BackupService.BackupNow(Path.Combine(_dir, "b"), NoteStore.ActiveDbFile, When, 5);
            using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Pooling = false }.ConnectionString))
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = "UPDATE attachments SET payload = x'00'";
                cmd.ExecuteNonQuery();
            }

            var check = BackupService.TestRestore(backup, null);

            Assert.False(check.Passed);
            Assert.Equal(1, check.DamagedAttachments);
        });

        [Fact]
        public void LiveCountsComeFromTheOpenNotepad()
        {
            using var store = new TempStore();
            NoteStore.Create("One");
            long trashed = NoteStore.Create("Two");
            NoteStore.Trash(trashed);

            var census = NoteStore.Census();

            Assert.Equal(2, census.Notes);
            Assert.Equal(0, census.Attachments);
        }

        private static string Hash(string path)
        {
            using var sha = SHA256.Create();
            using var file = File.OpenRead(path);
            return Convert.ToBase64String(sha.ComputeHash(file));
        }
    }
}
