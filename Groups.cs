using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes
{
    // Custom order + note groups (#4).
    //
    // ORDER: one global sort_order sequence over all notes ("custom" sort mode, third
    // sort button). A group's internal order is that sequence filtered, so moving notes
    // between groups never renumbers per-group. Dragging a note while sorted by time or
    // alphabet seeds sort_order from the on-screen order and switches to custom.
    //
    // GROUPS: named sections in the sidebar, backed by the (previously unused)
    // notes.notebook column + the groups table (order, collapsed). The sidebar renders a
    // COMPOSITE list - the group sections first (pinned above the loose notes so they stay
    // reachable, issue #8), each a GroupHeader row followed by its notes (hidden while
    // collapsed), then the ungrouped notes underneath. Headers are not selectable: click
    // toggles collapse, right-click renames/deletes, dropping a note on one files it there.
    // Search results stay flat (relevance order, no headers).
    //
    // The same left-drag serves reorder AND the existing shell drag-out: the DataObject
    // carries the temp .knote (external targets) plus the note id (this list). Dropping
    // inside the list reorders; leaving the window still lands a .knote in Teams/Explorer.
    public partial class MainWindow
    {
        internal const string NoteIdFormat = "KillerNotes.NoteId";

        private List<(string Name, bool Collapsed, string Color)> _groups = [];

        /// <summary>Set by HandleNoteDrop so Sharing.cs skips the "drag ready" flash
        /// when the drag ended as an in-list reorder rather than an external drop.</summary>
        private bool _noteReordered;

        // ---- Composite sidebar list ----

        /// <summary>Headers + notes for the sidebar (RefreshList). Flat while searching,
        /// and flat when the database has no groups at all (zero change until used).</summary>
        private System.Collections.IList BuildSidebarItems()
        {
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) return _notes;

            _groups = NoteStore.ListGroups();
            bool anyGrouped = _notes.Any(n => n.Notebook.Length > 0);
            if (_groups.Count == 0 && !anyGrouped) return _notes;

            var items = new List<object>();

            // Groups first, so the named sections stay pinned above the loose notes and never
            // scroll out of reach (issue #8); the ungrouped notes follow underneath.
            // Defined groups in stored order; names that exist only on notes (imports
            // from another database) are appended alphabetically, uncollapsed.
            var order = new List<(string Name, bool Collapsed, string Color)>(_groups);
            var known = new HashSet<string>(_groups.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
            foreach (string name in _notes.Where(n => n.Notebook.Length > 0 && !known.Contains(n.Notebook))
                                          .Select(n => n.Notebook)
                                          .Distinct(StringComparer.OrdinalIgnoreCase)
                                          .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                order.Add((name, false, ""));

            foreach (var g in order)
            {
                var members = _notes.Where(n =>
                    string.Equals(n.Notebook, g.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                // Stripe on each note matches the group color; flag the first/last so the
                // connector line caps cleanly at the top and bottom of this group.
                for (int i = 0; i < members.Count; i++)
                {
                    members[i].GroupColor = g.Color;
                    members[i].IsFirstInGroup = false;   // header's connector caps the top; notes only cap the bottom
                    members[i].IsLastInGroup = i == members.Count - 1;
                }
                items.Add(new GroupHeader { Name = g.Name, Count = members.Count, Collapsed = g.Collapsed, NameColor = g.Color });
                if (!g.Collapsed) items.AddRange(members);
            }

            foreach (var n in _notes) if (n.Notebook.Length == 0) items.Add(n);
            return items;
        }

        // ---- Header interactions (wired in the GroupHeader DataTemplate) ----

        private void GroupHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not GroupHeader h) return;
            NoteStore.SetGroupCollapsed(h.Name, !h.Collapsed);
            RefreshList();
            e.Handled = true;   // keep the ListBox from selecting the header row
        }

        // The header's ContextMenu lives outside the visual tree, so remember which
        // header was right-clicked rather than trusting DataContext propagation.
        private GroupHeader? _ctxGroup;

        private void GroupHeader_RightDown(object sender, MouseButtonEventArgs e)
            => _ctxGroup = (sender as FrameworkElement)?.DataContext as GroupHeader;

        private void RenameGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxGroup is not GroupHeader g) return;
            var dlg = new InputDialog(Loc("Str_Dlg_RenameGroupHead"), g.Name, Loc("Str_Btn_Rename")) { Owner = this };
            dlg.ShowDialog();
            string name = dlg.Value.Trim();
            if (!dlg.Confirmed || name.Length == 0 || name == g.Name) return;
            if (!string.Equals(name, g.Name, StringComparison.OrdinalIgnoreCase) &&
                NoteStore.ListGroups().Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                FlashStatus(string.Format(Loc("Str_Grp_Exists"), name));
                return;
            }
            NoteStore.RenameGroup(g.Name, name);
            RefreshList();
        }

        private void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxGroup is not GroupHeader g) return;
            var confirm = new ConfirmDialog(
                string.Format(Loc("Str_Dlg_DeleteGroupHead"), g.Name),
                Loc("Str_Dlg_DeleteGroupBody"),
                Loc("Str_Btn_Delete")) { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;
            NoteStore.DeleteGroup(g.Name);
            RefreshList();
        }

        // ---- Group name color (mirrors the per-note title color, Notes.cs) ----

        private void GroupColorPick_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxGroup is not GroupHeader g) return;
            var initial = g.NameBrush is SolidColorBrush sb ? sb.Color
                : (TryFindResource("TextBrush") as SolidColorBrush)?.Color ?? Colors.White;
            var dlg = new ColorPickerDialog(this, initial);
            if (dlg.ShowDialog() != true) return;
            var c = dlg.SelectedColor;
            NoteStore.SetGroupColor(g.Name, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
            RefreshList();
        }

        private void GroupColorReset_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxGroup is GroupHeader g) { NoteStore.SetGroupColor(g.Name, ""); RefreshList(); }
        }

        // ---- Right-click > Group submenu (built from NotesContextMenu_Opened, like Tags) ----

        private void BuildGroupMenu(List<Note> selected)
        {
            GroupMenu.Items.Clear();
            GroupMenu.IsEnabled = selected.Count > 0;
            if (selected.Count == 0) return;

            foreach (var g in NoteStore.ListGroups())
            {
                bool all = selected.All(n =>
                    string.Equals(n.Notebook, g.Name, StringComparison.OrdinalIgnoreCase));
                var check = new TextBlock { Text = all ? "✓" : "", VerticalAlignment = VerticalAlignment.Center };
                check.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
                var item = new MenuItem
                {
                    Header = BuildMenuRow(check, null, g.Name, null),   // Tags.cs (shared row layout)
                    Padding = new Thickness(6, 5, 14, 5),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                string name = g.Name;
                bool allIn = all;
                item.Click += (_, _) => AssignGroup(SelectedOrSame(selected), allIn ? "" : name);
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
            foreach (var n in notes)
            {
                if (string.Equals(n.Notebook, group, StringComparison.OrdinalIgnoreCase)) continue;
                NoteStore.SetNoteGroup(n.Id, group);
                n.Notebook = group;
            }
            RefreshList();
            FlashStatus(group.Length == 0
                ? Loc("Str_St_RemovedFromGroup")
                : string.Format(Loc("Str_St_MovedToGroup"), group));
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

        // ---- Drag-and-drop reorder / move-to-group ----
        // Called first from the NotesList DragOver/Drop handlers (ImportExport.cs); a drag
        // that is not our own note falls through to the file-import path unchanged.

        /// <summary>Our own note dragged over the list: show the insertion line. Returns
        /// true when the event was consumed. Disabled while searching (a filtered list
        /// has no meaningful order to drop into).</summary>
        private bool HandleNoteDragOver(DragEventArgs e)
        {
            if (!_noteDragOut || !e.Data.GetDataPresent(NoteIdFormat)) return false;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                e.Effects = DragDropEffects.None;
                ClearInsertionLine();
            }
            else
            {
                // Copy, not Move: DoDragDrop allows Copy only, so external targets
                // (Explorer) keep today's copy semantics; the drop handler below is what
                // makes an in-list "copy" act as the reorder.
                e.Effects = DragDropEffects.Copy;
                ShowInsertionLine(e);
            }
            e.Handled = true;
            return true;
        }

        private bool HandleNoteDrop(DragEventArgs e)
        {
            if (!_noteDragOut || !e.Data.GetDataPresent(NoteIdFormat)) return false;
            ClearInsertionLine();
            e.Handled = true;
            _noteReordered = true;   // Sharing.cs: no "drag ready" flash for an in-list drop
            if (string.IsNullOrWhiteSpace(SearchBox.Text) &&
                e.Data.GetData(NoteIdFormat) is long id)
                ApplyReorderDrop(id, HitSlot(e));
            return true;
        }

        /// <summary>Composite-list slot the mouse is pointing at: index of the item the
        /// note would be inserted BEFORE (top half = before that item, bottom = after;
        /// empty space below the rows = end of list).</summary>
        private int HitSlot(DragEventArgs e)
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && d is not ListBoxItem) d = VisualTreeHelper.GetParent(d);
            if (d is not ListBoxItem item) return NotesList.Items.Count;
            int idx = NotesList.ItemContainerGenerator.IndexFromContainer(item);
            if (idx < 0) return NotesList.Items.Count;
            return e.GetPosition(item).Y < item.ActualHeight / 2 ? idx : idx + 1;
        }

        private void ApplyReorderDrop(long id, int slot)
        {
            // Resolve the slot into (target group, note to insert after). A slot right below
            // a header = start of that group; a slot after a note = after that note, in its
            // group; the very top (slot 0) = top of the first section, which with groups now
            // pinned above the loose notes (issue #8) is the first group when one exists
            // (otherwise the ungrouped list, so a group-less database keeps today's behavior).
            string group = "";
            Note? after = null;
            var items = NotesList.Items;
            if (slot > 0 && slot <= items.Count)
            {
                if (items[slot - 1] is GroupHeader h) group = h.Name;
                else if (items[slot - 1] is Note p) { after = p; group = p.Notebook; }
            }
            else if (slot == 0 && items.Count > 0 && items[0] is GroupHeader top)
            {
                group = top.Name;   // above the first header -> file into that group's top
            }
            if (after != null && after.Id == id) return;   // dropped onto its own spot

            // First drag from a time/alpha sort: keep what is on screen, then go custom.
            if (_sortField != "custom")
            {
                SeedCustomOrderIfNeeded();
                _sortField = "custom";
                UpdateSortButtons();
                FlashStatus(Loc("Str_St_CustomOrderOn"));
            }

            var all = NoteStore.List(null, "custom");
            var dragged = all.FirstOrDefault(n => n.Id == id);
            if (dragged == null) return;
            all.Remove(dragged);

            int insert;
            if (after != null)
            {
                insert = all.FindIndex(n => n.Id == after.Id) + 1;   // -1 + 1 = 0, safe
            }
            else if (group.Length == 0)
            {
                insert = 0;
            }
            else
            {
                insert = all.FindIndex(n =>
                    string.Equals(n.Notebook, group, StringComparison.OrdinalIgnoreCase));
                if (insert < 0) insert = all.Count;   // empty group - global slot is moot
            }
            all.Insert(insert, dragged);

            if (!string.Equals(dragged.Notebook, group, StringComparison.OrdinalIgnoreCase))
                NoteStore.SetNoteGroup(id, group);
            NoteStore.SetNoteOrders(all.Select((n, i) => (n.Id, i + 1)));
            RefreshList();
        }

        /// <summary>Lays sort_order down from the current on-screen arrangement the first
        /// time custom order is engaged. "Never ordered" = duplicate sort_order values
        /// (fresh columns are all 0); a database that was ever renumbered has unique
        /// values and is left alone.</summary>
        private void SeedCustomOrderIfNeeded()
        {
            var all = NoteStore.List(null, _sort);   // current sort, unfiltered
            if (all.Count == 0) return;
            bool needSeed = all.GroupBy(n => n.SortOrder).Any(g => g.Count() > 1);
            if (needSeed) NoteStore.SetNoteOrders(all.Select((n, i) => (n.Id, i + 1)));
        }

        // ---- Insertion line (a 2px accent rule on the row edge under the cursor) ----

        private InsertionAdorner? _insertAdorner;

        private void ShowInsertionLine(DragEventArgs e)
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && d is not ListBoxItem) d = VisualTreeHelper.GetParent(d);
            if (d is not ListBoxItem item) { ClearInsertionLine(); return; }
            bool top = e.GetPosition(item).Y < item.ActualHeight / 2;

            if (_insertAdorner != null &&
                ReferenceEquals(_insertAdorner.AdornedElement, item) && _insertAdorner.Top == top) return;
            ClearInsertionLine();
            var layer = AdornerLayer.GetAdornerLayer(item);
            if (layer == null) return;
            _insertAdorner = new InsertionAdorner(item, top);
            layer.Add(_insertAdorner);
        }

        private void ClearInsertionLine()
        {
            if (_insertAdorner == null) return;
            AdornerLayer.GetAdornerLayer(_insertAdorner.AdornedElement)?.Remove(_insertAdorner);
            _insertAdorner = null;
        }

        private void NotesList_DragLeave(object sender, DragEventArgs e)
        {
            // Only when the drag truly left the list - DragLeave also fires when moving
            // between child elements, where a DragOver follows immediately and repaints.
            var pos = e.GetPosition(NotesList);
            if (pos.X < 0 || pos.Y < 0 || pos.X >= NotesList.ActualWidth || pos.Y >= NotesList.ActualHeight)
                ClearInsertionLine();
        }

        private sealed class InsertionAdorner : Adorner
        {
            public bool Top { get; }

            public InsertionAdorner(UIElement adorned, bool top) : base(adorned)
            {
                Top = top;
                IsHitTestVisible = false;
            }

            protected override void OnRender(DrawingContext dc)
            {
                var el = (FrameworkElement)AdornedElement;
                var brush = Application.Current.TryFindResource("PrimaryBrush") as Brush ?? Brushes.White;
                double y = Top ? 0 : el.ActualHeight;
                dc.DrawLine(new Pen(brush, 2), new Point(2, y), new Point(el.ActualWidth - 2, y));
            }
        }
    }
}
