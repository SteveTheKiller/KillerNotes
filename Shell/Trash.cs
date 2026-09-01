// ═══════════════════════════════════════════════════════════
//  TRASH  -  deleted notes wait thirty days before they are gone
// ═══════════════════════════════════════════════════════════
//
// Delete used to drop the row and lean on Ctrl+Z for regret, which only lasted the session.
// Now a delete stamps notes.deleted and the note moves under a Trash header at the bottom of
// the sidebar, dimmed, where it opens read-only and can be restored or deleted for good. The
// store purges anything older than NoteStore.TrashDays on open, so the trash never grows
// without bound and nobody has to remember to empty it.
//
// Restore is a one-column update, which is why a trashed note keeps its group, tags, order,
// sketches and recordings: there is nothing to re-insert and nothing to lose. Ctrl+Z after a
// delete is the same call, so the old undo path became the trash's own restore.
//
// Trashed notes never take part in anything else: List, search, backlinks, mentions, the
// graph, the [[ autocomplete and the vault export all filter on the column in the store, so
// no caller up here has to remember to.

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    public partial class MainWindow
    {
        private List<Note> _trashNotes = [];

        // Set while a trashed note is open in the editor. MarkDirty checks it, so nothing done
        // to the document on screen can reach the save path.
        private bool _currentInTrash;

        private const string TrashCollapsedSetting = "TrashCollapsed";

        // ---- Sidebar section (BuildSidebarItems, Groups.cs) ----

        /// <summary>Appends the Trash header and, when expanded, its notes to a sidebar build.
        /// Nothing is added while the trash is empty, so a database that never deletes anything
        /// looks exactly as it did.</summary>
        private void AppendTrashSection(List<object> items)
        {
            if (_trashNotes.Count == 0) return;
            bool collapsed = App.GetSetting(TrashCollapsedSetting) != "0";
            items.Add(new TrashHeader { Count = _trashNotes.Count, Collapsed = collapsed, Density = _density });
            if (collapsed) return;
            foreach (var n in _trashNotes)
            {
                // No group furniture: the note keeps its group in the database for restore, but
                // under this header it is just a dimmed row.
                n.GroupDepth = 0;
                n.GroupColor = "";
                n.Rails = [];
                n.IsFirstInGroup = false;
                n.IsLastInGroup = false;
                items.Add(n);
            }
        }

        /// <summary>True for a row nothing may be dropped on or filed next to: the Trash header
        /// and the notes under it (Groups.DragDrop.cs).</summary>
        private static bool IsTrashRow(object? row) => row is TrashHeader || row is Note { IsDeleted: true };

        private static object? RowUnder(DragEventArgs e)
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && d is not ListBoxItem) d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            return (d as ListBoxItem)?.DataContext;
        }

        /// <summary>A drop in the empty space below the list resolves to the end, which is now
        /// the trash. Pull it back above the header so the note lands after the last live row.</summary>
        private int ClampSlotAboveTrash(int slot)
        {
            for (int i = 0; i < NotesList.Items.Count; i++)
                if (NotesList.Items[i] is TrashHeader) return System.Math.Min(slot, i);
            return slot;
        }

        // ---- Header interactions (TrashHeader DataTemplate, MainWindow.xaml) ----

        // Not a selectable row: the press is swallowed so the ListBox never selects it.
        private void TrashHeader_Press(object sender, MouseButtonEventArgs e) => e.Handled = true;

        private void TrashHeader_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            bool collapsed = App.GetSetting(TrashCollapsedSetting) != "0";
            App.SetSetting(TrashCollapsedSetting, collapsed ? "0" : "1");
            RefreshList(preserveScroll: true);
        }

        // ---- Menu (NotesContextMenu_Opened, Tags.cs) ----

        /// <summary>Swaps the shared note menu between its ordinary rows and the two trash rows.
        /// The ListBox owns one ContextMenu for every row, so the swap is a visibility pass over
        /// its items rather than a second menu.</summary>
        private void ApplyTrashMenuMode(bool trash)
        {
            if (NotesList.ContextMenu is not ContextMenu menu) return;
            foreach (var obj in menu.Items)
            {
                if (obj is not UIElement el) continue;
                bool trashRow = ReferenceEquals(el, RestoreItem) || ReferenceEquals(el, DeleteForeverItem);
                el.Visibility = trashRow == trash ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private List<Note> SelectedTrashNotes() =>
            NotesList.SelectedItems.OfType<Note>().Where(n => n.IsDeleted).ToList();

        private void RestoreNote_Click(object sender, RoutedEventArgs e) => RestoreNotes(SelectedTrashNotes());

        private void DeleteForever_Click(object sender, RoutedEventArgs e) => DeleteForeverWithConfirm(SelectedTrashNotes());

        private void RestoreNotes(List<Note> notes)
        {
            if (notes.Count == 0) return;
            var ids = notes.Select(n => n.Id).ToList();
            foreach (long id in ids) NoteStore.Restore(id);
            PushUndo(() =>
            {
                foreach (long id in ids) NoteStore.Trash(id);
                RefreshList(preserveScroll: true);
                if (ids.Contains(_currentId)) ReopenCurrentNote();
            });
            RefreshList(preserveScroll: true);
            // The restored note may be the one on screen: reopen it so the editor leaves read-only.
            if (ids.Contains(_currentId)) ReopenCurrentNote();
            FlashStatus(notes.Count == 1
                ? Loc("Str_St_NoteRestored")
                : string.Format(Loc("Str_St_NotesRestored"), notes.Count));
        }

        private void DeleteForeverWithConfirm(List<Note> notes)
        {
            if (notes.Count == 0) return;
            var dlg = new ConfirmDialog(
                notes.Count == 1
                    ? string.Format(Loc("Str_Dlg_DeleteForeverHead"), notes[0].Title)
                    : string.Format(Loc("Str_Dlg_DeleteForeverNHead"), notes.Count),
                Loc("Str_Dlg_DeleteForeverBody"),
                Loc("Str_Btn_Delete")) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            foreach (var n in notes)
            {
                NoteStore.DeleteForever(n.Id);
                if (n.Id == _currentId) CloseCurrentNote();
            }
            RefreshList(preserveScroll: true);
            FlashStatus(notes.Count == 1
                ? Loc("Str_St_NoteGone")
                : string.Format(Loc("Str_St_NotesGone"), notes.Count));
            OpenStartupNote();   // never drop back to the empty screen
        }

        private void EmptyTrash_Click(object sender, RoutedEventArgs e)
        {
            if (_trashNotes.Count == 0) return;
            var dlg = new ConfirmDialog(
                Loc("Str_Dlg_EmptyTrashHead"),
                string.Format(Loc("Str_Dlg_EmptyTrashBody"), _trashNotes.Count),
                Loc("Str_Btn_Delete")) { Owner = this };
            dlg.ShowDialog();
            if (!dlg.Confirmed) return;

            bool openOneGone = _trashNotes.Any(n => n.Id == _currentId);
            int gone = NoteStore.EmptyTrash();
            if (openOneGone) CloseCurrentNote();
            RefreshList(preserveScroll: true);
            FlashStatus(string.Format(Loc("Str_St_TrashEmptied"), gone));
            OpenStartupNote();
        }

        // ---- Editor state ----

        /// <summary>Puts the editor in or out of the read-only state a trashed note opens in.
        /// Called by OpenNote before ApplyFormatMode, which hides the format bar over it.</summary>
        private void SetTrashReadOnly(bool on)
        {
            _currentInTrash = on;
            // A database opened read-only (SecurityHost.cs) stays read-only whatever the note.
            bool ro = on || NoteStore.IsReadOnly;
            Editor.IsReadOnly = ro;
            TitleBox.IsReadOnly = ro;
        }

        /// <summary>Drops the editor's hold on the open note after it moved to the trash or
        /// ceased to exist. The caller refreshes the list and picks the next note to show.</summary>
        private void CloseCurrentNote()
        {
            _currentId = -1;
            _dirty = false;
            SetTrashReadOnly(false);
            RebuildLineNumbers();   // collapse the gutter - nothing else schedules a pass here
        }

        /// <summary>Reloads the open note through OpenNote so a restore (or an undone restore)
        /// re-evaluates read-only. Clearing _currentId first keeps it out of the history.</summary>
        private void ReopenCurrentNote()
        {
            long id = _currentId;
            if (id < 0) return;
            _currentId = -1;
            OpenNote(id);
            SelectNoteInList(id);   // WikiLinkNav.cs
        }
    }
}
