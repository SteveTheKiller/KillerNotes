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
        internal const string GroupPathFormat = "KillerNotes.GroupPath";   // dragged group's path (1.1.0)

        private List<(string Path, string Parent, bool Collapsed, string Color)> _groups = [];

        // Group-header drag (1.1.0): press records the candidate; a move past the threshold
        // starts the drag (TryStartGroupDrag); a plain press+release toggles collapse instead.
        private GroupHeader? _groupDragCandidate;
        private Point _groupDragStart;

        /// <summary>Set by HandleNoteDrop so Sharing.cs skips the "drag ready" flash
        /// when the drag ended as an in-list reorder rather than an external drop.</summary>
        private bool _noteReordered;

        // ---- Composite sidebar list ----

        /// <summary>Headers + notes for the sidebar (RefreshList). Flat while searching,
        /// and flat when the database has no groups at all (zero change until used).
        /// Groups nest (1.1.0): each group renders its header, then its own notes, then its
        /// child groups recursively - a collapsed group hides its notes AND its whole subtree.</summary>
        private System.Collections.IList BuildSidebarItems()
        {
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) return _notes;

            _groups = NoteStore.ListGroupTree();
            bool anyGrouped = _notes.Any(n => n.Notebook.Length > 0);
            if (_groups.Count == 0 && !anyGrouped) return _notes;

            // Children bucketed by parent path, each bucket left in stored (sort_order) order.
            var childrenOf = new Dictionary<string, List<(string Path, string Parent, bool Collapsed, string Color)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in _groups)
            {
                if (!childrenOf.TryGetValue(g.Parent, out var lst)) { lst = new(); childrenOf[g.Parent] = lst; }
                lst.Add(g);
            }
            var known = new HashSet<string>(_groups.Select(g => g.Path), StringComparer.OrdinalIgnoreCase);

            var items = new List<object>();

            // A frozen brush for a color hex, or null (uncolored -> the template draws the muted
            // theme line, staying theme-reactive).
            Brush? RailBrush(string hex)
            {
                if (string.IsNullOrEmpty(hex)) return null;
                try { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
                catch { return null; }
            }

            // Ancestor guide rails for one row, one per level above it. Built fresh per row so the
            // bottom cap can be set on just the last row of each ancestor's subtree.
            List<GroupRail> RailsFrom(List<(int Level, string Color)> ancestors) =>
                ancestors.Select(a => new GroupRail
                {
                    Level = a.Level,
                    HasColor = !string.IsNullOrEmpty(a.Color),
                    Brush = RailBrush(a.Color),
                }).ToList();

            // Rounds the rail at `level` on a row (its ancestor's subtree ends on this row).
            void CapRail(object row, int level)
            {
                var rails = (row as GroupHeader)?.Rails ?? (row as Note)?.Rails;
                var r = rails?.FirstOrDefault(x => x.Level == level);
                if (r != null) r.IsLast = true;
            }

            // Emits a group header, its direct notes (unless collapsed), then its child groups one
            // level deeper, and returns the index of the LAST row of the whole subtree. ancestors
            // are the guide-line levels above this group; a child inherits them plus this group's
            // own level, so the parent's line runs down the left of the child's subtree and is
            // capped (rounded) on that subtree's last row.
            int Emit((string Path, string Parent, bool Collapsed, string Color) g, int depth, List<(int Level, string Color)> ancestors)
            {
                var members = _notes.Where(n =>
                    string.Equals(n.Notebook, g.Path, StringComparison.OrdinalIgnoreCase)).ToList();
                items.Add(new GroupHeader
                {
                    Path = g.Path,
                    Name = NoteStore.GroupNameOf(g.Path),
                    Depth = depth,
                    Rails = RailsFrom(ancestors),
                    Count = members.Count,
                    Collapsed = g.Collapsed,
                    NameColor = g.Color,
                    Density = _density,   // compact modes trim the header spacing too (Density.cs)
                });
                if (g.Collapsed) return items.Count - 1;

                bool hasKids = childrenOf.ContainsKey(g.Path);
                for (int i = 0; i < members.Count; i++)
                {
                    members[i].GroupColor = g.Color;
                    members[i].GroupDepth = depth;
                    members[i].Rails = RailsFrom(ancestors);
                    members[i].IsFirstInGroup = false;   // the header caps the spine's top
                    // The last own note caps the spine's bottom only when nothing else follows in
                    // this group; with child subgroups below, the line runs on into them instead.
                    members[i].IsLastInGroup = i == members.Count - 1 && !hasKids;
                    items.Add(members[i]);
                }

                int lastIdx = items.Count - 1;
                if (hasKids)
                {
                    var childAncestors = new List<(int Level, string Color)>(ancestors) { (depth, g.Color) };
                    foreach (var k in childrenOf[g.Path]) lastIdx = Emit(k, depth + 1, childAncestors);
                    CapRail(items[lastIdx], depth);   // round this group's rail on its subtree's last row
                }
                return lastIdx;
            }

            // Groups first (pinned above the loose notes, issue #8): every top-level group
            // (parent = ""), each expanded into its subtree. Paths that exist only on notes
            // (imported from another database) have no group row, so they are appended as
            // top-level sections, uncollapsed, in alphabetical order.
            var rootAncestors = new List<(int Level, string Color)>();
            if (childrenOf.TryGetValue("", out var roots))
                foreach (var g in roots) Emit(g, 0, rootAncestors);

            foreach (string path in _notes.Where(n => n.Notebook.Length > 0 && !known.Contains(n.Notebook))
                                          .Select(n => n.Notebook)
                                          .Distinct(StringComparer.OrdinalIgnoreCase)
                                          .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                Emit((path, "", false, ""), 0, rootAncestors);

            foreach (var n in _notes) if (n.Notebook.Length == 0) { n.GroupDepth = 0; items.Add(n); }
            return items;
        }

        // ---- Header interactions (wired in the GroupHeader DataTemplate) ----

        // Press on a header: block ListBox selection and arm a possible drag. Collapse is
        // deferred to release so a press-drag reorders the group instead of toggling it.
        private void GroupHeader_Press(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not GroupHeader h) return;
            _groupDragCandidate = h;
            _groupDragStart = e.GetPosition(null);
            e.Handled = true;   // headers are not selectable rows
        }

        // Release on a header with no drag = a click: toggle collapse. A drag consumes the
        // release (DoDragDrop) and clears the candidate, so this no-ops after a drag.
        private void GroupHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not GroupHeader h) return;
            e.Handled = true;
            if (!ReferenceEquals(_groupDragCandidate, h)) return;   // a drag ran instead of a click
            _groupDragCandidate = null;
            NoteStore.SetGroupCollapsed(h.Path, !h.Collapsed);
            RefreshList(preserveScroll: true);   // keep the sidebar from jumping on collapse/expand
        }

        // Begins a header drag once the pointer passes the threshold; returns true when it
        // started one (the caller stops treating the move as a note drag). Called from
        // NotesList_PreviewMouseMove (Sharing.cs).
        private bool TryStartGroupDrag(MouseEventArgs e)
        {
            if (_groupDragCandidate == null || e.LeftButton != MouseButtonState.Pressed) return false;
            var p = e.GetPosition(null);
            // Nudge resistance: a group header only starts moving on a deliberate drag, not a
            // stray twitch while clicking to collapse - 2.5x the system drag threshold. Below
            // that the press stays a click and just toggles collapse. (Steve, 2026-07-22)
            const double NudgeFactor = 2.5;
            if (Math.Abs(p.X - _groupDragStart.X) < SystemParameters.MinimumHorizontalDragDistance * NudgeFactor &&
                Math.Abs(p.Y - _groupDragStart.Y) < SystemParameters.MinimumVerticalDragDistance * NudgeFactor) return false;
            string path = _groupDragCandidate.Path;
            _groupDragCandidate = null;   // consumed: the release must not toggle collapse
            try { DragDrop.DoDragDrop(NotesList, new DataObject(GroupPathFormat, path), DragDropEffects.Move); }
            catch { /* a failed drag leaves the tree untouched */ }
            finally { ClearInsertionLine(); }
            return true;
        }

        // ---- Group drag: reorder / re-nest (1.1.0) ----

        private bool HandleGroupDragOver(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(GroupPathFormat)) return false;
            e.Handled = true;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) { e.Effects = DragDropEffects.None; ClearInsertionLine(); return true; }
            e.Effects = DragDropEffects.Move;
            ShowInsertionLine(e);
            return true;
        }

        private bool HandleGroupDrop(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(GroupPathFormat)) return false;
            ClearInsertionLine();
            e.Handled = true;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text)) return true;
            if (e.Data.GetData(GroupPathFormat) is not string dragged || dragged.Length == 0) return true;

            // Resolve (new parent, before-sibling) from the row under the cursor. On a header the
            // top/bottom edge reorders it as a sibling (before/after the target), the middle nests
            // into it; on a note, nest into that note's group; empty space = move to top level.
            var d = e.OriginalSource as DependencyObject;
            while (d != null && d is not ListBoxItem) d = VisualTreeHelper.GetParent(d);
            var item = d as ListBoxItem;
            object? row = item?.DataContext;

            string newParent; string? before = null;
            if (row is GroupHeader gh && item != null)
            {
                double y = e.GetPosition(item).Y, h = Math.Max(1, item.ActualHeight);
                if (y < h * 0.30) { newParent = NoteStore.GroupParentOf(gh.Path); before = gh.Path; }
                else if (y > h * 0.70) { newParent = NoteStore.GroupParentOf(gh.Path); before = SiblingAfter(gh.Path); }
                else newParent = gh.Path;   // nest into the target group
            }
            else if (row is Note nt && nt.Notebook.Length > 0) newParent = nt.Notebook;   // nest into the note's group
            else newParent = "";   // top level

            // Capture the pre-move position so Ctrl+Z can put the branch back exactly:
            // its original parent and the sibling it originally sat in front of. The leaf
            // name is unchanged by a move, so the post-move path is (newParent / leaf).
            string leaf = NoteStore.GroupNameOf(dragged);
            string origParent = NoteStore.GroupParentOf(dragged);
            string? origBefore = SiblingAfter(dragged);
            if (NoteStore.MoveGroup(dragged, newParent, before))
            {
                string newPath = NoteStore.GroupPath(newParent, leaf);
                PushUndo(() =>
                {
                    NoteStore.MoveGroup(newPath, origParent, origBefore);
                    RefreshList(preserveScroll: true);
                });
                RefreshList(preserveScroll: true);
                FlashStatus(Loc("Str_St_GroupMoved"));
            }
            return true;
        }

        // The sibling right after `path` among its parent's children (null = it is last).
        private string? SiblingAfter(string path)
        {
            string parent = NoteStore.GroupParentOf(path);
            var sibs = NoteStore.ListGroupTree()
                .Where(x => string.Equals(x.Parent, parent, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Path).ToList();
            int i = sibs.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < sibs.Count ? sibs[i + 1] : null;
        }

        // The header's ContextMenu lives outside the visual tree, so remember which
        // header was right-clicked rather than trusting DataContext propagation.
        private GroupHeader? _ctxGroup;

        private void GroupHeader_RightDown(object sender, MouseButtonEventArgs e)
        {
            var fe = sender as FrameworkElement;
            _ctxGroup = fe?.DataContext as GroupHeader;
            // A nested group's color item reads "Subgroup color..." for clarity; a root group
            // keeps "Group color...". Set on right-click, before the menu opens. (Steve, 2026-07-22)
            if (fe?.ContextMenu is ContextMenu cm && _ctxGroup is GroupHeader g)
                foreach (var it in cm.Items)
                    if (it is MenuItem mi && (mi.Tag as string) == "groupcolor")
                        mi.Header = Loc(g.IsNested ? "Str_Ctx_SubgroupColor" : "Str_Ctx_GroupColor");
        }

        private void RenameGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxGroup is not GroupHeader g) return;
            var dlg = new InputDialog(Loc("Str_Dlg_RenameGroupHead"), g.Name, Loc("Str_Btn_Rename")) { Owner = this };
            dlg.ShowDialog();
            string name = dlg.Value.Trim().Replace(NoteStore.GroupSep, "");   // strip the reserved path separator
            if (!dlg.Confirmed || name.Length == 0 || name == g.Name) return;
            // Only a sibling (same parent) using the new leaf blocks the rename; the same
            // leaf under a different parent is a distinct group path and is fine.
            string parent = NoteStore.GroupParentOf(g.Path);
            if (NoteStore.ListGroupTree().Any(x =>
                    string.Equals(x.Parent, parent, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.Path, g.Path, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(NoteStore.GroupNameOf(x.Path), name, StringComparison.OrdinalIgnoreCase)))
            {
                FlashStatus(string.Format(Loc("Str_Grp_Exists"), name));
                return;
            }
            NoteStore.RenameGroup(g.Path, name);
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
            NoteStore.DeleteGroup(g.Path);
            RefreshList();
        }

        // Creates a child group under the right-clicked header (1.1.0 subgroups). The new
        // group's path is parent + separator + leaf; its parent's row is expanded so the
        // child is visible. An existing sibling leaf just resolves to that group.
        private void NewSubgroup_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxGroup is not GroupHeader g) return;
            var dlg = new InputDialog(Loc("Str_Dlg_NewSubgroupHead"), "", Loc("Str_Btn_Create")) { Owner = this };
            dlg.ShowDialog();
            string leaf = dlg.Value.Trim().Replace(NoteStore.GroupSep, "");   // strip the reserved path separator
            if (!dlg.Confirmed || leaf.Length == 0) return;
            string parent = g.Path;
            NoteStore.AddGroup(NoteStore.GroupPath(parent, leaf), parent);
            NoteStore.SetGroupCollapsed(parent, false);   // reveal the new child
            RefreshList();
        }

        // ---- Keyboard entry points (Ctrl+Shift+G / Ctrl+Shift+K, Shortcuts.cs) ----
        // Group headers are not selectable, so a keyboard group action targets the group of
        // the selected note(s): valid only when the selection lands in exactly one group.

        private GroupHeader? ResolveKeyboardGroup()
        {
            var groups = NotesList.SelectedItems.OfType<Note>()
                .Select(n => n.Notebook).Where(g => g.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (groups.Count != 1) return null;
            var t = NoteStore.ListGroupTree()
                .FirstOrDefault(x => string.Equals(x.Path, groups[0], StringComparison.OrdinalIgnoreCase));
            if (t.Path == null) return null;
            return new GroupHeader { Path = t.Path, Name = NoteStore.GroupNameOf(t.Path), NameColor = t.Color };
        }

        private void NewSubgroupShortcut()
        {
            var g = ResolveKeyboardGroup();
            if (g == null) { FlashStatus(Loc("Str_St_PickGroupFirst")); return; }
            _ctxGroup = g;
            NewSubgroup_Click(this, new RoutedEventArgs());
        }

        private void GroupColorShortcut()
        {
            var g = ResolveKeyboardGroup();
            if (g == null) { FlashStatus(Loc("Str_St_PickGroupFirst")); return; }
            _ctxGroup = g;
            GroupColorPick_Click(this, new RoutedEventArgs());
        }

        // ---- Group name color (mirrors the per-note title color, Notes.cs) ----

        private void GroupColorPick_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxGroup is not GroupHeader g) return;
            var initial = g.NameBrush is SolidColorBrush sb ? sb.Color
                : (TryFindResource("TextBrush") as SolidColorBrush)?.Color ?? Colors.White;
            string groupName = g.Path;
            string original = g.NameColor;
            var dlg = new ColorPickerDialog(this, initial);
            // Live preview: recolor the group header + its notes' connector line as the color
            // changes in the picker (PreviewGroupColor). The RefreshList below then rebuilds
            // with the stored color (cancel) or the newly saved one (OK).
            dlg.ColorChanged += c => PreviewGroupColor(groupName, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
            if (dlg.ShowDialog() == true)
            {
                NoteStore.SetGroupColor(groupName,
                    $"#{dlg.SelectedColor.R:X2}{dlg.SelectedColor.G:X2}{dlg.SelectedColor.B:X2}");
                PushUndo(() => RestoreGroupColor(groupName, original));
            }
            RefreshList();
        }

        // Undo target: restore a group's stored name color by path.
        private void RestoreGroupColor(string path, string hex)
        {
            NoteStore.SetGroupColor(path, hex);
            RefreshList(preserveScroll: true);
        }

        /// <summary>Recolors the open group's header and its notes' spine in place while the
        /// color picker is open, so the change previews as you drag. Transient only - the
        /// caller's RefreshList restores the stored color on cancel (or the saved one on OK).
        /// GroupHeader/Note raise PropertyChanged for the color, so the rows update without a
        /// list rebuild that would reset the scroll position.</summary>
        private void PreviewGroupColor(string groupName, string hex)
        {
            if (NotesList.ItemsSource is not System.Collections.IEnumerable items) return;
            foreach (var it in items)
            {
                if (it is GroupHeader gh &&
                    string.Equals(gh.Path, groupName, StringComparison.OrdinalIgnoreCase))
                    gh.NameColor = hex;
                else if (it is Note n &&
                    string.Equals(n.Notebook, groupName, StringComparison.OrdinalIgnoreCase))
                    n.GroupColor = hex;
            }
        }

        private void GroupColorReset_Click(object sender, RoutedEventArgs e)
        {
            if (_ctxGroup is not GroupHeader g) return;
            string path = g.Path, original = g.NameColor;
            NoteStore.SetGroupColor(path, "");
            PushUndo(() => RestoreGroupColor(path, original));
            RefreshList();
        }

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
                if (items[slot - 1] is GroupHeader h) group = h.Path;
                else if (items[slot - 1] is Note p) { after = p; group = p.Notebook; }
            }
            else if (slot == 0 && items.Count > 0 && items[0] is GroupHeader top)
            {
                group = top.Path;   // above the first header -> file into that group's top
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
            // Snapshot the pre-move arrangement (all orders + the dragged note's group) so Ctrl+Z
            // puts it back. Captured after any first-time seed, so it matches the on-screen order.
            var undoOrders = all.Select(n => (n.Id, n.SortOrder)).ToList();
            string undoGroup = dragged.Notebook;
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
            PushUndo(() =>
            {
                NoteStore.SetNoteGroup(id, undoGroup);
                NoteStore.SetNoteOrders(undoOrders);
                RefreshList(preserveScroll: true);
            });
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
