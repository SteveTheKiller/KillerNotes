// ═══════════════════════════════════════════════════════════
//  BACKUPS  -  copies of the database, on a schedule, somewhere else
// ═══════════════════════════════════════════════════════════
//
// A backup is the database file itself, copied as it is: an encrypted database gives an
// encrypted copy that opens with the same password, a plain one gives a plain copy. Nothing is
// re-encoded, so a backup is a .kndb like any other and opens by double-click or imports like a
// shared file. While the store is open the copy is taken through SQLite's backup API on the
// live connection, so it is consistent even mid-edit; with the store closed (the Databases
// dialog) a file copy is the same thing.
//
// Files are named "<database>-<yyyy-MM-dd-HHmm>.kndb" in the chosen folder, and the newest N
// are kept per database. The schedule is a wall-clock interval checked on launch and every
// quarter hour (Shell/Backup.cs): if the last backup of the open database is older than the
// interval, one is taken. There is no service and no task scheduler entry; a backup happens
// while the app is running, which is also the only time the database changes.
//
// Settings are app-wide (folder, interval, copies to keep); the last-backup stamp and the last
// failure are per database (1.6.0; before that one stamp, "file|timestamp", served whichever
// database had backed up last, so switching made the other look never backed up). The pure
// parts take their inputs as parameters, and the rest read through Settings, so the tests never
// touch the registry.
//
// HEALTH (1.6.0). A copy only counts once it has been checked: a live copy is opened with its
// key and passes SQLite's quick_check, a file copy matches its source byte for byte. A failure is
// remembered with its reason until a backup succeeds, and the shell keeps a warning on screen for
// a failing or overdue notepad (Shell/Backup.cs). Test restore reads a backup back from a
// temporary copy - integrity, notes, and every attachment against its own hash - and touches
// neither the backup nor the data folder.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;
using System.Text.RegularExpressions;

namespace KillerNotes.Services
{
    /// <summary>Where backup settings and stamps are read and written: the app's registry
    /// settings, or an in-memory table in the tests.</summary>
    internal sealed record BackupSettings(Func<string, string?> Get, Action<string, string> Set, Action<string> Remove)
    {
        internal static readonly BackupSettings App =
            new(global::KillerNotes.App.GetSetting, global::KillerNotes.App.SetSetting, global::KillerNotes.App.RemoveSetting);
    }

    internal enum BackupState { Off, Ok, Overdue, Failing }

    /// <summary>What Test restore read back from a backup. WorkFolder is the temporary copy's
    /// folder, already deleted by the time this is returned.</summary>
    internal sealed record RestoreCheck(string Integrity, int Notes, int Attachments, int DamagedAttachments, string WorkFolder)
    {
        public bool Passed => Integrity == "ok" && DamagedAttachments == 0;
    }

    internal static class BackupService
    {
        internal static BackupSettings Settings = BackupSettings.App;

        public const string FolderSetting = "BackupFolder";
        public const string HoursSetting = "BackupEveryHours";   // 0 or missing = off
        public const string KeepSetting = "BackupKeep";
        public const string LastSetting = "LastBackup";
        public const string FailureSetting = "BackupFailure";

        public const int DefaultHours = 24;
        public const int DefaultKeep = 10;

        // ---- Settings ----

        public static string? Folder => Settings.Get(FolderSetting) is string f && f.Length > 0 ? f : null;

        public static int Hours => int.TryParse(Settings.Get(HoursSetting), out int h) && h > 0 ? h : 0;

        public static int Keep => int.TryParse(Settings.Get(KeepSetting), out int k) && k > 0 ? k : DefaultKeep;

        public static bool Enabled => Hours > 0 && Folder != null;

        /// <summary>The per-database setting name: the database's full path, so two data folders
        /// holding the same file name keep separate stamps.</summary>
        private static string KeyFor(string prefix, string dbFile) =>
            prefix + ":" + Path.GetFullPath(Path.Combine(NoteStore.DbDir, dbFile)).ToLowerInvariant();

        private static string Stamp(DateTime when) => when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        private static DateTime? ParseStamp(string? text) =>
            DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

