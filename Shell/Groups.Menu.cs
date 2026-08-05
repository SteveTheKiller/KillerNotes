using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // The right-click Group submenu and group assignment.
    public partial class MainWindow
    {
        // ---- Right-click > Group submenu (built from NotesContextMenu_Opened, like Tags) ----

        private void BuildGroupMenu(List<Note> selected)
        {
            GroupMenu.Items.Clear();
            GroupMenu.IsEnabled = selected.Count > 0;
            if (selected.Count == 0) return;

            foreach (var g in NoteStore.ListGroupTree())
            {
                bool all = selected.All(n =>
                    string.Equals(n.Notebook, g.Path, StringComparison.OrdinalIgnoreCase));
                var check = new TextBlock { Text = all ? "✓" : "", VerticalAlignment = VerticalAlignment.Center };
                check.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
                // Full path (Parent / Child) so a nested group reads unambiguously in the flat menu.
                string label = g.Path.Replace(NoteStore.GroupSep, " / ");
                var item = new MenuItem
                {
                    Header = BuildMenuRow(check, null, label, null),   // Tags.cs (shared row layout)
                    Padding = new Thickness(6, 5, 14, 5),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                string path = g.Path;
                bool allIn = all;
                item.Click += (_, _) => AssignGroup(SelectedOrSame(selected), allIn ? "" : path);
                GroupMenu.Items.Add(item);
            }

            if (selected.Any(n => n.Notebook.Length > 0))
            {
                var remove = new MenuItem
                {
                    Header = BuildMenuRow(null, null, Loc("Str_Ctx_RemoveFromGroup"), null),
                    Padding = new Thickness(6, 5, 14, 5),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                remove.Click += (_, _) => AssignGroup(SelectedOrSame(selected), "");
                GroupMenu.Items.Add(remove);
            }

            var create = new MenuItem
            {
                Header = BuildMenuRow(null, null, Loc("Str_Ctx_NewGroup"), null),
                Padding = new Thickness(6, 6, 14, 6),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            create.Click += (_, _) => NewGroupForNotes(SelectedOrSame(selected));
            GroupMenu.Items.Add(create);
        }

        // The snapshot from menu-open is normally right, but re-read the live selection
        // in case it changed while the submenu stayed open.
        private List<Note> SelectedOrSame(List<Note> fallback)
        {
            var live = NotesList.SelectedItems.OfType<Note>().ToList();
            return live.Count > 0 ? live : fallback;
        }

        private void AssignGroup(List<Note> notes, string group)
        {
            // Snapshot the prior membership of only the notes that actually move, by id, so
            // Ctrl+Z files them back where they were (the Note instances are stale post-refresh).
            var snap = notes
                .Where(n => !string.Equals(n.Notebook, group, StringComparison.OrdinalIgnoreCase))
                .Select(n => (n.Id, n.Notebook)).ToList();
            foreach (var n in notes)
            {
                if (string.Equals(n.Notebook, group, StringComparison.OrdinalIgnoreCase)) continue;
                NoteStore.SetNoteGroup(n.Id, group);
                n.Notebook = group;
            }
            if (snap.Count > 0)
                PushUndo(() =>
                {
                    foreach (var (id, notebook) in snap) NoteStore.SetNoteGroup(id, notebook);
                    RefreshList(preserveScroll: true);
                });
            RefreshList();
            FlashStatus(group.Length == 0
                ? Loc("Str_St_RemovedFromGroup")
                : string.Format(Loc("Str_St_MovedToGroup"), group));
        }

        // Ctrl+G (Shortcuts.cs): new top-level group. Files the selected notes into it;
        // with nothing selected it just creates the empty group.
        private void NewGroupShortcut()
        {
            if (!NoteStore.IsOpen) return;
            var notes = NotesList.SelectedItems.OfType<Note>().ToList();
            if (notes.Count > 0) { NewGroupForNotes(notes); return; }
            var dlg = new InputDialog(Loc("Str_Dlg_NewGroupHead"), "", Loc("Str_Btn_Create")) { Owner = this };
            dlg.ShowDialog();
            string name = dlg.Value.Trim().Replace(NoteStore.GroupSep, "");
            if (!dlg.Confirmed || name.Length == 0) return;
            NoteStore.AddGroup(name);   // an existing name just resolves to that group
            RefreshList();
        }

        private void NewGroupForNotes(List<Note> notes)
        {
            var dlg = new InputDialog(Loc("Str_Dlg_NewGroupHead"), "", Loc("Str_Btn_Create")) { Owner = this };
            dlg.ShowDialog();
            string name = dlg.Value.Trim();
            if (!dlg.Confirmed || name.Length == 0) return;
            NoteStore.AddGroup(name);   // an existing name just gets the notes filed into it
            AssignGroup(notes, name);
        }

    }
}
