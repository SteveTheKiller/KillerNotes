using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // Outlook-style color-coded tags. Definitions (name + color) live per database
    // (tags table - they travel inside shared .kndb/.knote files); assignment is the
    // notes.tags CSV, which the FTS triggers already index, so tag search and the
    // chip-click filter are instant. Chips render on the sidebar cards; the right-click
    // Tags submenu toggles them; TagsDialog manages the definitions.
    public partial class MainWindow
    {
        // Definitions and chip building live in Services/TagManager.cs - this partial is the
        // menu and the toggle plumbing that needs the list, the undo stack and the status line.

        // ---- Chip click: filter the list by that tag (FTS-backed; Esc clears) ----

        private void TagChip_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TagChip chip })
            {
                SearchBox.Text = chip.Name;
                e.Handled = true;
            }
        }

        // ---- Right-click Tags submenu: one toggle row per defined tag ----

        private void NotesContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            // Acts on the whole multi-selection, not just the anchor row (#7): the
            // check reflects "every selected note has this tag", and toggling brings
            // all of them to the same state.
            var selected = NotesList.SelectedItems.Cast<Note>().ToList();
            // Inside the trash the menu is two rows, restore and delete for good (Trash.cs).
            // First, because it resets every other row's visibility before the lines below
            // decide theirs.
            bool trash = selected.Count > 0 && selected.All(n => n.IsDeleted);
            ApplyTrashMenuMode(trash);
            if (trash) return;
            // This setting belongs to the note-row menu, not the blank sidebar surface.
            // The ListBox owns one shared ContextMenu, so use the right-click hit captured
            // before the popup opened to hide this row for background clicks.
            PreviewDetectGlobal.Visibility = _noteContextTarget ? Visibility.Visible : Visibility.Collapsed;
            PreviewDetectGlobal.IsChecked = DetectMarkdownGlobally;   // Preview.cs (#14)
            UpdateConvertMenuItem();   // Markdown.cs (labels the row with the conversion direction)
            UpdatePinMenuItem(selected);   // Pin.cs (Pin or Unpin, for the selection)
            BuildTemplateMenu();           // Templates.cs (needs no selection: it makes a note)
            TagsMenu.Items.Clear();
            TagsMenu.IsEnabled = selected.Count > 0;
            if (selected.Count == 0) return;

            int i = 0;
            foreach (var def in TagManager.Order)
            {
                bool allAssigned = selected.All(n => TagManager.HasTag(n, def.Name));
                TagsMenu.Items.Add(BuildTagToggleItem(selected, def.Name, def.Color, allAssigned, ++i));
            }

            BuildGroupMenu(selected);   // Groups.cs (#4)

            // No Separator here: implicit Separator styles don't reach menu separators, so
            // WPF drew the default light line ("white line"). A tighter-padded item reads
            // fine without one. Its shortcut (F7) is right-aligned like the rest.
            var manageHead = BuildMenuRow(check: null, swatch: null, Loc("Str_Ctx_ManageTags"), "F7");
            var manage = new MenuItem { Header = manageHead, Padding = new Thickness(6, 6, 14, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            manage.Click += (_, _) => OpenTagsDialog();
            TagsMenu.Items.Add(manage);
        }

        // Check glyph + color swatch + name + right-aligned Ctrl+n hint, built by hand
        // because the themed MenuItem template renders only the Header.
        private MenuItem BuildTagToggleItem(List<Note> notes, string name, string colorHex, bool assigned, int number)
        {
            var check = new TextBlock { Text = assigned ? "✓" : "", VerticalAlignment = VerticalAlignment.Center };
            check.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");

            Brush swatchBrush;
            try { swatchBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)); }
            catch { swatchBrush = Brushes.Gray; }
            var swatch = new Border
            {
                Width = 10, Height = 10, CornerRadius = new CornerRadius(2),
                Background = swatchBrush, VerticalAlignment = VerticalAlignment.Center,
            };

            var head = BuildMenuRow(check, swatch, name, number <= 9 ? "Ctrl+" + number : null);
            var item = new MenuItem { Header = head, StaysOpenOnClick = true, Padding = new Thickness(6, 5, 14, 5), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            item.Click += (_, _) =>
            {
                bool nowAssigned = ToggleTagOnNotes(notes, name);
                check.Text = nowAssigned ? "✓" : "";
            };
            return item;
        }

        // Shared row layout for the Tags submenu so the Ctrl+n hints line up in a column:
        // [check 12] [swatch auto] [name *] [hint auto, right]. The name column takes the
        // slack (star), pushing every hint to the same right edge regardless of name length.
        private static FrameworkElement BuildMenuRow(TextBlock? check, Border? swatch, string name, string? hint)
        {
            var grid = new Grid { MinWidth = 172, HorizontalAlignment = HorizontalAlignment.Stretch };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });                  // check
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                     // swatch
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                     // hint

            if (check != null) { Grid.SetColumn(check, 0); grid.Children.Add(check); }
            if (swatch != null) { swatch.Margin = new Thickness(0, 0, 8, 0); Grid.SetColumn(swatch, 1); grid.Children.Add(swatch); }

            var nameBlock = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameBlock, 2);
            grid.Children.Add(nameBlock);

            if (hint != null)
            {
                var h = new TextBlock
                {
                    Text = hint, FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 0, 0, 0),
                };
                h.SetResourceReference(TextBlock.ForegroundProperty, "DimTextBrush");
                Grid.SetColumn(h, 3);
                grid.Children.Add(h);
            }
            return grid;
        }

        /// <summary>Adds/removes the tag on the note; returns true when now assigned.</summary>
        private bool ToggleTag(Note note, string tag)
        {
            var snap = new List<(long Id, string Tags)> { (note.Id, note.Tags) };
            bool nowAssigned = !TagManager.HasTag(note, tag);
            TagManager.SetAssigned(note, tag, nowAssigned);
            _syncingSelection = true;
            NotesList.Items.Refresh();
            _syncingSelection = false;
            PushUndo(() => RestoreTags(snap));
            FlashStatus(string.Format(Loc(nowAssigned ? "Str_St_TagAdded" : "Str_St_TagRemoved"), tag));
            return nowAssigned;
        }

        // Undo target: restore each note's stored tag CSV by id (the captured Note instances
        // are stale after a refresh). RefreshList reloads the rows and rebuilds their chips.
        private void RestoreTags(List<(long Id, string Tags)> snap)
        {
            foreach (var (id, tags) in snap) NoteStore.SetNoteTags(id, tags);
            RefreshList(preserveScroll: true);
        }

        /// <summary>Toggles the tag across a selection (#7). Mixed state assigns to the
        /// notes still missing it; a uniform state flips all of them. Returns the new
        /// shared state.</summary>
        private bool ToggleTagOnNotes(List<Note> notes, string tag)
        {
            if (notes.Count == 1) return ToggleTag(notes[0], tag);

            var snap = notes.Select(n => (n.Id, n.Tags)).ToList();
            bool assign = notes.Any(n => !TagManager.HasTag(n, tag));
            foreach (var n in notes) TagManager.SetAssigned(n, tag, assign);
            _syncingSelection = true;
            NotesList.Items.Refresh();
            _syncingSelection = false;
            PushUndo(() => RestoreTags(snap));
            FlashStatus(string.Format(Loc(assign ? "Str_St_TagAdded" : "Str_St_TagRemoved"), tag));
            return assign;
        }

        /// <summary>Ctrl+1..9: toggle the Nth defined tag on the currently OPEN note
        /// (Shortcuts.cs). No-op when no note is open or fewer than N tags exist.</summary>
        internal void ToggleTagByIndex(int index)
        {
            if (_currentId < 0 || index < 0 || index >= TagManager.Order.Count) return;
            if (_notes.FirstOrDefault(n => n.Id == _currentId) is Note note)
                ToggleTag(note, TagManager.Order[index].Name);
        }

        private void OpenTagsDialog()
        {
            var dlg = new TagsDialog { Owner = this };
            // Live refresh: every add/rename/recolor/delete in the dialog re-reads the
            // notes from the database and rebuilds chips immediately, so the sidebar
            // updates as you edit rather than waiting for the dialog to close. (Rename
            // rewrote notes.tags in the DB, so the in-memory list must be re-read - a
            // stale rebuild is what left renamed tags gray.)
            dlg.TagsChanged += () => RefreshList();
            dlg.ShowDialog();
            RefreshList();   // final catch-all
        }
    }
}
