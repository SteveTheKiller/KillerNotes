using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes
{
    // Note list, search, sort, and the load/save plumbing between NoteStore and the editor.
    public partial class MainWindow
    {
        private List<Note> _notes = [];
        private long _currentId = -1;
        private bool _dirty;
        private bool _loadingNote;        // suppresses TextChanged while a note is loaded in
        private bool _syncingSelection;   // suppresses SelectionChanged while the list re-syncs
        private string _sortField = "created";   // "created" | "title"
        private bool _sortAsc = true;            // default: oldest at the top, moving down
        private string _sort => $"{_sortField}-{(_sortAsc ? "asc" : "desc")}";
        private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(2) };

        private bool _notesInit;

        // Idempotent: OpenDatabase calls this again after a database switch, and the
        // timer handler must not stack.
        private void InitNotes()
        {
            if (!_notesInit)
            {
                _notesInit = true;
                _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveCurrentNote(); };
                // Alt-tabbing away commits immediately - notes must always be current.
                Deactivated += (_, _) => SaveCurrentNote(refreshList: false);
            }
            ShowEditor(false);
        }

        // ---- List / search / sort ----

        private void RefreshList()
        {
            if (!NoteStore.IsOpen) return;
            _notes = NoteStore.List(SearchBox.Text, _sort);
            _syncingSelection = true;
            NotesList.ItemsSource = _notes;
            NotesList.SelectedItem = _notes.FirstOrDefault(n => n.Id == _currentId);
            _syncingSelection = false;

            StatusText.Text = string.Format(
                Loc(string.IsNullOrWhiteSpace(SearchBox.Text) ? "Str_St_NotesCount" : "Str_St_Matches"),
                _notes.Count);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

        // Dedicated sort buttons: clicking the inactive one activates it (its default
        // direction); clicking the active one reverses direction.
        private void SortTimeBtn_Click(object sender, RoutedEventArgs e) => SetSort("created", defaultAsc: true);
        private void SortAlphaBtn_Click(object sender, RoutedEventArgs e) => SetSort("title", defaultAsc: true);

        private void SetSort(string field, bool defaultAsc)
        {
            if (_sortField == field) _sortAsc = !_sortAsc;
            else { _sortField = field; _sortAsc = defaultAsc; }
            UpdateSortButtons();
            RefreshList();
            StatusText.Text = _sortField == "created"
                ? (_sortAsc ? "By creation time - oldest first" : "By creation time - newest first")
                : (_sortAsc ? "Alphabetical - A to Z" : "Alphabetical - Z to A");
        }

        /// <summary>Accent-colors the active sort button, shows its direction arrow
        /// (up = ascending), and keeps tooltips truthful.</summary>
        private void UpdateSortButtons()
        {
            bool time = _sortField == "created";
            SortTimeBtn.SetResourceReference(ForegroundProperty, time ? "PrimaryBrush" : "TextBrush");
            SortAlphaBtn.SetResourceReference(ForegroundProperty, time ? "TextBrush" : "PrimaryBrush");
            string arrow = _sortAsc ? "↑" : "↓";
            SortTimeArrow.Text  = time ? arrow : "";
            SortAlphaArrow.Text = time ? "" : arrow;
            SortTimeBtn.ToolTip = time
                ? (_sortAsc ? "By creation time - oldest first (click to reverse)"
                            : "By creation time - newest first (click to reverse)")
                : "Sort by creation time";
            SortAlphaBtn.ToolTip = !time
                ? (_sortAsc ? "Alphabetical A-Z (click to reverse)" : "Alphabetical Z-A (click to reverse)")
                : "Sort alphabetically";
        }

        private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSelection) return;
            SaveCurrentNote(refreshList: false);
            // Extended mode: only a single selection opens a note; Ctrl/Shift multi-
            // selection (for mass delete) leaves the current note in the editor.
            if (NotesList.SelectedItems.Count == 1 && NotesList.SelectedItem is Note n) OpenNote(n.Id);
        }

        // Right-click selects the item under the cursor before the context menu opens,
        // so "Delete note" always targets the row that was clicked. If the clicked row
        // is already part of a multi-selection, the selection is kept intact so the
        // menu can act on all of it.
        private void NotesList_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && d is not ListBoxItem)
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            if (d is ListBoxItem item && !item.IsSelected)
            {
                NotesList.SelectedItems.Clear();
                item.IsSelected = true;
            }
        }

        // ---- Open / save ----

        private void OpenNote(long id)
        {
            var meta = _notes.FirstOrDefault(n => n.Id == id);
            if (meta == null) return;

            _loadingNote = true;
            _currentId = id;
            TitleBox.Text = meta.Title;

            DeselectImage();   // ImageResize.cs (handles must not outlive the document swap)
            Editor.Document.Blocks.Clear();
            var blob = NoteStore.LoadContent(id);
            if (blob != null)
            {
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                using var ms = new MemoryStream(blob);
                range.Load(ms, DataFormats.XamlPackage);
            }
            NormalizeThemeColors(Editor.Document);   // Editor.cs (default text follows the live theme)
            ApplyImageQuality(Editor.Document);      // ImageResize.cs (Fant scaling on loaded images)
            EnsureEditableTail();   // Editor.cs (rule/table as last block traps the caret)
            _loadingNote = false;
            _dirty = false;
            ShowEditor(true);
            UpdatePreviewState();   // Preview.cs (md/html detection for this note)
        }

        /// <summary>Persists the open note (title, XamlPackage blob, plain text for search).</summary>
        private void SaveCurrentNote(bool refreshList = true)
        {
            _saveTimer.Stop();
            if (_currentId < 0 || !_dirty || !NoteStore.IsOpen) return;

            var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
            using var ms = new MemoryStream();
            range.Save(ms, DataFormats.XamlPackage);
            NoteStore.Save(_currentId, TitleBox.Text, ms.ToArray(), range.Text);
            _dirty = false;

            // ALWAYS sync the in-memory row too: OpenNote reads titles from this list, so
            // a stale row resurrected the old title on the next visit and the following
            // save wrote it back over the real one (the "title never saved" bug).
            if (_notes.FirstOrDefault(n => n.Id == _currentId) is Note meta)
            {
                meta.Title = TitleBox.Text;
                meta.Modified = DateTime.Now;
                string plain = range.Text.TrimStart();
                int nl = plain.IndexOfAny(['\r', '\n']);
                if (nl >= 0) plain = plain.Substring(0, nl);
                meta.Snippet = plain.Length > 120 ? plain.Substring(0, 120) : plain;
            }

            if (refreshList)
            {
                RefreshList();
            }
            else
            {
                // Repaint the rows in place so the sidebar text stays accurate.
                _syncingSelection = true;
                NotesList.Items.Refresh();
                _syncingSelection = false;
            }
            UpdatePreviewState();   // Preview.cs (re-detect + refresh an open pane)
        }

        private void MarkDirty()
        {
            if (_loadingNote || _currentId < 0) return;
            _dirty = true;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void TitleBox_TextChanged(object sender, TextChangedEventArgs e) => MarkDirty();
        private void Editor_TextChanged(object sender, TextChangedEventArgs e) => MarkDirty();

        // ---- New / delete ----

        private void NewNote_Click(object sender, RoutedEventArgs e) => CreateNewNote(focusTitle: true);

        // The button/Ctrl+N path focuses the title for naming; the click-the-empty-space
        // path drops straight into the body so typing starts immediately.
        private void CreateNewNote(bool focusTitle)
        {
            if (!NoteStore.IsOpen) return;
            SaveCurrentNote(refreshList: false);
            _currentId = NoteStore.Create(Loc("Str_Untitled"));
            SearchBox.Text = "";   // a filtered list would hide the new note
            RefreshList();
            OpenNote(_currentId);
            _syncingSelection = true;
            NotesList.SelectedItem = _notes.FirstOrDefault(n => n.Id == _currentId);
            _syncingSelection = false;
            if (focusTitle) { TitleBox.Focus(); TitleBox.SelectAll(); }
            else Editor.Focus();
        }

        /// <summary>The app always opens INTO a note - no "make a new note" screen. Reuses
        /// the newest still-empty Untitled note (so launches don't litter blank rows),
        /// otherwise creates one. Called after the db opens and after deleting the open note.</summary>
        private void OpenStartupNote()
        {
            if (!NoteStore.IsOpen || _currentId >= 0) return;
            // Match the English default AND the active locale's, so a language switch
            // never orphans yesterday's empty startup note.
            var empty = _notes.Where(n => (n.Title == "Untitled" || n.Title == Loc("Str_Untitled")) &&
                                          string.IsNullOrWhiteSpace(n.Snippet))
                              .OrderByDescending(n => n.Modified)
                              .FirstOrDefault();   // newest empty regardless of the list's sort order
            if (empty != null)
            {
                OpenNote(empty.Id);
                _syncingSelection = true;
                NotesList.SelectedItem = empty;
                _syncingSelection = false;
                Editor.Focus();
            }
            else
            {
                CreateNewNote(focusTitle: false);
            }
        }

        // ---- Empty-state interactions (no note open) ----

        private void EmptyState_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!NoteStore.IsOpen) return;
            CreateNewNote(focusTitle: false);
        }

        private void EmptyState_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = NoteStore.IsOpen && !_noteDragOut ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void EmptyState_Drop(object sender, DragEventArgs e)
        {
            if (!NoteStore.IsOpen || _noteDragOut) return;
            // Document files become their own notes (ImportExport.cs); images and raw
            // text keep the original behavior of starting one fresh note carrying them.
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(IsDocImport))
            {
                ImportFiles(files);
                return;
            }
            CreateNewNote(focusTitle: false);
            if (!HandleEditorDrop(e))   // Editor.cs (images); text lands below
            {
                string? txt = e.Data.GetData(DataFormats.UnicodeText) as string
                           ?? e.Data.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrEmpty(txt)) { Editor.AppendText(txt); MarkDirty(); }
            }
        }

        private void DeleteNote_Click(object sender, RoutedEventArgs e)
        {
            // ContextMenu DataContext propagation is unreliable (menu lives outside the
            // visual tree), so fall back to the list selection.
            var n = (sender as MenuItem)?.DataContext as Note ?? NotesList.SelectedItem as Note;
            if (n == null) return;
            var sel = NotesList.SelectedItems.Cast<Note>().ToList();
            if (sel.Count > 1 && sel.Contains(n)) DeleteNotesWithConfirm(sel);
            else DeleteNoteWithConfirm(n);
        }

        // Shared by the context menu and the Delete key (Shortcuts.cs).
        private void DeleteNoteWithConfirm(Note n)
        {
            var dlg = new ConfirmDialog(
                string.Format(Loc("Str_Dlg_DeleteNoteHead"), n.Title),
                Loc("Str_Dlg_CannotUndo"),
                Loc("Str_Btn_Delete")) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            NoteStore.Delete(n.Id);
            if (n.Id == _currentId)
            {
                _currentId = -1;
                _dirty = false;
            }
            RefreshList();
            StatusText.Text = Loc("Str_St_NoteDeleted");
            OpenStartupNote();   // never drop back to the empty screen
        }

        // Mass delete for a Ctrl/Shift multi-selection: one confirm, one list refresh.
        private void DeleteNotesWithConfirm(List<Note> notes)
        {
            if (notes.Count == 0) return;
            if (notes.Count == 1) { DeleteNoteWithConfirm(notes[0]); return; }

            var dlg = new ConfirmDialog(
                string.Format(Loc("Str_Dlg_DeleteNotesHead"), notes.Count),
                Loc("Str_Dlg_CannotUndo"),
                Loc("Str_Btn_Delete")) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            foreach (var n in notes)
            {
                NoteStore.Delete(n.Id);
                if (n.Id == _currentId)
                {
                    _currentId = -1;
                    _dirty = false;
                }
            }
            RefreshList();
            StatusText.Text = string.Format(Loc("Str_St_NotesDeleted"), notes.Count);
            OpenStartupNote();   // never drop back to the empty screen
        }

        // ---- Editor pane visibility ----

        private void ShowEditor(bool visible)
        {
            EmptyState.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            TitleArea.Visibility = FormatBar.Visibility = Editor.Visibility =
                visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible) PopInFormatBar();   // FormatBar.cs (first show only)
            else
            {
                PreviewBtn.Visibility = Visibility.Collapsed;
                ClosePreview();   // Preview.cs
            }
        }

        // Save on close; Chrome.cs saves window placement in OnClosed.
        protected override void OnClosing(CancelEventArgs e)
        {
            SaveCurrentNote(refreshList: false);
            NoteStore.Close();
            base.OnClosing(e);
        }
    }
}