        public static DateTime? LastBackup(string dbFile)
        {
            if (ParseStamp(Settings.Get(KeyFor(LastSetting, dbFile))) is DateTime stamped) return stamped;
            // 1.3.1 to 1.5: one stamp for whichever database backed up last.
            if (Settings.Get(LastSetting) is not string raw) return null;
            int sep = raw.IndexOf('|');
            if (sep <= 0 || !string.Equals(raw[..sep], dbFile, StringComparison.OrdinalIgnoreCase)) return null;
            return DateTime.TryParse(raw[(sep + 1)..], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
        }

        /// <summary>A checked backup: stamps it and clears any remembered failure.</summary>
        public static void RememberBackup(string dbFile, DateTime when)
        {
            Settings.Set(KeyFor(LastSetting, dbFile), Stamp(when));
            Settings.Remove(KeyFor(FailureSetting, dbFile));
        }

        public static (DateTime When, string Reason)? LastFailure(string dbFile)
        {
            if (Settings.Get(KeyFor(FailureSetting, dbFile)) is not string raw) return null;
            int sep = raw.IndexOf('|');
            if (sep <= 0 || ParseStamp(raw[..sep]) is not DateTime when) return null;
            return (when, raw[(sep + 1)..]);
        }

        public static void RememberFailure(string dbFile, DateTime when, string reason) =>
            Settings.Set(KeyFor(FailureSetting, dbFile), Stamp(when) + "|" + reason);

        /// <summary>The pure health rule. Off without a schedule and a folder; failing when the
        /// last attempt failed after the last success; overdue when the last success is more than
        /// twice the interval old, which leaves room for a run of missed quarter-hour checks while
        /// the app was closed. A schedule that has not run yet is fine: the timer takes it.</summary>
        public static BackupState StateOf(int hours, bool folder, DateTime? last, DateTime? failed, DateTime now)
        {
            if (hours <= 0 || !folder) return BackupState.Off;
            if (failed is DateTime f && (last == null || f > last.Value)) return BackupState.Failing;
            if (last is DateTime l && now - l > TimeSpan.FromHours(hours * 2)) return BackupState.Overdue;
            return BackupState.Ok;
        }

        public static BackupState StateOf(string dbFile, DateTime now) =>
            StateOf(Hours, Folder != null, LastBackup(dbFile), LastFailure(dbFile)?.When, now);

        public static bool IsDue(string dbFile, DateTime now) =>
            Enabled && IsDue(LastBackup(dbFile), Hours, now);

        /// <summary>The pure schedule rule: due when never backed up, or the last one is at
        /// least `hours` old.</summary>
        public static bool IsDue(DateTime? last, int hours, DateTime now) =>
            hours > 0 && (last == null || now - last.Value >= TimeSpan.FromHours(hours));

        // ---- Names ----

        public static string Stem(string dbFile) => Path.GetFileNameWithoutExtension(dbFile);

        public static string FileNameFor(string dbFile, DateTime when) =>
            $"{GroupStem(dbFile)}-{when.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)}.kndb";

        public static string GroupStem(string dbFile)
        {
            string path = Path.GetFullPath(Path.Combine(NoteStore.DbDir, dbFile));
            if (File.Exists(path))
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var resolved = new StringBuilder(32768);
                uint count = GetFinalPathNameByHandle(file.SafeFileHandle, resolved, (uint)resolved.Capacity, 0);
                if (count == 0 || count >= resolved.Capacity) throw new IOException("Cannot identify backup source.");
                path = resolved.ToString();
            }
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path.Substring(8);
            else if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path.Substring(4);
            string authority = path.StartsWith(@"\\", StringComparison.Ordinal) ? "network" :
                Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string
                    ?? throw new IOException("Cannot identify the local backup source machine.");
            using var hash = SHA256.Create();
            string identity = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(
                authority.ToUpperInvariant() + "|" + path.ToUpperInvariant()))).Replace("-", "");
            // Reserve the timestamp, a full int collision counter, extension and pending GUID
            // within Windows' 255-character filename component limit.
            string label = Stem(dbFile);
            if (label.Length > 120)
                label = label.Substring(0, char.IsHighSurrogate(label[119]) ? 119 : 120);
            return label + "~" + identity;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);

        private static readonly Regex StampSuffix = new(@"-\d{4}-\d{2}-\d{2}-\d{4}(?:-\d{2,})?$", RegexOptions.Compiled);

        /// <summary>The backups of one database in a folder, newest first, by the stamp in the
        /// name rather than the file time so a copied-in file sorts where it belongs.</summary>
        public static List<FileInfo> ListBackups(string folder, string dbFile, bool includeLegacy = true)
        {
            var list = new List<FileInfo>();
            if (!Directory.Exists(folder)) return list;
            string stem = GroupStem(dbFile);
            string legacy = Stem(dbFile);
            foreach (string path in Directory.GetFiles(folder, "*.kndb"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                var m = StampSuffix.Match(name);
                if (!m.Success) continue;
                string owner = name[..m.Index];
                if (!string.Equals(owner, stem, StringComparison.OrdinalIgnoreCase) &&
                    !(includeLegacy && string.Equals(owner, legacy, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(new FileInfo(path));
            }
            return [.. list.OrderByDescending(f => Path.GetFileNameWithoutExtension(f.Name), StringComparer.Ordinal)];
        }

        private static readonly Regex PendingSuffix = new(@"-\d{4}-\d{2}-\d{2}-\d{4}(?:-\d{2,})?\.kndb\.[0-9a-f]{32}\.tmp$", RegexOptions.Compiled);

        // A partial copy younger than this may still be in progress, possibly from another host
        // backing up to the same shared folder.
        private static readonly TimeSpan PendingGrace = TimeSpan.FromDays(1);

        /// <summary>Deletes all but the newest `keep` backups of the database, and any partial
        /// copy a killed backup left behind. Returns the count of backups removed.</summary>
        public static int Prune(string folder, string dbFile, int keep)
        {
            int removed = 0;
            foreach (var old in ListBackups(folder, dbFile, includeLegacy: false).Skip(Math.Max(1, keep)))
            {
                try { old.Delete(); removed++; } catch { /* a locked file waits for the next pass */ }
            }
            RemoveStalePending(folder, dbFile);
            return removed;
        }

        private static void RemoveStalePending(string folder, string dbFile)
        {
            if (!Directory.Exists(folder)) return;
            string stem = GroupStem(dbFile);
            foreach (string path in Directory.GetFiles(folder, stem + "-*.tmp"))
            {
                string name = Path.GetFileName(path);
                var m = PendingSuffix.Match(name);
                if (!m.Success || !string.Equals(name[..m.Index], stem, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > PendingGrace) File.Delete(path);
                }
                catch { /* a locked file waits for the next pass */ }
            }
        }

        // ---- Doing it ----

        /// <summary>Writes one backup of `dbFile` into the folder, checks it, stamps the moment, and
        /// prunes. Returns the path written. Throws on failure so the caller can say why, after
        /// remembering the failure for the health warning.</summary>
        public static string BackupNow(string folder, string dbFile, DateTime now, int keep)
        {
            try { return WriteBackup(folder, dbFile, now, keep); }
            catch (Exception ex)
            {
                RememberFailure(dbFile, now, ex.Message);
                throw;
            }
        }

        private static string WriteBackup(string folder, string dbFile, DateTime now, int keep)
        {
            Directory.CreateDirectory(folder);
            string dest = Path.Combine(folder, FileNameFor(dbFile, now));
            string basePath = Path.Combine(folder, Path.GetFileNameWithoutExtension(dest));
            for (int n = 1; File.Exists(dest); n++) dest = $"{basePath}-{n:D2}.kndb";
            string pending = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                if (NoteStore.IsOpen && string.Equals(dbFile, NoteStore.ActiveDbFile, StringComparison.OrdinalIgnoreCase))
                {
                    NoteStore.BackupTo(pending);   // the live connection: consistent, and keyed like the source
                    NoteStore.VerifyBackupCopy(pending);
                }
                else
                {
                    // Closed, so nothing can change under the copy, and an encrypted file's key is
                    // not known here: the check is that the copy matches its source exactly.
                    string source = Path.Combine(NoteStore.DbDir, dbFile);
                    File.Copy(source, pending);
                    if (!SameBytes(source, pending)) throw new IOException("The backup copy does not match the database.");
                }
                File.Move(pending, dest);
            }
            finally
            {
                if (File.Exists(pending)) File.Delete(pending);
            }
            RememberBackup(dbFile, now);
            Prune(folder, dbFile, keep);
            return dest;
        }

        private static bool SameBytes(string a, string b)
        {
            using var sha = SHA256.Create();
            byte[] Hash(string path)
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return sha.ComputeHash(file);
            }
            return Hash(a).SequenceEqual(Hash(b));
        }

        private static SqliteConnection OpenCopy(string path, string? password)
        {
            var csb = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            if (!string.IsNullOrEmpty(password)) csb.Password = password;
            var db = new SqliteConnection(csb.ConnectionString);
            db.Open();
            return db;
        }

        private static object? Scalar(SqliteConnection db, string sql)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteScalar();
        }

        /// <summary>Opens a written copy with its key and runs quick_check; throws unless it is
        /// sound. A wrong key or damaged page throws from SQLite itself.</summary>
        public static void VerifyCopy(string path, string? password)
        {
            using var db = OpenCopy(path, password);
            string result = Scalar(db, "PRAGMA quick_check") as string ?? "";
            if (result != "ok") throw new IOException("The backup copy failed its check: " + result);
            Scalar(db, "SELECT count(*) FROM notes");
        }

        /// <summary>Test restore: reads a backup back the way a restore would, from a temporary
        /// copy, so neither the backup nor the data folder is touched. Checks integrity, counts the
        /// notes (trash included) and re-hashes every attachment. A wrong password throws
        /// SqliteException for the caller to ask again.</summary>
        public static RestoreCheck TestRestore(string backupPath, string? password)
        {
            string work = Path.Combine(Path.GetTempPath(), "KillerNotes", "restore-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                string copy = Path.Combine(work, "restore-test.db");
                File.Copy(backupPath, copy);
                using var db = OpenCopy(copy, password);
                string integrity = Scalar(db, "PRAGMA quick_check") as string ?? "";
                int notes = Convert.ToInt32(Scalar(db, "SELECT count(*) FROM notes"));
                int attachments = 0, damaged = 0;
                if (Convert.ToInt32(Scalar(db, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'attachments'")) > 0)
                {
                    using var cmd = db.CreateCommand();
                    cmd.CommandText = "SELECT hash, payload FROM attachments";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        attachments++;
                        var payload = reader.IsDBNull(1) ? [] : (byte[])reader.GetValue(1);
                        if (!string.Equals(Attachments.HashOf(payload), reader.GetString(0), StringComparison.OrdinalIgnoreCase))
                            damaged++;
                    }
                }
                return new RestoreCheck(integrity, notes, attachments, damaged, work);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(work, recursive: true); } catch { /* the temp folder is cleaned by Windows */ }
            }
        }

        // ---- Live counts for Test restore to compare against ----
        // The Databases dialog closes the store while it is up, so the open database's counts are
        // taken just before (SecurityController.ShowDatabases).

        private static (string Db, NoteStore.StoreCensus Census)? _census;

        public static void RememberCensus(string dbFile, NoteStore.StoreCensus census) => _census = (dbFile, census);

        public static NoteStore.StoreCensus? CensusFor(string dbFile)
        {
            if (NoteStore.IsOpen && string.Equals(dbFile, NoteStore.ActiveDbFile, StringComparison.OrdinalIgnoreCase))
                return NoteStore.Census();
            return _census is { } c && string.Equals(c.Db, dbFile, StringComparison.OrdinalIgnoreCase) ? c.Census : null;
        }

        /// <summary>Copies a backup into the data folder as a NEW database, never over the one
        /// it came from, and returns the new file name. Opening it is the user's next step.</summary>
        public static string RestoreToDataFolder(string backupPath, DateTime now)
        {
            string name = Path.GetFileNameWithoutExtension(backupPath);
            string stem = StampSuffix.Replace(name, "");
            stem = Regex.Replace(stem, @"~[0-9A-F]{64}$", "", RegexOptions.IgnoreCase);
            Directory.CreateDirectory(NoteStore.DbDir);
            string baseName = $"{stem}-restored-{now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}";
            string file = baseName + ".db";
            for (int n = 2; File.Exists(Path.Combine(NoteStore.DbDir, file)); n++)
                file = $"{baseName} ({n}).db";
            File.Copy(backupPath, Path.Combine(NoteStore.DbDir, file));
            return file;
        }
    }
}
