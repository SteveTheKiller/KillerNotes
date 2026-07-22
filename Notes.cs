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
        private string _sortField = "created";   // "created" | "title" | "custom" (#4)
        private bool _sortAsc = true;            // default: oldest at the top, moving down
        private string _sort => _sortField == "custom" ? "custom"
                                                       : $"{_sortField}-{(_sortAsc ? "asc" : "desc")}";
        private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
        // Reverts a transient status message (drag-ready, tag toggled, ...) back to the
        // note count so confirmations don't sit in the corner forever.
        private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(6) };

        private bool _notesInit;

        // Idempotent: OpenDatabase calls this again after a database switch, and the
        // timer handler must not stack.
        private void InitNotes()
        {
            if (!_notesInit)
            {
                _notesInit = true;
                _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveCurrentNote(); };
                _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); if (NoteStore.IsOpen) StatusText.Text = DefaultStatus(); };
                // Alt-tabbing away commits immediately - notes must always be current.
                Deactivated += (_, _) => SaveCurrentNote(refreshList: false);
            }
            ShowEditor(false);
            ClearActionUndo();   // ids are per-database; never replay an undo across a switch
        }

        // ---- List / search / sort ----

        // Stable ItemsSource so the list is updated in place (ReconcileSidebar) instead of
        // reassigned. Reassigning ItemsSource resets the scroll offset, which reads as the
        // sidebar jumping on every collapse/expand or group move.
        private readonly System.Collections.ObjectModel.ObservableCollection<object> _sidebarItems = new();

        private void RefreshList(bool preserveScroll = false)
        {
            if (!NoteStore.IsOpen) return;
            RefreshTagDefs();   // Tags.cs (cheap; keeps chip colors current across db switches)
            _notes = NoteStore.List(SearchBox.Text, _sort);
            ApplyTagChips(_notes);   // Tags.cs
            foreach (var n in _notes) n.Density = _density;   // sidebar row density (Density.cs)

            if (!ReferenceEquals(NotesList.ItemsSource, _sidebarItems))
                NotesList.ItemsSource = _sidebarItems;

            _syncingSelection = true;
            ReconcileSidebar(BuildSidebarItems());   // Groups.cs (headers + notes, #4); in place
            NotesList.SelectedItem = _sidebarItems.FirstOrDefault(o => o is Note n && n.Id == _currentId);
            _syncingSelection = false;

            StatusText.Text = DefaultStatus();

            // A search/sort snaps back to the top; in-place edits (collapse/expand, reorder) pass
            // preserveScroll and hold position, since the reconcile leaves the offset alone.
            if (!preserveScroll)
                Dispatcher.BeginInvoke(new System.Action(() => _notesScroll?.ScrollToVerticalOffset(0)),
                                       System.Windows.Threading.DispatcherPriority.Loaded);
            // Re-evaluate the sidebar bottom fade once this rebuild has laid out (Sidebar.cs):
            // a load/refresh that overflows should fade without waiting for a scroll.
            Dispatcher.BeginInvoke(new System.Action(ResolveAndUpdateNotesFade),
                                   System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // Brings _sidebarItems in line with `built` with the SMALLEST set of Insert/Remove/Replace
        // edits (never a Clear/Reset), matching rows by identity (RowKey) so unchanged rows keep
        // their container. Collapsing a group then removes only its descendant rows and leaves the
        // rest untouched - so the ScrollViewer's offset (and every other row) stays put. A row is
        // replaced only when its display data (RowSig) actually changed. BuildSidebarItems hands
        // back fresh objects each time, so matching by reference (the old approach) churned every
        // row and drifted the scroll.
        private void ReconcileSidebar(System.Collections.IList built)
        {
            var builtKeys = new System.Collections.Generic.HashSet<string>();
            foreach (var o in built) builtKeys.Add(RowKey(o));

            int i = 0, j = 0;
            while (j < built.Count)
            {
                if (i >= _sidebarItems.Count) { _sidebarItems.Insert(i, built[j]); i++; j++; continue; }

                string curKey = RowKey(_sidebarItems[i]);
                if (curKey == RowKey(built[j]))
                {
                    if (RowSig(_sidebarItems[i]) != RowSig(built[j])) _sidebarItems[i] = built[j];   // data changed
                    i++; j++;
                }
                else if (!builtKeys.Contains(curKey)) _sidebarItems.RemoveAt(i);   // row is gone (collapsed)
                else { _sidebarItems.Insert(i, built[j]); i++; j++; }              // new row here (expanded)
            }
            while (_sidebarItems.Count > j) _sidebarItems.RemoveAt(_sidebarItems.Count - 1);
        }

        // Row identity (survives a rebuild): note id / group path.
        private static string RowKey(object o) => o switch
        {
            Note n => "N" + n.Id,
            GroupHeader g => "G" + g.Path,
            _ => "?" + o.GetHashCode(),
        };

        // Everything the sidebar row renders from; when this is unchanged the old container is kept.
        private static string RowSig(object o) => o switch
        {
            Note n => string.Join("|", "N", n.Id, n.Title, n.Snippet, n.ModifiedDisplay, n.TitleColor,
                                  n.Tags, n.Notebook, n.GroupDepth, n.GroupColor, n.IsFirstInGroup, n.IsLastInGroup, n.Density, RailSig(n.Rails)),
            GroupHeader g => string.Join("|", "G", g.Path, g.Name, g.Depth, g.Count, g.Collapsed, g.NameColor, g.Density, RailSig(g.Rails)),
            _ => o.GetHashCode().ToString(),
        };

        private static string RailSig(System.Collections.Generic.List<GroupRail> rails)
        {
            if (rails == null || rails.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var r in rails)
                sb.Append(r.Level).Append(':')
                  .Append(r.Brush is System.Windows.Media.SolidColorBrush b ? b.Color.ToString() : "-").Append(':')
                  .Append(r.IsLast ? '1' : '0').Append(';');
            return sb.ToString();
        }

        /// <summary>The resting status line: note count, or match count while searching.</summary>
        private string DefaultStatus() => string.Format(
            Loc(string.IsNullOrWhiteSpace(SearchBox.Text) ? "Str_St_NotesCount" : "Str_St_Matches"),
            _notes.Count);

        /// <summary>Shows a transient status message that auto-clears to DefaultStatus after
        /// a few seconds (so drag/share/tag confirmations don't linger in the corner).</summary>
        private void FlashStatus(string msg)
        {
            StatusText.Text = msg;
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

        // Dedicated sort buttons: clicking the inactive one activates it (its default
        // direction); clicking the active one reverses direction. Custom order (#4) has
        // no direction - clicking it again is a no-op.
        private void SortTimeBtn_Click(object sender, RoutedEventArgs e) => SetSort("created", defaultAsc: true);
        private void SortAlphaBtn_Click(object sender, RoutedEventArgs e) => SetSort("title", defaultAsc: true);
        private void SortCustomBtn_Click(object sender, RoutedEventArgs e) => SetSort("custom", defaultAsc: true);

        // F10 (Shortcuts.cs): step to the NEXT sort mode - time -> A-Z -> custom -> time.
        // Always a mode change, so SetSort never treats it as a direction flip.
        private void CycleSortShortcut()
            => SetSort(_sortField switch { "created" => "title", "title" => "custom", _ => "created" },
                       defaultAsc: true);

        private void SetSort(string field, bool defaultAsc)
        {
            // First engagement of custom order keeps what is on screen (Groups.cs).
            if (field == "custom" && _sortField != "custom") SeedCustomOrderIfNeeded();
            if (_sortField == field) { if (field != "custom") _sortAsc = !_sortAsc; }
            else { _sortField = field; _sortAsc = defaultAsc; }
            UpdateSortButtons();
            RefreshList();
            StatusText.Text = _sortField switch
            {
                "created" => Loc(_sortAsc ? "Str_St_SortOldest" : "Str_St_SortNewest"),
                "title"   => Loc(_sortAsc ? "Str_St_SortAZ" : "Str_St_SortZA"),
                _         => Loc("Str_TT_SortCustom"),
            };
        }

        /// <summary>Accent-colors the active sort button, shows its direction arrow
        /// (up = ascending), and keeps tooltips truthful.</summary>
        private void UpdateSortButtons()
        {
            bool time = _sortField == "created", alpha = _sortField == "title", custom = _sortField == "custom";
            SortTimeBtn.SetResourceReference(ForegroundProperty, time ? "PrimaryBrush" : "TextBrush");
            SortAlphaBtn.SetResourceReference(ForegroundProperty, alpha ? "PrimaryBrush" : "TextBrush");
            SortCustomBtn.SetResourceReference(ForegroundProperty, custom ? "PrimaryBrush" : "TextBrush");
            string arrow = _sortAsc ? "↑" : "↓";
            SortTimeArrow.Text  = time ? arrow : "";
            SortAlphaArrow.Text = alpha ? arrow : "";
            SortTimeBtn.ToolTip = time
                ? string.Format(Loc("Str_TT_ClickReverse"), Loc(_sortAsc ? "Str_St_SortOldest" : "Str_St_SortNewest"))
                : Loc("Str_TT_SortTimeOff");
            SortAlphaBtn.ToolTip = alpha
                ? string.Format(Loc("Str_TT_ClickReverse"), Loc(_sortAsc ? "Str_St_SortAZ" : "Str_St_SortZA"))
                : Loc("Str_TT_SortAlphaOff");
            SortCustomBtn.ToolTip = Loc("Str_TT_SortCustom");
        }

        private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Group headers are never a selection (#4): clicks toggle collapse and set
            // Handled, but keyboard navigation can still land one here - scrub it.
            // The removal re-fires SelectionChanged, so it runs under the sync guard.
            if (e.AddedItems.Count > 0)
            {
                var headers = e.AddedItems.OfType<Models.GroupHeader>().ToList();
                if (headers.Count > 0)
                {
                    bool prev = _syncingSelection;
                    _syncingSelection = true;
                    foreach (var h in headers) NotesList.SelectedItems.Remove(h);
                    _syncingSelection = prev;
                }
            }

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
                if (item.DataContext is Models.GroupHeader) return;   // headers: own menu (#4)
                NotesList.SelectedItems.Clear();
                item.IsSelected = true;
            }
        }

        // ---- Open / save ----

        private void OpenNote(long id)
        {
            var meta = _notes.FirstOrDefault(n => n.Id == id);
            if (meta == null) return;

            SaveNotePosition();   // remember where the outgoing note was left (1.1.1)

            _loadingNote = true;
            _currentId = id;
            // Remembered for the next launch (OpenStartupNote). Demo sessions must
            // never touch real settings.
            if (NoteStore.DemoDbFile == null)
                App.SetSetting("LastNote", $"{NoteStore.ActiveDbFile}|{id}");
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
            NormalizeContentFont(Editor.Document);   // Fonts.cs (baked save-time font must not defeat the ContentFont slot)
            ApplyImageQuality(Editor.Document);      // ImageResize.cs (Fant scaling on loaded images)
            EnsureEditableTail();   // Editor.cs (rule/table as last block traps the caret)
            ApplyWordWrap(_wordWrap);   // Editor.cs (re-assert the word-wrap page width after the load)
            _loadingNote = false;
            _dirty = false;
            ApplySpellCheck(meta.SpellCheck);   // Editor.cs (per-note flag, off by default)
            ApplyTitleColor(meta);
            ShowEditor(true);
            RestoreNotePosition(id);   // reopen where the note was left, not at the top (1.1.1)
            UpdatePreviewState();   // Preview.cs (md/html detection for this note)
        }

        // ---- Remembered reading position (1.1.1, #8 follow-up) ----
        // The caret offset and scroll are saved when a note is left (note switch, alt-tab)
        // and restored on open, so a long running note reopens at the spot you were working
        // instead of the top. Position-only changes never touch the modified stamp.

        private void SaveNotePosition()
        {
            if (_currentId < 0 || !NoteStore.IsOpen) return;
            int caret = Editor.Document.ContentStart.GetOffsetToPosition(Editor.CaretPosition);
            NoteStore.SetNotePosition(_currentId, caret, Editor.VerticalOffset);
        }

        private void RestoreNotePosition(long id)
        {
            var (caret, scroll) = NoteStore.GetNotePosition(id);
            if (caret > 0 &&
                Editor.Document.ContentStart.GetPositionAtOffset(caret) is TextPointer p)
                Editor.CaretPosition = p;
            if (scroll > 0)
                // Deferred: the freshly loaded document has no layout yet, so an immediate
                // scroll would be clamped to 0. Loaded priority runs after measure/arrange.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_currentId == id) Editor.ScrollToVerticalOffset(scroll);
                }), DispatcherPriority.Loaded);
        }

        /// <summary>Colors the open note's title box (concrete brush) or restores the
        /// theme-reactive default when the note has no title color.</summary>
        private void ApplyTitleColor(Note meta)
        {
            if (meta.TitleBrush is Brush b) TitleBox.Foreground = b;
            else TitleBox.SetResourceReference(ForegroundProperty, "TextBrush");
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
            _lastActionWasOrg = false;   // a text edit is now the most recent undoable thing (ActionUndo.cs)
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

            // "file|id": the remembered id only counts inside the database it was saved in.
            Note? target = null;
            if (App.GetSetting("LastNote") is string last)
            {
                int sep = last.LastIndexOf('|');
                if (sep > 0 &&
                    string.Equals(last.Substring(0, sep), NoteStore.ActiveDbFile, StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(last.Substring(sep + 1), out long lastId))
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
            if (dlg.ShowDialog() == true)
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
                Loc("Str_Dlg_DeleteNoteBody"),
                Loc("Str_Btn_Delete")) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            var snap = NoteStore.CaptureRow(n.Id);   // for Ctrl+Z (ActionUndo.cs)
            NoteStore.Delete(n.Id);
            if (n.Id == _currentId)
            {
                _currentId = -1;
                _dirty = false;
            }
            if (snap != null)
                PushUndo(() => { NoteStore.RestoreRow(snap); RefreshList(); });
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
                Loc("Str_Dlg_DeleteNoteBody"),
                Loc("Str_Btn_Delete")) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            var snaps = notes.Select(x => NoteStore.CaptureRow(x.Id)).Where(s => s != null).Select(s => s!).ToList();
            foreach (var n in notes)
            {
                NoteStore.Delete(n.Id);
                if (n.Id == _currentId)
                {
                    _currentId = -1;
                    _dirty = false;
                }
            }
            if (snaps.Count > 0)
                PushUndo(() => { foreach (var s in snaps) NoteStore.RestoreRow(s); RefreshList(); });
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
