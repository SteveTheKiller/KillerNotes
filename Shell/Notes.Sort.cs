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
    // Search box, sort modes and buttons, and list selection.
    public partial class MainWindow
    {
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

        // Dedicated sort buttons: clicking the inactive one activates it (its default
        // direction); clicking the active one reverses direction. Custom order (#4) has
        // no direction - clicking it again is a no-op.
        private void SortTimeBtn_Click(object sender, RoutedEventArgs e) => SetSort("created", defaultAsc: false);
        private void SortAlphaBtn_Click(object sender, RoutedEventArgs e) => SetSort("title", defaultAsc: true);
        private void SortCustomBtn_Click(object sender, RoutedEventArgs e) => SetSort("custom", defaultAsc: true);

        // F10 (Shortcuts.cs): step to the NEXT sort mode - time -> A-Z -> custom -> time.
        // Always a mode change, so SetSort never treats it as a direction flip.
        private void CycleSortShortcut()
        {
            string next = _sortField switch { "created" => "title", "title" => "custom", _ => "created" };
            SetSort(next, defaultAsc: next != "created");
        }

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

        // The three sort buttons share a right-click menu (MainWindow.xaml SortMenu) that
        // names each sort beside its glyph, so you can pick one without decoding the strip.
        // Refresh the active marker each time it opens: accent the current mode and, for
        // the two directional sorts, show its arrow on the right (custom has no direction).
        private void SortMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu) return;
            bool directional = _sortField != "custom";   // custom order has no direction to reverse
            foreach (var obj in menu.Items)
            {
                // The reverse action and its separator only apply to a directional sort.
                if (obj is Separator sep) { sep.Visibility = directional ? Visibility.Visible : Visibility.Collapsed; continue; }
                if (obj is not MenuItem item) continue;
                if (item.Tag as string == "reverse") { item.Visibility = directional ? Visibility.Visible : Visibility.Collapsed; continue; }
                bool active = (item.Tag as string) == _sortField;
                if (active) item.SetResourceReference(ForegroundProperty, "PrimaryBrush");
                else item.ClearValue(ForegroundProperty);   // fall back to the style so hover still accents
                item.InputGestureText = active && directional ? (_sortAsc ? "↑" : "↓") : "";
            }
        }

        // A sort mode from the menu behaves exactly like clicking its button (switch to it,
        // or reverse when already active); "Reverse order" flips the active sort's direction.
        private void SortMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item || item.Tag is not string tag) return;
            if (tag == "reverse")
            {
                if (_sortField != "custom") SetSort(_sortField, defaultAsc: true);   // same field -> SetSort flips direction
            }
            else SetSort(tag, defaultAsc: true);
        }

        private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Group headers are never a selection (#4): clicks toggle collapse and set
            // Handled, but keyboard navigation can still land one here - scrub it.
            // The removal re-fires SelectionChanged, so it runs under the sync guard.
            if (e.AddedItems.Count > 0)
            {
                var headers = e.AddedItems.Cast<object>()
                    .Where(o => o is Models.GroupHeader or Models.TrashHeader).ToList();
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
        private bool _noteContextTarget;

        private void NotesList_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var d = e.OriginalSource as DependencyObject;
            while (d != null && d is not ListBoxItem)
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            _noteContextTarget = d is ListBoxItem { DataContext: Note };
            if (d is ListBoxItem item && !item.IsSelected)
            {
                if (item.DataContext is Models.GroupHeader or Models.TrashHeader) return;   // headers: own menu (#4)
                NotesList.SelectedItems.Clear();
                item.IsSelected = true;
            }
        }

        private void NotesContextMenu_Closed(object sender, RoutedEventArgs e)
            => _noteContextTarget = false;

    }
}
