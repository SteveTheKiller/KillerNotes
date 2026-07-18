using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Data.Sqlite;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes
{
    // Sharing between techs: .kndb (a whole database) and .knote (one note - literally a
    // one-note KillerNotes database, optionally SQLCipher-encrypted with a share password).
    // Receiving: double-click a file (App registers the HKCU associations) and this picks
    // it up after launch. Sending: right-click a note > "Share note...", or the Databases
    // dialog > "Export as .kndb...".
    public partial class MainWindow
    {
        /// <summary>Routes a double-clicked .kndb/.knote after the database has opened.</summary>
        private void HandlePendingOpenFile()
        {
            string? path = App.PendingOpenFile;
            App.PendingOpenFile = null;
            if (path == null || !NoteStore.IsOpen) return;

            if (Path.GetExtension(path).Equals(".kndb", StringComparison.OrdinalIgnoreCase))
                AddSharedDatabase(path);
            else
                ImportSharedNote(path);
        }

        // .kndb: copied into the data folder and switched to; the previous database
        // stays available in Manage databases.
        private void AddSharedDatabase(string path)
        {
            var confirm = new ConfirmDialog(
                $"Add database \"{Path.GetFileName(path)}\"?",
                "It is copied into your KillerNotes data folder and opened.\nYour current database stays available in Manage databases.",
                "Add") { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

            try
            {
                string name = Path.GetFileNameWithoutExtension(path) + ".db";
                for (int i = 2; File.Exists(Path.Combine(NoteStore.DbDir, name)); i++)
                    name = $"{Path.GetFileNameWithoutExtension(path)}-{i}.db";
                Directory.CreateDirectory(NoteStore.DbDir);
                File.Copy(path, Path.Combine(NoteStore.DbDir, name));

                SaveCurrentNote(refreshList: false);
                string prevFile = NoteStore.ActiveDbFile;
                NoteStore.Close();
                App.SetSetting("ActiveDatabase", name);
                _dbPassword = null;
                _currentId = -1;
                if (!OpenDatabase(exitOnCancel: false))
                {
                    // Unlock cancelled - fall back to the previous database.
                    App.SetSetting("ActiveDatabase", prevFile);
                    OpenDatabase(exitOnCancel: false);
                }
            }
            catch (Exception ex) { StatusText.Text = "Add database failed: " + ex.Message; }
        }

        // .knote: its note(s) are imported into the CURRENT database, prompting for the
        // share password when the file is encrypted.
        private void ImportSharedNote(string path)
        {
            var confirm = new ConfirmDialog(
                $"Import \"{Path.GetFileName(path)}\"?",
                "Its notes are added to your current database.",
                "Import") { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

            try
            {
                int count;
                if (NoteStore.IsEncryptedFile(path))
                {
                    string heading = "Unlock shared note";
                    while (true)
                    {
                        var dlg = new PasswordDialog(heading,
                            $"\"{Path.GetFileName(path)}\" is protected with a share password.",
                            "Unlock") { Owner = this };
                        dlg.ShowDialog();
                        if (!dlg.Confirmed) return;
                        try { count = NoteStore.ImportNotes(path, dlg.Password); break; }
                        catch (SqliteException) { heading = "Wrong password - try again"; }
                    }
                }
                else
                {
                    count = NoteStore.ImportNotes(path, null);
                }
                RefreshList();
                StatusText.Text = count == 1 ? "1 note imported" : $"{count} notes imported";
            }
            catch (Exception ex) { StatusText.Text = "Import failed: " + ex.Message; }
        }

        // ---- Sending: right-click a note > Share note... ----

        private static string SafeFileName(string title)
        {
            string safe = string.Join("-", title.Split(Path.GetInvalidFileNameChars(),
                StringSplitOptions.RemoveEmptyEntries)).Trim();
            return safe.Length > 0 ? safe : "note";
        }

        private void ShareNote_Click(object sender, RoutedEventArgs e)
        {
            var n = ((sender as System.Windows.Controls.MenuItem)?.DataContext as Note)
                    ?? NotesList.SelectedItem as Note;
            if (n == null) return;
            SaveCurrentNote(refreshList: false);   // share what is on screen, not a stale copy

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "KillerNotes shared note (*.knote)|*.knote",
                FileName = SafeFileName(n.Title) + ".knote",
            };
            if (dlg.ShowDialog(this) != true) return;

            var pw = new PasswordDialog(
                "Protect the shared note?",
                "Optional: the recipient must enter this password to open it.\nLeave both boxes empty to share without one.",
                "Share", showConfirm: true) { Owner = this };
            pw.ShowDialog();
            if (!pw.Confirmed) return;
            if (pw.Password != pw.PasswordConfirm)
            {
                StatusText.Text = "Passwords did not match - nothing shared";
                return;
            }

            try
            {
                NoteStore.ExportNote(n.Id, dlg.FileName,
                    string.IsNullOrEmpty(pw.Password) ? null : pw.Password);
                StatusText.Text = $"Shared to {dlg.FileName}";
            }
            catch (Exception ex) { StatusText.Text = "Share failed: " + ex.Message; }
        }

        // ---- Sending: drag a note straight out of the sidebar ----
        // Standard shell drag-out (CF_HDROP, the Outlook-attachment mechanism): the .knote
        // is exported to %TEMP% first, then offered as a file drop - Teams, Outlook, and
        // Explorer all accept it. Drag-outs are UNENCRYPTED (there is no sane way to
        // prompt for a password mid-drag); use right-click > Share note... for a protected
        // copy.

        private Point _noteDragStart;
        private Note? _noteDragCandidate;

        private void NotesList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _noteDragStart = e.GetPosition(null);
            _noteDragCandidate = NoteUnderMouse(e.OriginalSource);
        }

        private static Note? NoteUnderMouse(object source)
        {
            var d = source as DependencyObject;
            while (d != null && d is not System.Windows.Controls.ListBoxItem)
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            return (d as System.Windows.Controls.ListBoxItem)?.DataContext as Note;
        }

        private void NotesList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_noteDragCandidate == null ||
                e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;

            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _noteDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _noteDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            var note = _noteDragCandidate;
            _noteDragCandidate = null;
            try
            {
                SaveCurrentNote(refreshList: false);   // the dragged note may be the open one
                string dir = Path.Combine(Path.GetTempPath(), "KillerNotes");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, SafeFileName(note.Title) + ".knote");
                NoteStore.ExportNote(note.Id, path, null);

                var data = new DataObject(DataFormats.FileDrop, new[] { path });
                _noteDragOut = true;   // keep NotesList_Drop from re-importing our own file
                try { DragDrop.DoDragDrop(NotesList, data, DragDropEffects.Copy); }
                finally { _noteDragOut = false; }
                StatusText.Text = $"{SafeFileName(note.Title)}.knote ready wherever you dropped it";
            }
            catch (Exception ex) { StatusText.Text = "Drag share failed: " + ex.Message; }
        }
    }
}
