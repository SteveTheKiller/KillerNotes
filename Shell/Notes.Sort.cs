using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

        internal static (string Field, bool Ascending) ParseSortPreference(string? value) => value switch
        {
            "created-asc" => ("created", true),
            "title-asc" => ("title", true),
            "title-desc" => ("title", false),
            "custom" => ("custom", false),
            _ => ("created", false),
        };

        private void RememberSort() => App.SetSetting("SidebarSort", SortKey);

        // Alt+4 to Alt+6 (ActionShortcuts.cs): picking the inactive sort activates it in its
        // default direction; picking the active one reverses it. Custom order (#4) has no
        // direction, so picking it again is a no-op. The names predate the single sort button.
        private void SortTimeBtn_Click(object sender, RoutedEventArgs e) => SetSort("created", defaultAsc: false);
        private void SortAlphaBtn_Click(object sender, RoutedEventArgs e) => SetSort("title", defaultAsc: true);
        private void SortCustomBtn_Click(object sender, RoutedEventArgs e) => SetSort("custom", defaultAsc: true);

        /// <summary>The sidebar's one sort button opens its menu under it, the same menu a
        /// right-click gives: FlyoutPlacement's toolbar dropdown, right edges aligned. The menu is
        /// wider than a default sidebar, so on a left-hand sidebar it is nudged right where it
        /// would hang past the sidebar's far edge and off the window. On a right-hand sidebar the
        /// overhang falls over the content pane, which is fine.</summary>
        private void SortBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SortBtn.ContextMenu is not ContextMenu menu) return;
            FlyoutPlacement.AttachBelowRight(menu, SortBtn);
            var below = menu.CustomPopupPlacementCallback;
            menu.CustomPopupPlacementCallback = (popup, target, offset) =>
            {
                var placed = below(popup, target, offset);
                if (_sidebarRight) return placed;
                double scale = SortBtn.ActualWidth > 0 ? target.Width / SortBtn.ActualWidth : 1;
                double halo = new Border { Style = TryFindResource("FlyoutCard") as Style }.Margin.Left;
                double minX = -SortBtn.TranslatePoint(new Point(0, 0), SidebarPanel).X * scale - halo;
                var p = placed[0].Point;
                return [new CustomPopupPlacement(new Point(Math.Max(minX, p.X), p.Y), placed[0].PrimaryAxis)];
            };
            menu.IsOpen = true;
        }

        /// <summary>The direction a sort mode starts in when it is picked: newest first for time,
        /// A to Z for the others. The buttons, Alt+4 to Alt+6 and the sort menu all agree on it.</summary>
        internal static bool DefaultAscending(string field) => field != "created";

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
            RememberSort();
            UpdateSortButtons();
            RefreshList();
            StatusText.Text = _sortField switch
            {
                "created" => Loc(_sortAsc ? "Str_St_SortOldest" : "Str_St_SortNewest"),
                "title"   => Loc(_sortAsc ? "Str_St_SortAZ" : "Str_St_SortZA"),
                _         => Loc("Str_TT_SortCustom"),
            };
        }

        /// <summary>The sort button wears the active sort's glyph and, for the two directional
        /// sorts, its arrow (up = ascending). The tooltip names the sort and its shortcut.</summary>
        private void UpdateSortButtons()
        {
            SortGlyph.Text = SortGlyphFor(_sortField);
            SortArrow.Text = _sortField == "custom" ? "" : _sortAsc ? "\u2191" : "\u2193";
            SortArrow.Visibility = _sortField == "custom" ? Visibility.Collapsed : Visibility.Visible;
            string name = _sortField switch
            {
                "created" => Loc(_sortAsc ? "Str_St_SortOldest" : "Str_St_SortNewest"),
                "title" => Loc(_sortAsc ? "Str_St_SortAZ" : "Str_St_SortZA"),
                _ => Loc("Str_TT_SortCustom"),
            };
            SortBtn.ToolTip = name;
            AutomationProperties.SetName(SortBtn, name);
        }

        /// <summary>The MDL2 glyph each sort shows, the same one its row has in the sort menu.</summary>
        internal static string SortGlyphFor(string field) => field switch
        {
            "title" => "\uE8CB",
            "custom" => "\uE76F",
            _ => "\uE823",
        };

        // The sort menu (MainWindow.xaml SortMenu) names each sort beside its glyph. Refresh the active marker each time it opens: accent the current mode and, for
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
                if (item.Tag as string == "reverse") { item.Visibility = directional ? Visibility.Visible : Visibility.Collapsed; item.InputGestureText = "Alt+7"; continue; }
                bool active = (item.Tag as string) == _sortField;
                if (active) item.SetResourceReference(ForegroundProperty, "PrimaryBrush");
                else item.ClearValue(ForegroundProperty);   // fall back to the style so hover still accents
                item.InputGestureText = item.Tag as string switch { "created" => "Alt+4", "title" => "Alt+5", "custom" => "Alt+6", _ => "" };
            }
        }

        // A sort mode from the menu switches to it, or reverses it when already active; "Reverse order" flips the active sort's direction.
        private void SortMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item || item.Tag is not string tag) return;
            if (tag == "reverse")
            {
                if (_sortField != "custom") SetSort(_sortField, defaultAsc: true);   // same field -> SetSort flips direction
            }
            else SetSort(tag, defaultAsc: DefaultAscending(tag));
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
            if (!SaveCurrentNote(refreshList: false)) { SelectNoteInList(_currentId); return; }
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
            _managerCardNotes = null;   // the sidebar owns the card again (NoteCard.cs)
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
