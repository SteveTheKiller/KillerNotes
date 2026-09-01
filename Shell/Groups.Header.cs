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
    // Group header interactions, group drag/re-nest, and the header menu.
    public partial class MainWindow
    {
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
            // that the press stays a click and just toggles collapse. (2026-07-22)
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
            // keeps "Group color...". Set on right-click, before the menu opens. (2026-07-22)
            if (fe?.ContextMenu is ContextMenu cm && _ctxGroup is GroupHeader g)
                foreach (var it in cm.Items)
                    if (it is MenuItem mi)
                    {
                        if ((mi.Tag as string) == "groupcolor")
                            mi.Header = Loc(g.IsNested ? "Str_Ctx_SubgroupColor" : "Str_Ctx_GroupColor");
                        else if ((mi.Tag as string) == "subtop")
                            mi.IsChecked = App.GetSetting("SubgroupsOnTop") != "0";
                        else if ((mi.Tag as string) == "templates")
                            mi.IsChecked = IsTemplatesGroup(g.Path);   // Templates.cs
                    }
        }

        // App-wide toggle (default on): subgroups render above their parent's notes.
        private void SubgroupsOnTop_Click(object sender, RoutedEventArgs e)
        {
            bool on = App.GetSetting("SubgroupsOnTop") != "0";
            App.SetSetting("SubgroupsOnTop", on ? "0" : "1");
            RefreshList(preserveScroll: true);
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
            RefreshList(preserveScroll: true);   // in-place edit - hold position
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
            RefreshList(preserveScroll: true);   // in-place edit - hold position
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
            RefreshList(preserveScroll: true);   // in-place edit at the header you clicked - hold position
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

    }
}
