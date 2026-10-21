// ═══════════════════════════════════════════════════════════
//  SCHEDULED BACKUPS  -  the timer half of BackupService
// ═══════════════════════════════════════════════════════════
//
// The settings and the copying live in Services/BackupService.cs and the Backups dialog. This
// is only the clock: a check shortly after the database opens and every quarter hour after
// that, which takes a backup when the last one of the open database is older than the chosen
// interval. Nothing runs while the app is closed, and nothing is installed into Windows.
//
// It also keeps the footer's backup warning (1.6.0): shown while the open notepad's backups are
// failing or overdue, with the reason in its tooltip, and it opens Backups. A scheduled retry
// that fails again stays quiet, since the warning is already on screen saying so.

using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using KillerNotes.Controls;
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
            if (BackupService.IsDue(NoteStore.ActiveDbFile, DateTime.Now)) RunBackup(scheduled: true);
            else UpdateBackupHealth();   // overdue can begin without any backup being attempted
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

        private void RunBackup(bool scheduled = false)
        {
            string db = NoteStore.ActiveDbFile;
            var now = DateTime.Now;
            if (BackupService.Folder is not string folder) return;

            if (!SaveCurrentNote(refreshList: false)) return;
            try
            {
                string path = BackupService.BackupNow(folder, db, now, BackupService.Keep);
                FlashStatus(string.Format(Loc("Str_St_BackedUp"), Path.GetFileName(path)));
            }
            catch (Exception ex)
            {
                if (!scheduled || BackupWarningBtn.Visibility != Visibility.Visible)
                    FlashStatus(string.Format(Loc("Str_St_BackupFailed"), ex.Message));
            }
            UpdateBackupHealth();
        }

        /// <summary>Shows or hides the footer warning for the open notepad.</summary>
        private void UpdateBackupHealth()
        {
            if (!NoteStore.IsOpen || NoteStore.DemoDbFile != null)
            {
                BackupWarningBtn.Visibility = Visibility.Collapsed;
                return;
            }
            string db = NoteStore.ActiveDbFile;
            var state = BackupService.StateOf(db, DateTime.Now);
            string? tip = state switch
            {
                BackupState.Failing when BackupService.LastFailure(db) is { } f =>
                    string.Format(Loc("Str_Bk_WarnFailingTip"), f.When.ToString("yyyy-MM-dd HH:mm"), f.Reason),
                BackupState.Overdue when BackupService.LastBackup(db) is DateTime last =>
                    string.Format(Loc("Str_Bk_WarnOverdueTip"), last.ToString("yyyy-MM-dd HH:mm")),
                _ => null,
            };
            if (tip == null)
            {
                BackupWarningBtn.Visibility = Visibility.Collapsed;
                return;
            }
            BackupWarningBtn.Content = Loc(state == BackupState.Failing ? "Str_Bk_WarnFailing" : "Str_Bk_WarnOverdue");
            BackupWarningBtn.ToolTip = tip;
            AutomationProperties.SetHelpText(BackupWarningBtn, tip);
            BackupWarningBtn.Visibility = Visibility.Visible;
        }

        private void BackupWarning_Click(object sender, RoutedEventArgs e)
        {
            if (!NoteStore.IsOpen || !SaveCurrentNote(refreshList: false)) return;
            var dlg = new BackupDialog(NoteStore.ActiveDbFile) { Owner = this };
            dlg.ShowDialog();
            if (dlg.RestoredDatabase != null)
                FlashStatus(string.Format(Loc("Str_Bk_Restored"), dlg.RestoredDatabase));
            UpdateBackupHealth();
        }
    }
}
