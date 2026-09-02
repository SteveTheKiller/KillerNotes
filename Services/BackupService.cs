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
// Settings are app-wide (folder, interval, copies to keep); the last-backup stamp is per
// database, "file|timestamp", the LastNote pattern. The pure parts take their inputs as
// parameters so the tests never touch the registry.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KillerNotes.Services
{
    internal static class BackupService
    {
        public const string FolderSetting = "BackupFolder";
        public const string HoursSetting = "BackupEveryHours";   // 0 or missing = off
        public const string KeepSetting = "BackupKeep";
        public const string LastSetting = "LastBackup";

        public const int DefaultHours = 24;
        public const int DefaultKeep = 10;

        // ---- Settings ----

        public static string? Folder => App.GetSetting(FolderSetting) is string f && f.Length > 0 ? f : null;

        public static int Hours => int.TryParse(App.GetSetting(HoursSetting), out int h) && h > 0 ? h : 0;

        public static int Keep => int.TryParse(App.GetSetting(KeepSetting), out int k) && k > 0 ? k : DefaultKeep;

        public static bool Enabled => Hours > 0 && Folder != null;

        public static DateTime? LastBackup(string dbFile)
        {
            if (App.GetSetting(LastSetting) is not string raw) return null;
            int sep = raw.IndexOf('|');
            if (sep <= 0 || !string.Equals(raw[..sep], dbFile, StringComparison.OrdinalIgnoreCase)) return null;
            return DateTime.TryParse(raw[(sep + 1)..], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
        }

        public static void RememberBackup(string dbFile, DateTime when) =>
            App.SetSetting(LastSetting, $"{dbFile}|{when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");

        public static bool IsDue(string dbFile, DateTime now) =>
            Enabled && IsDue(LastBackup(dbFile), Hours, now);

        /// <summary>The pure schedule rule: due when never backed up, or the last one is at
        /// least `hours` old.</summary>
        public static bool IsDue(DateTime? last, int hours, DateTime now) =>
            hours > 0 && (last == null || now - last.Value >= TimeSpan.FromHours(hours));

        // ---- Names ----

        public static string Stem(string dbFile) => Path.GetFileNameWithoutExtension(dbFile);

        public static string FileNameFor(string dbFile, DateTime when) =>
            $"{Stem(dbFile)}-{when.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)}.kndb";

        private static readonly Regex StampSuffix = new(@"-\d{4}-\d{2}-\d{2}-\d{4}$", RegexOptions.Compiled);

        /// <summary>The backups of one database in a folder, newest first, by the stamp in the
        /// name rather than the file time so a copied-in file sorts where it belongs.</summary>
        public static List<FileInfo> ListBackups(string folder, string dbFile)
        {
            var list = new List<FileInfo>();
            if (!Directory.Exists(folder)) return list;
            string stem = Stem(dbFile);
            foreach (string path in Directory.GetFiles(folder, stem + "-*.kndb"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                var m = StampSuffix.Match(name);
                if (!m.Success || !string.Equals(name[..m.Index], stem, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new FileInfo(path));
            }
            return list.OrderByDescending(f => Path.GetFileNameWithoutExtension(f.Name), StringComparer.Ordinal).ToList();
        }

        /// <summary>Deletes all but the newest `keep` backups of the database. Returns the count removed.</summary>
        public static int Prune(string folder, string dbFile, int keep)
        {
            int removed = 0;
            foreach (var old in ListBackups(folder, dbFile).Skip(Math.Max(1, keep)))
            {
                try { old.Delete(); removed++; } catch { /* a locked file waits for the next pass */ }
            }
            return removed;
        }

        // ---- Doing it ----

        /// <summary>Writes one backup of `dbFile` into the folder, stamps the moment, and prunes.
        /// Returns the path written. Throws on failure so the caller can say why.</summary>
        public static string BackupNow(string folder, string dbFile, DateTime now, int keep)
        {
            Directory.CreateDirectory(folder);
            string dest = Path.Combine(folder, FileNameFor(dbFile, now));
            if (NoteStore.IsOpen && string.Equals(dbFile, NoteStore.ActiveDbFile, StringComparison.OrdinalIgnoreCase))
            {
                NoteStore.BackupTo(dest);   // the live connection: consistent, and keyed like the source
            }
            else
            {
                File.Copy(Path.Combine(NoteStore.DbDir, dbFile), dest, overwrite: true);
            }
            RememberBackup(dbFile, now);
            Prune(folder, dbFile, keep);
            return dest;
        }

        /// <summary>Copies a backup into the data folder as a NEW database, never over the one
        /// it came from, and returns the new file name. Opening it is the user's next step.</summary>
        public static string RestoreToDataFolder(string backupPath, DateTime now)
        {
            string name = Path.GetFileNameWithoutExtension(backupPath);
            string stem = StampSuffix.Replace(name, "");
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
