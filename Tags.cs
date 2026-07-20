using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes
{
    // Outlook-style color-coded tags. Definitions (name + color) live per database
    // (tags table - they travel inside shared .kndb/.knote files); assignment is the
    // notes.tags CSV, which the FTS triggers already index, so tag search and the
    // chip-click filter are instant. Chips render on the sidebar cards; the right-click
    // Tags submenu toggles them; TagsDialog manages the definitions.
    public partial class MainWindow
    {
        private Dictionary<string, string> _tagDefs = new(StringComparer.OrdinalIgnoreCase);
        private List<(string Name, string Color)> _tagOrder = [];

        /// <summary>Reloads tag definitions from the open database. Cheap (a handful of
        /// rows), called from RefreshList so database switches stay in sync for free.</summary>
        private void RefreshTagDefs()
        {
            _tagOrder = NoteStore.ListTags();
            _tagDefs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in _tagOrder) _tagDefs[t.Name] = t.Color;
        }

        /// <summary>Rebuilds every note's chip list from its CSV + the definitions.</summary>
        private void ApplyTagChips(IEnumerable<Note> notes)
        {
            foreach (var n in notes) BuildChips(n);
        }

        private void BuildChips(Note n)
        {
            n.Chips.Clear();
            foreach (string tag in NoteStore.SplitTags(n.Tags))
            {
                // A tag whose definition was deleted still shows, in neutral gray.
                string hex = _tagDefs.TryGetValue(tag, out string? c) ? c! : "#9A9A9A";
                Color color;
                try { color = (Color)ColorConverter.ConvertFromString(hex); }
                catch { color = Color.FromRgb(0x9A, 0x9A, 0x9A); }
                n.Chips.Add(new TagChip
                {
                    Name = tag,
                    Background = new SolidColorBrush(color),
                    Foreground = Luminance(color) > 0.55 ? Brushes.Black : Brushes.White,
                });
            }
        }

        private static double Luminance(Color c) =>
            (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        // ---- Chip click: filter the list by that tag (FTS-backed; Esc clears) ----

        private void TagChip_Click(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TagChip chip)
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
            TagsMenu.Items.Clear();
            TagsMenu.IsEnabled = selected.Count > 0;
            if (selected.Count == 0) return;

            int i = 0;
            foreach (var def in _tagOrder)
            {
                bool allAssigned = selected.All(n => HasTag(n, def.Name));
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

        private static bool HasTag(Note note, string tag) =>
            NoteStore.SplitTags(note.Tags).Contains(tag, StringComparer.OrdinalIgnoreCase);

        // Persists one note's tag state without touching the UI (callers batch the
        // list refresh so a 50-note toggle repaints once, not 50 times).
        private void SetTagAssigned(Note note, string tag, bool assigned)
        {
            var parts = NoteStore.SplitTags(note.Tags).ToList();
            int idx = parts.FindIndex(p => string.Equals(p, tag, StringComparison.OrdinalIgnoreCase));
            if (assigned) { if (idx < 0) parts.Add(tag); else return; }
            else { if (idx >= 0) parts.RemoveAt(idx); else return; }

            note.Tags = string.Join(", ", parts);
            NoteStore.SetNoteTags(note.Id, note.Tags);
            BuildChips(note);
        }

        /// <summary>Adds/removes the tag on the note; returns true when now assigned.</summary>
        private bool ToggleTag(Note note, string tag)
        {
            bool nowAssigned = !HasTag(note, tag);
            SetTagAssigned(note, tag, nowAssigned);
            _syncingSelection = true;
            NotesList.Items.Refresh();
            _syncingSelection = false;
            FlashStatus(string.Format(Loc(nowAssigned ? "Str_St_TagAdded" : "Str_St_TagRemoved"), tag));
            return nowAssigned;
        }

        /// <summary>Toggles the tag across a selection (#7). Mixed state assigns to the
        /// notes still missing it; a uniform state flips all of them. Returns the new
        /// shared state.</summary>
        private bool ToggleTagOnNotes(List<Note> notes, string tag)
        {
            if (notes.Count == 1) return ToggleTag(notes[0], tag);

            bool assign = notes.Any(n => !HasTag(n, tag));
            foreach (var n in notes) SetTagAssigned(n, tag, assign);
            _syncingSelection = true;
            NotesList.Items.Refresh();
            _syncingSelection = false;
            FlashStatus(string.Format(Loc(assign ? "Str_St_TagAdded" : "Str_St_TagRemoved"), tag));
            return assign;
        }

        /// <summary>Ctrl+1..9: toggle the Nth defined tag on the currently OPEN note
        /// (Shortcuts.cs). No-op when no note is open or fewer than N tags exist.</summary>
        internal void ToggleTagByIndex(int index)
        {
            if (_currentId < 0 || index < 0 || index >= _tagOrder.Count) return;
            if (_notes.FirstOrDefault(n => n.Id == _currentId) is Note note)
                ToggleTag(note, _tagOrder[index].Name);
        }

        private void OpenTagsDialog()
        {
            var dlg = new TagsDialog { Owner = this };
            // Live refresh: every add/rename/recolor/delete in the dialog re-reads the
            // notes from the database and rebuilds chips immediately, so the sidebar
            // updates as you edit rather than waiting for the dialog to close. (Rename
            // rewrote notes.tags in the DB, so the in-memory list must be re-read - a
            // stale rebuild is what left renamed tags gray.)
            dlg.TagsChanged += RefreshList;
            dlg.ShowDialog();
            RefreshList();   // final catch-all
        }
    }
}
