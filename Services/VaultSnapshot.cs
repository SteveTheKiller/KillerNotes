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
                {
                    if (backup == null || backup.IsInvalid) throw new IOException("The export snapshot could not start.");
                    var clock = Stopwatch.StartNew();
                    while (true)
                    {
                        CheckOwner(session, cancellation);
                        if (clock.Elapsed > TimeSpan.FromMinutes(2)) throw new IOException("The export snapshot timed out.");
                        int result = SQLitePCL.raw.sqlite3_backup_step(backup, 128);
                        if (result == SQLitePCL.raw.SQLITE_DONE) break;
                        if (result != SQLitePCL.raw.SQLITE_OK && result != SQLitePCL.raw.SQLITE_BUSY && result != SQLitePCL.raw.SQLITE_LOCKED)
                            throw new IOException("The export snapshot could not finish (SQLite " + result + ").");
                        // Each step releases the source read lock, leaving room for live saves.
                        if (cancellation.WaitHandle.WaitOne(result == SQLitePCL.raw.SQLITE_OK ? 1 : 20))
                            cancellation.ThrowIfCancellationRequested();
                    }
                }
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

        private static void CheckOwner(object session, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!ReferenceEquals(NoteStore.OpenSession, session))
                throw new OperationCanceledException("The export's database session has closed.", cancellation);
        }
    }
}
