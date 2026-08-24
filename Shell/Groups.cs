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
                if (!childrenOf.TryGetValue(g.Parent, out var lst)) { lst = []; childrenOf[g.Parent] = lst; }
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
                [.. ancestors.Select(a => new GroupRail
                {
                    Level = a.Level,
                    HasColor = !string.IsNullOrEmpty(a.Color),
                    Brush = RailBrush(a.Color),
                })];

            // Rounds the rail at `level` on a row (its ancestor's subtree ends on this row).
            void CapRail(object row, int level)
            {
                var rails = (row as GroupHeader)?.Rails ?? (row as Note)?.Rails;
                var r = rails?.FirstOrDefault(x => x.Level == level);
                r?.IsLast = true;
            }

            // Subgroups on top (toggleable from the group right-click menu): child groups render
            // right under their parent's header, ABOVE the parent's own notes; off restores the
            // original notes-then-subgroups order. The parent's line stays continuous either way:
            // its rail (level = depth) runs beside the child rows and its spine beside its own
            // notes at the same x, so only which segment ends the line changes.
            bool subTop = App.GetSetting("SubgroupsOnTop") != "0";

            // Emits a group header and its subtree (order per subTop), returning the index of the
            // LAST row of the whole subtree. ancestors are the guide-line levels above this group;
            // a child inherits them plus this group's own level, so the parent's line runs down
            // the left of the child's subtree and is capped (rounded) where the subtree ends.
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

                if (subTop && hasKids)
                {
                    // Children first: the group's rail runs from the header down through them.
                    var childAncestors = new List<(int Level, string Color)>(ancestors) { (depth, g.Color) };
                    int lastChild = items.Count - 1;
                    foreach (var k in childrenOf[g.Path]) lastChild = Emit(k, depth + 1, childAncestors);

                    for (int i = 0; i < members.Count; i++)
                    {
                        members[i].GroupColor = g.Color;
                        members[i].GroupDepth = depth;
                        members[i].Rails = RailsFrom(ancestors);
                        members[i].IsFirstInGroup = false;   // the header caps the spine's top
                        members[i].IsLastInGroup = i == members.Count - 1;   // notes end the group now
                        items.Add(members[i]);
                    }
                    // No own notes below the children: the rail itself ends the line, so cap it
                    // on the subtree's last row. With notes below, the last note's spine caps.
                    if (members.Count == 0) CapRail(items[lastChild], depth);
                    return items.Count - 1;
                }

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

    }
}
