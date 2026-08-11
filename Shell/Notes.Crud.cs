using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // New note, startup note, and per-note title color.
    public partial class MainWindow
    {
        // ---- New / delete ----

        private void NewNote_Click(object sender, RoutedEventArgs e) => CreateNewNote(focusTitle: true);

        // The button/Ctrl+N path focuses the title for naming; the click-the-empty-space
        // path drops straight into the body so typing starts immediately.
        private void CreateNewNote(bool focusTitle)
        {
            if (!NoteStore.IsOpen) return;
            SaveCurrentNote(refreshList: false);
            _currentId = NoteStore.Create(Loc("Str_Untitled"));
            // Creating is a chronological action: switch to newest-first so the new row
            // has one predictable home even if the user was browsing A-Z or custom order.
            _sortField = "created";
            _sortAsc = false;
            UpdateSortButtons();
            SearchBox.Text = "";   // a filtered list would hide the new note
            // Newest-first places the note at the head of the loose-note section, after
            // any pinned group trees. Reveal it explicitly because those groups may be
            // taller than the viewport and otherwise leave the selected row off-screen.
            RefreshList(preserveScroll: true);
            OpenNote(_currentId);
            _syncingSelection = true;
            var newRow = _sidebarItems.OfType<Note>().FirstOrDefault(n => n.Id == _currentId);
            NotesList.SelectedItem = newRow;
            _syncingSelection = false;
            if (newRow != null)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_currentId == newRow.Id)
                    {
                        // Ensure virtualization has produced the row before asking the
                        // ListBox to reveal it beneath what may be a tall group section.
                        NotesList.UpdateLayout();
                        NotesList.ScrollIntoView(newRow);
                        ResolveAndUpdateNotesFade();
                    }
                }), DispatcherPriority.Loaded);
            if (focusTitle) { TitleBox.Focus(); TitleBox.SelectAll(); }
            else Editor.Focus();
        }

        /// <summary>The app always opens INTO a note - no "make a new note" screen. Reopens
        /// the last open note (per database, "LastNote" setting), falls back to the most
        /// recently modified, and only creates an empty Untitled when the database has no
        /// notes at all - launching into a phantom "Untitled" row a user never asked for
        /// (and could not delete, because deleting recreated it) was #2.
        /// Called after the db opens and after deleting the open note.</summary>
        private void OpenStartupNote()
        {
            if (!NoteStore.IsOpen || _currentId >= 0) return;

            // A filtered-out library is not an empty one: clear the search first so the
            // fallbacks below see every note (mirrors CreateNewNote).
            if (_notes.Count == 0 && !string.IsNullOrEmpty(SearchBox.Text))
                SearchBox.Text = "";   // TextChanged refreshes the list synchronously

            // Same-database round trip (Manage databases canceled, lock/unlock): reopen exactly
            // the note that was open, not whatever the LastNote setting or the most-recent
            // fallback would pick. In-memory (SecurityHost.cs), so it holds in demo sessions,
            // which never write the LastNote setting.
            Note? target = null;
            if (_resumeNoteId >= 0 &&
                string.Equals(_resumeDb, NoteStore.ActiveDbFile, StringComparison.OrdinalIgnoreCase))
                target = _notes.FirstOrDefault(n => n.Id == _resumeNoteId);
            _resumeNoteId = -1; _resumeDb = "";

            // "file|id": the remembered id only counts inside the database it was saved in.
            if (target == null && App.GetSetting("LastNote") is string last)
            {
                int sep = last.LastIndexOf('|');
                if (sep > 0 &&
                    string.Equals(last[..sep], NoteStore.ActiveDbFile, StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(last[(sep + 1)..], out long lastId))
                    target = _notes.FirstOrDefault(n => n.Id == lastId);
            }
            target ??= _notes.OrderByDescending(n => n.Modified).FirstOrDefault();

            if (target != null)
            {
                OpenNote(target.Id);
                _syncingSelection = true;
                NotesList.SelectedItem = target;
                _syncingSelection = false;
                Editor.Focus();
            }
            else
            {
                CreateNewNote(focusTitle: false);
            }
        }

        // ---- Title color (sidebar right-click menu; 1.0.1, #1) ----

        private void TitleColorPick_Click(object sender, RoutedEventArgs e)
        {
            var n = (sender as MenuItem)?.DataContext as Note ?? NotesList.SelectedItem as Note;
            if (n == null) return;
            var initial = n.TitleBrush is SolidColorBrush sb ? sb.Color
                : (TryFindResource("TextBrush") as SolidColorBrush)?.Color ?? Colors.White;
            string original = n.TitleColor;
            var dlg = new ColorPickerDialog(this, initial);
            // Live preview: recolor the note's sidebar title as the color changes in the
            // picker (TitleColor is notifying). Restore the stored color on cancel.
            dlg.ColorChanged += c => n.TitleColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            // Confirmed, not ShowDialog() == true: the close fade nulls DialogResult
            // (ColorPickerDialog.Confirmed doc).
            dlg.ShowDialog();
            if (dlg.Confirmed)
            {
                var c = dlg.SelectedColor;
                long id = n.Id;
                SetNoteTitleColor(n, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
                PushUndo(() => RestoreTitleColor(id, original));
            }
            else
            {
                n.TitleColor = original;
                if (n.Id == _currentId) ApplyTitleColor(n);
            }
        }

        private void TitleColorReset_Click(object sender, RoutedEventArgs e)
        {
            var n = (sender as MenuItem)?.DataContext as Note ?? NotesList.SelectedItem as Note;
            if (n == null) return;
            string original = n.TitleColor;
            long id = n.Id;
            SetNoteTitleColor(n, "");
            PushUndo(() => RestoreTitleColor(id, original));
        }

        private void SetNoteTitleColor(Note n, string hex)
        {
            NoteStore.SetTitleColor(n.Id, hex);
            n.TitleColor = hex;
            if (n.Id == _currentId) ApplyTitleColor(n);
            // Repaint rows in place; the title DataTrigger re-evaluates on refresh.
            _syncingSelection = true;
            NotesList.Items.Refresh();
            _syncingSelection = false;
        }

        // Undo target: restore a note's stored title color by id (the captured Note instance
        // is stale after any refresh). Repaints the row and the open editor's title box.
        private void RestoreTitleColor(long id, string hex)
        {
            NoteStore.SetTitleColor(id, hex);
            RefreshList(preserveScroll: true);
            if (id == _currentId && _notes.FirstOrDefault(x => x.Id == id) is Note m)
                ApplyTitleColor(m);
        }

    }
}
