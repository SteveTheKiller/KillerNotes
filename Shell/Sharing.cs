using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Data.Sqlite;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // Sharing between techs: .kndb (a whole database) and .knote (one note - literally a
    // one-note KillerNotes database, optionally SQLCipher-encrypted with a share password).
    // Receiving: double-click a file (App registers the HKCU associations) and this picks
    // it up after launch. Sending: right-click a note > "Share note...", or the Databases
    // dialog > "Export as .kndb...".
    public partial class MainWindow
    {
        /// <summary>Routes a double-clicked .kndb/.knote after the database has opened.
        /// Internal: also called by App for paths forwarded from a blocked second launch
        /// (single-instance pipe).</summary>
        internal void HandlePendingOpenFile()
        {
            string? path = App.PendingOpenFile;
            App.PendingOpenFile = null;
            if (path == null) return;
            if (_security.IsLocked)
            {
                // Kept for the unlock, which runs this again (LockNow.cs).
                App.PendingOpenFile = path;
                StatusText.Text = string.Format(Loc("Str_St_LockedOpenFile"), Path.GetFileName(path));
                return;
            }
            if (!NoteStore.IsOpen) return;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".kndb")
            {
                AddSharedDatabase(path);
                return;
            }
            if (ext is ".md" or ".markdown" or ".txt")
            {
                ImportTextFile(path);
                return;
            }
            // Explorer can hand over the same file twice in a moment; a .knote has no memory of
            // being imported, so the second request would make a second copy.
            try
            {
                var (key, hash) = FolderImporter.SourceOf(path);
                if (!_recentOpens.First(key, hash, DateTime.Now))
                {
                    StatusText.Text = string.Format(Loc("Str_St_AlreadyOpening"), Path.GetFileName(path));
                    return;
                }
            }
            catch (IOException) { /* unreadable here; the import below reports it */ }
            ImportSharedNote(path);
        }

        private readonly RecentOpens _recentOpens = new();

        /// <summary>A .md, .markdown or .txt from Explorer's Open with, or Ctrl+O: the folder
        /// import's rules for one file (front matter, Markdown as written, a .txt as plain text).
        /// A file already imported unchanged opens the note it became instead of a copy. Returns
        /// the note's id, or -1.</summary>
        private long ImportTextFile(string path)
        {
            if (NoteStore.IsReadOnly)
            {
                StatusText.Text = string.Format(Loc("Str_St_ReadOnly"), NoteStore.ReadOnlyOwner);
                return -1;
            }
            try
            {
                if (new FileInfo(path).Length > FolderImporter.DefaultMaxBytes)
                {
                    StatusText.Text = string.Format(Loc("Str_St_FileTooLarge"), Path.GetFileName(path));
                    return -1;
                }
                var plan = FolderImporter.PlanFile(path, NoteStore.ImportState());
                long id;
                if (plan.Notes.Count == 0)
                {
                    var (key, hash) = FolderImporter.SourceOf(path);
                    if (NoteStore.ImportedNoteFor(key, hash) is not long existing) return -1;
                    id = existing;
                    StatusText.Text = string.Format(Loc("Str_St_OpenedAgain"), Path.GetFileName(path));
                }
                else
                {
                    id = NoteStore.ImportBatch(FolderImporter.Prepare(plan, null, System.Threading.CancellationToken.None))[0];
                    StatusText.Text = Loc("Str_St_Imported1");
                }
                SearchBox.Text = "";   // a filtered list would hide it
                RefreshList();
                OpenNote(id);
                SelectNoteInList(id);
                return id;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                StatusText.Text = string.Format(Loc("Str_St_ImportFailedFile"), Path.GetFileName(path), ex.Message);
                return -1;
            }
        }

        // .kndb: copied into the data folder and switched to; the previous database
        // stays available in Manage databases.
        private void AddSharedDatabase(string path)
        {
            var confirm = new ConfirmDialog(
                string.Format(Loc("Str_Dlg_AddDbHead"), Path.GetFileName(path)),
                Loc("Str_Dlg_AddDbBody"),
                Loc("Str_Btn_Add")) { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

            try
            {
                string name = Path.GetFileNameWithoutExtension(path) + ".db";
                for (int i = 2; File.Exists(Path.Combine(NoteStore.DbDir, name)); i++)
                    name = $"{Path.GetFileNameWithoutExtension(path)}-{i}.db";
                Directory.CreateDirectory(NoteStore.DbDir);
                File.Copy(path, Path.Combine(NoteStore.DbDir, name));

                if (!SaveCurrentNote(refreshList: false)) return;
                string prevFile = NoteStore.ActiveDbFile;
                NoteStore.Close();
                App.SetSetting("ActiveDatabase", name);
                ForgetDbPassword();   // SecurityHost.cs - a different file, its password is not ours
                _currentId = -1;
                if (!OpenDatabase(exitOnCancel: false))
                {
                    // Unlock canceled - fall back to the previous database.
                    App.SetSetting("ActiveDatabase", prevFile);
                    OpenDatabase(exitOnCancel: false);
                }
            }
            catch (Exception ex) { StatusText.Text = string.Format(Loc("Str_St_AddDbFailed"), ex.Message); }
        }

        // .knote: its note(s) are imported into the CURRENT database, prompting for the
        // share password when the file is encrypted.
        private void ImportSharedNote(string path)
        {
            var confirm = new ConfirmDialog(
                string.Format(Loc("Str_Dlg_ImportNoteHead"), Path.GetFileName(path)),
                Loc("Str_Dlg_ImportNoteBody"),
                Loc("Str_Btn_Import")) { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;

            try
            {
                int count;
                if (NoteStore.IsEncryptedFile(path))
                {
                    string heading = Loc("Str_Pw_SharedHead");
                    while (true)
                    {
                        var dlg = new PasswordDialog(heading,
                            string.Format(Loc("Str_Pw_SharedBody"), Path.GetFileName(path)),
                            Loc("Str_Btn_Unlock")) { Owner = this };
                        dlg.ShowDialog();
                        if (!dlg.Confirmed) return;
                        try { count = NoteStore.ImportNotes(path, dlg.Password); break; }
                        catch (SqliteException) { heading = Loc("Str_Pw_WrongPw"); }
                    }
                }
                else
                {
                    count = NoteStore.ImportNotes(path, null);
                }
                RefreshList();
                StatusText.Text = count == 1
                    ? Loc("Str_St_NoteImported1")
                    : string.Format(Loc("Str_St_NoteImportedN"), count);
            }
            catch (Exception ex) { StatusText.Text = string.Format(Loc("Str_St_ImportFailed"), ex.Message); }
        }

        // ---- Sending: right-click a note > Share note... ----

        internal static string SafeFileName(string title)
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
            if (!SaveCurrentNote(refreshList: false)) return;

            var dlg = new SharedPicker.FileDialog(SharedPicker.FileDialogMode.Save)
            {
                Filter = "KillerNotes shared note (*.knote)|*.knote",
                FileName = SafeFileName(n.Title) + ".knote",
            };
            if (dlg.ShowDialog(this) != true) return;

            var pw = new PasswordDialog(
                Loc("Str_Pw_ShareHead"),
                Loc("Str_Pw_ShareBody"),
                Loc("Str_Btn_Share"), showConfirm: true) { Owner = this };
            pw.ShowDialog();
            if (!pw.Confirmed) return;
            if (pw.Password != pw.PasswordConfirm)
            {
                StatusText.Text = Loc("Str_St_NothingShared");
                return;
            }

            try
            {
                NoteStore.ExportNote(n.Id, dlg.FileName,
                    string.IsNullOrEmpty(pw.Password) ? null : pw.Password);
                StatusText.Text = string.Format(Loc("Str_St_SharedTo"), dlg.FileName);
            }
            catch (Exception ex) { StatusText.Text = string.Format(Loc("Str_St_ShareFailed"), ex.Message); }
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
            _groupDragCandidate = null;   // cleared each press; GroupHeader_Press re-arms it for a header
            _noteDragStart = e.GetPosition(null);
            _noteDragCandidate = NoteUnderMouse(e.OriginalSource);
        }

        private static Note? NoteUnderMouse(object source)
        {
            var d = source as DependencyObject;
            while (d != null && d is not System.Windows.Controls.ListBoxItem)
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            // A trashed note is not draggable: it has no place in the order and should not be
            // handed to Explorer as a .knote either (Trash.cs).
            return d is System.Windows.Controls.ListBoxItem { DataContext: Note { IsDeleted: false } n } ? n : null;
        }

        private void NotesList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (TryStartGroupDrag(e)) return;   // Groups.cs: dragging a header reorders / re-nests the group
            if (_noteDragCandidate == null ||
                e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;

            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _noteDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _noteDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            var note = _noteDragCandidate;
            _noteDragCandidate = null;
            try
            {
                if (!SaveCurrentNote(refreshList: false)) return;
                // The same drag serves in-list reorder (#4): the note id rides along and
                // the NotesList drop handler (Groups.cs) recognizes its own drag by it.
                // The .knote is written only if an external target asks for it (NoteDragData.cs).
                var data = new NoteDragData(note.Id, note.Title, NoteDragData.DefaultTempRoot,
                    path => NoteStore.ExportNote(note.Id, path, null));
                _noteDragOut = true;   // keep NotesList_Drop from re-importing our own file
                _noteReordered = false;
                DragDropEffects result;
                try { result = DragDrop.DoDragDrop(NotesList, data, DragDropEffects.Copy); }
                finally { _noteDragOut = false; ClearInsertionLine(); }
                // Only announce when an EXTERNAL target actually accepted the drop; a
                // canceled drag returns None, and an in-list reorder announces itself.
                if (result == DragDropEffects.Copy && !_noteReordered && data.FileWritten)
                    FlashStatus(string.Format(Loc("Str_St_DragReady"), SafeFileName(note.Title)));
            }
            catch (Exception ex) { FlashStatus(string.Format(Loc("Str_St_DragFailed"), ex.Message)); }
        }
    }
}
