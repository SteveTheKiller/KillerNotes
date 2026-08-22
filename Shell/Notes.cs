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
    // Note list, search, sort, and the load/save plumbing between NoteStore and the editor.
    public partial class MainWindow
    {
        private List<Note> _notes = [];
        private long _currentId = -1;
        private bool _dirty;
        private bool _loadingNote;        // suppresses TextChanged while a note is loaded in
        // Set when a note's stored blob would not deserialize. Blocks the save path for that
        // note: the editor is empty because the load failed, not because the note is empty,
        // and saving would write that emptiness over content still sitting on disk.
        private bool _loadFailed;
        private string _loadError = "";
        private bool _syncingSelection;   // suppresses SelectionChanged while the list re-syncs
        private string _sortField = "created";   // "created" | "title" | "custom" (#4)
        private bool _sortAsc = false;           // default: newest at the top, below the groups
        private string SortKey => _sortField == "custom" ? "custom"
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
        private readonly System.Collections.ObjectModel.ObservableCollection<object> _sidebarItems = [];

        private void RefreshList(bool preserveScroll = false)
        {
            if (!NoteStore.IsOpen) return;
            _notesScroll ??= FindDescendant<ScrollViewer>(NotesList);
            double savedOffset = preserveScroll ? (_notesScroll?.VerticalOffset ?? 0) : 0;
            TagManager.Refresh();   // cheap; keeps chip colors current across db switches
            _notes = NoteStore.List(SearchBox.Text, SortKey);
            TagManager.ApplyChips(_notes);
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
            else
                // Replacing a selected row can make WPF bring its new container into view even
                // though the collection itself was reconciled in place. Restore the exact pixel
                // offset after layout so autosave cannot move the reader at all.
                Dispatcher.BeginInvoke(new System.Action(() =>
                    _notesScroll?.ScrollToVerticalOffset(Math.Min(savedOffset, _notesScroll.ScrollableHeight))),
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

    }
}
