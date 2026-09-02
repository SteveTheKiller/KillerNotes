// ═══════════════════════════════════════════════════════════
//  VERSION HISTORY  -  earlier states of a note, and putting one back
// ═══════════════════════════════════════════════════════════
//
// The store keeps versions on its own as notes are saved (NoteStore.Snapshot: one per sitting,
// plus one before anything lossy). This file is the doorway: Alt+H or the row menu opens
// HistoryDialog for a note, and a chosen version is restored through the store and then
// reloaded into the editor by the ordinary OpenNote path, so links, the sidebar row and the
// read-only rules all come out right without a second code path.
//
// Restore is not on the Ctrl+Z stack. The text it replaced was kept as a version first, so the
// way back is the same dialog, and a single undo entry could not hold a whole note anyway.

using System.Linq;
using System.Windows;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private void History_Click(object sender, RoutedEventArgs e)
        {
            var n = NotesList.SelectedItem as Note ?? _notes.FirstOrDefault(x => x.Id == _currentId);
            if (n == null || n.IsDeleted) return;
            OpenHistory(n.Id);
        }

        /// <summary>Alt+H (Shortcuts.cs): the open note's history.</summary>
        private void HistoryShortcut()
        {
            if (_currentId >= 0 && !_currentInTrash) OpenHistory(_currentId);
        }

        private void OpenHistory(long id)
        {
            if (!NoteStore.IsOpen) return;
            // What is on screen should be the note's current state before the list is read,
            // and a chosen version must not be overwritten by a stale autosave afterwards.
            if (id == _currentId) SaveCurrentNote(refreshList: false);

            var dlg = new HistoryDialog(id) { Owner = this };
            dlg.ShowDialog();
            if (dlg.ChosenVersion < 0) return;

            if (!NoteStore.RestoreVersion(id, dlg.ChosenVersion)) return;
            // Links are derived from the text on every save; the restored text has its own.
            if (NoteStore.LoadVersion(dlg.ChosenVersion) is { } v)
                NoteStore.SetLinks(id, WikiLinks.Parse(v.Plain));

            RefreshList(preserveScroll: true);
            if (id == _currentId) ReopenCurrentNote();   // Trash.cs: back through OpenNote
            else { OpenNote(id); SelectNoteInList(id); }
            RefreshBacklinks();
            FlashStatus(string.Format(Loc("Str_St_VersionRestored"), dlg.ChosenSaved.ToString("yyyy-MM-dd HH:mm")));
        }
    }
}
