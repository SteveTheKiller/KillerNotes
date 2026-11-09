using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace KillerNotes.Services
{
    internal static class VaultSnapshot
    {
        internal static NoteStore.VaultSource Capture(NoteStore.BackupSource origin, string? databaseId,
            object session, CancellationToken cancellation)
        {
            string folder = Path.Combine(Path.GetTempPath(), "KillerNotes", "vault-" + Guid.NewGuid().ToString("N"));
            SqliteConnection? destination = null;
            try
            {
                CheckOwner(session, cancellation);
                var from = new SqliteConnectionStringBuilder { DataSource = origin.Path,
                    Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1 };
                var to = new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, "snapshot.db"), Pooling = false };
                if (!string.IsNullOrEmpty(origin.Password)) { from.Password = origin.Password; to.Password = origin.Password; }
                using var source = new SqliteConnection(from.ConnectionString);
                source.Open();
                if (databaseId != null)
                {
                    using var identity = source.CreateCommand();
                    identity.CommandText = "SELECT value FROM kn_meta WHERE key = 'database_id'";
                    if (!string.Equals(identity.ExecuteScalar() as string, databaseId, StringComparison.Ordinal))
                        throw new IOException("The export source database has changed.");
                }
                Directory.CreateDirectory(folder);
                destination = new SqliteConnection(to.ConnectionString);
                destination.Open();
                using (var backup = SQLitePCL.raw.sqlite3_backup_init(destination.Handle!, "main", source.Handle!, "main"))
                    Copy(backup, () => CheckOwner(session, cancellation), cancellation, TimeSpan.FromMinutes(2));
                CheckOwner(session, cancellation);
                return new NoteStore.VaultSource(destination, folder);
            }
            catch (Exception failure)
            {
                destination?.Dispose();
                try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
                catch (Exception cleanup)
                {
                    throw new IOException("The failed export snapshot could not be removed: " + folder,
                        new AggregateException(failure, cleanup));
                }
                throw;
            }
        }

        internal static void Copy(SQLitePCL.sqlite3_backup backup, Action checkOwner, CancellationToken cancellation,
            TimeSpan timeout, Action? afterStep = null)
        {
            if (backup == null || backup.IsInvalid) throw new IOException("The export snapshot could not start.");
            var clock = Stopwatch.StartNew();
            int restarts = 0, lastRemaining = int.MaxValue;
            while (true)
            {
                checkOwner();
                if (clock.Elapsed > timeout) throw new IOException("The export snapshot timed out.");
                // A write through any other connection (an autosave) restarts the copy from its
                // first page. While the user keeps typing, a large vault could restart until the
                // timeout, so after a few restarts the rest is copied in one pass under one read
                // lock; saves wait for that pass, which is what the busy timeout is for.
                int result = SQLitePCL.raw.sqlite3_backup_step(backup, restarts >= MaxRestarts ? -1 : 128);
                if (result == SQLitePCL.raw.SQLITE_DONE) break;
                if (result != SQLitePCL.raw.SQLITE_OK && result != SQLitePCL.raw.SQLITE_BUSY && result != SQLitePCL.raw.SQLITE_LOCKED)
                    throw new IOException("The export snapshot could not finish (SQLite " + result + ").");
                int remaining = SQLitePCL.raw.sqlite3_backup_remaining(backup);
                if (remaining > lastRemaining) restarts++;
                lastRemaining = remaining;
                afterStep?.Invoke();
                // Each step releases the source read lock, leaving room for live saves.
                if (cancellation.WaitHandle.WaitOne(result == SQLitePCL.raw.SQLITE_OK ? 1 : 20))
                    cancellation.ThrowIfCancellationRequested();
            }
        }

        private const int MaxRestarts = 3;

        /// <summary>Snapshot folders an earlier run could not remove (a scanner still held the
        /// copy, or the app was ended mid-export). Only the first instance calls this at start,
        /// before any export of its own can be running.</summary>
        internal static void RemoveLeftovers(string root)
        {
            if (!Directory.Exists(root)) return;
            foreach (string folder in Directory.GetDirectories(root, "vault-*"))
                try { Directory.Delete(folder, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
        }

        /// <summary>Removes a snapshot folder without failing an export that already finished; a
        /// folder still held is left for <see cref="RemoveLeftovers"/> at the next start.</summary>
        internal static void TryRemove(string folder)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); return; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                Thread.Sleep(100);
            }
        }

        private static void CheckOwner(object session, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!ReferenceEquals(NoteStore.OpenSession, session))
                throw new OperationCanceledException("The export's database session has closed.", cancellation);
        }
    }
}
