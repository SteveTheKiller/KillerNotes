using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The Back up now button runs the copy on a worker while the window keeps running autosave,
    /// MCP commands and the lock timers on the shared connection, so the worker backs up through
    /// its own connection to the file. The file and key are captured first, on the calling thread.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class BackupLiveSourceTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));
        private readonly System.Collections.Generic.Dictionary<string, string> _settings = new();
        private static readonly DateTime When = new(2026, 9, 1, 14, 5, 0);

        public BackupLiveSourceTests()
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

        [Fact]
        public void ABackupTakenFromCapturedDetailsSurvivesTheStoreClosingFirst()
        {
            using var store = new TempStore("hunter2");
            for (int i = 0; i < 5; i++) NoteStore.Create("Note " + i);
            string file = NoteStore.ActiveDbFile;
            var live = BackupService.CaptureLive(file);
            Assert.NotNull(live);

            // A lock arriving while the worker runs: the shared connection is gone and the key cleared.
            NoteStore.Close();
            Assert.False(NoteStore.IsOpen);

            string backup = BackupService.BackupNow(Path.Combine(_dir, "b"), file, When, 2, live);

            Assert.True(NoteStore.IsEncryptedFile(backup));
            Assert.Equal(5, CountNotes(backup, "hunter2"));
            Assert.Throws<SqliteException>(() => CountNotes(backup, "wrong"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("hunter2")]
        public async Task ABackupCompletesWhileAnotherConnectionKeepsWriting(string? password)
        {
            using var store = new TempStore(password);
            for (int i = 0; i < 50; i++) NoteStore.Create("Before " + i);
            string file = NoteStore.ActiveDbFile;
            var live = BackupService.CaptureLive(file);

            using var stop = new CancellationTokenSource();
            int written = 0;
            var writer = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    NoteStore.Create("During " + written);
                    Interlocked.Increment(ref written);
                }
            });
            try
            {
                SpinWait.SpinUntil(() => Volatile.Read(ref written) > 0, TimeSpan.FromSeconds(10));
                string backup = BackupService.BackupNow(Path.Combine(_dir, "b"), file, When, 2, live);
                SpinWait.SpinUntil(() => Volatile.Read(ref written) > 5, TimeSpan.FromSeconds(10));

                Assert.Equal(password != null, NoteStore.IsEncryptedFile(backup));
                Assert.True(CountNotes(backup, password) >= 50);   // a consistent snapshot, not a torn one
            }
            finally
            {
                stop.Cancel();
                await writer;   // a writer that threw fails the test here
            }
        }
    }
}
