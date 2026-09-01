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
    // Note reorder / move-to-group drag-and-drop and the insertion line.
    public partial class MainWindow
    {
        // ---- Drag-and-drop reorder / move-to-group ----
        // Called first from the NotesList DragOver/Drop handlers (ImportExport.cs); a drag
        // that is not our own note falls through to the file-import path unchanged.

        /// <summary>Our own note dragged over the list: show the insertion line. Returns
        /// true when the event was consumed. Disabled while searching (a filtered list
        /// has no meaningful order to drop into).</summary>
        private bool HandleNoteDragOver(DragEventArgs e)
        {
            if (!_noteDragOut || !e.Data.GetDataPresent(NoteIdFormat)) return false;
            if (!string.IsNullOrWhiteSpace(SearchBox.Text) || IsTrashRow(RowUnder(e)))   // Trash.cs
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
            if (string.IsNullOrWhiteSpace(SearchBox.Text) && !IsTrashRow(RowUnder(e)) &&
                e.Data.GetData(NoteIdFormat) is long id)
                ApplyReorderDrop(id, ClampSlotAboveTrash(HitSlot(e)));   // Trash.cs: never file into the trash
            return true;
        }

        /// <summary>Composite-list slot the mouse is pointing at: index of the item the
        /// note would be inserted BEFORE (top half = before that item, bottom = after;
        /// empty space below the rows = end of list). A group header is one direct drop
        /// target, so any point on it resolves immediately below the header and files the
        /// note into that group.</summary>
        private int HitSlot(DragEventArgs e)
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && d is not ListBoxItem) d = VisualTreeHelper.GetParent(d);
            if (d is not ListBoxItem item) return NotesList.Items.Count;
            int idx = NotesList.ItemContainerGenerator.IndexFromContainer(item);
            if (idx < 0) return NotesList.Items.Count;
            if (item.DataContext is GroupHeader) return idx + 1;
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

            // Dragging from a time/alpha sort: keep what is on screen, then go custom. Forced,
            // because the drop below is positioned against rows the user can SEE.
            if (_sortField != "custom")
            {
                SeedCustomOrderIfNeeded(force: true);
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

        /// <summary>Lays sort_order down from the current on-screen arrangement when custom order
        /// is engaged from another sort. Both callers have already established that much, so the
        /// only question left here is whether an arrangement worth keeping is already stored.
        ///
        /// Unforced, the seed runs only when sort_order carries DUPLICATES, which means no
        /// arrangement was ever saved: the column defaults to 0 for every row predating it. That
        /// guard is what stops the sort button from wiping a saved arrangement when the user
        /// rounds back to custom through A-Z, so the button path keeps it.
        ///
        /// The DRAG path passes force. A drop is positioned against the rows on screen, so the
        /// visible order has to become the stored order first, or the note lands relative to an
        /// order nobody is looking at and the sidebar reshuffles under the cursor. The duplicate
        /// check cannot serve that path: Create hands out MAX+1, so every note made since 1.0.2
        /// already has a unique value and the check stopped firing, which quietly broke the
        /// documented "keeps what is on screen" behavior for everyone after their first drag.</summary>
        private void SeedCustomOrderIfNeeded(bool force = false)
        {
            var all = NoteStore.List(null, SortKey);   // current sort, unfiltered
            if (all.Count == 0) return;
            bool needSeed = force || all.GroupBy(n => n.SortOrder).Any(g => g.Count() > 1);
            if (needSeed) NoteStore.SetNoteOrders(all.Select((n, i) => (n.Id, i + 1)));
        }

        // ---- Insertion line (a 2px accent rule on the row edge under the cursor) ----

        private InsertionAdorner? _insertAdorner;

        private void ShowInsertionLine(DragEventArgs e)
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && d is not ListBoxItem) d = VisualTreeHelper.GetParent(d);
            if (d is not ListBoxItem item) { ClearInsertionLine(); return; }
            bool top = item.DataContext is not GroupHeader &&
                e.GetPosition(item).Y < item.ActualHeight / 2;

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
