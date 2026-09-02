// ═══════════════════════════════════════════════════════════
//  SCHEDULED BACKUPS  -  the timer half of BackupService
// ═══════════════════════════════════════════════════════════
//
// The settings and the copying live in Services/BackupService.cs and the Backups dialog. This
// is only the clock: a check shortly after the database opens and every quarter hour after
// that, which takes a backup when the last one of the open database is older than the chosen
// interval. Nothing runs while the app is closed, and nothing is installed into Windows.

using System;
using System.IO;
using System.Windows.Threading;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private readonly DispatcherTimer _backupTimer = new() { Interval = TimeSpan.FromMinutes(15) };

        private void InitBackupSchedule()
        {
            _backupTimer.Tick += (_, _) => RunScheduledBackup();
            _backupTimer.Start();
        }

        /// <summary>Takes a backup of the open database if one is due. Quiet when backups are
        /// off, the database is read-only or a demo, or the last one is recent enough.</summary>
        private void RunScheduledBackup()
        {
            if (!NoteStore.IsOpen || NoteStore.IsReadOnly || NoteStore.DemoDbFile != null) return;
            if (!BackupService.IsDue(NoteStore.ActiveDbFile, DateTime.Now)) return;
            RunBackup();
        }

        /// <summary>Alt+B (Shortcuts.cs): a backup right now, schedule or no schedule, as long
        /// as a folder has been chosen in Manage databases.</summary>
        private void BackupNowShortcut()
        {
            if (!NoteStore.IsOpen || NoteStore.DemoDbFile != null) return;
            if (NoteStore.IsReadOnly) { FlashStatus(string.Format(Loc("Str_St_ReadOnly"), NoteStore.ReadOnlyOwner)); return; }
            if (BackupService.Folder == null) { FlashStatus(Loc("Str_St_NoBackupFolder")); return; }
            RunBackup();
        }

        private void RunBackup()
        {
            string db = NoteStore.ActiveDbFile;
            var now = DateTime.Now;
            if (BackupService.Folder is not string folder) return;

            SaveCurrentNote(refreshList: false);   // the copy should hold what is on screen
            try
            {
                string path = BackupService.BackupNow(folder, db, now, BackupService.Keep);
                FlashStatus(string.Format(Loc("Str_St_BackedUp"), Path.GetFileName(path)));
            }
            catch (Exception ex)
            {
                FlashStatus(string.Format(Loc("Str_St_BackupFailed"), ex.Message));
            }
        }
    }
}
