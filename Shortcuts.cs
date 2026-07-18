using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes
{
    // App-wide hotkeys + the F1 shortcuts overlay. Family preference: single keys first,
    // but bare letters type into notes here, so F-keys carry the single-key roles and
    // Ctrl+ combos cover the conventions other apps set (Ctrl+N, Ctrl+F, Ctrl+S).
    public partial class MainWindow
    {
        // (keys, action) pairs - BuildShortcutRows renders these into the F1 overlay,
        // so this list IS the documentation. Keep it current when adding hotkeys.
        private static readonly (string Keys, string Action)[] ShortcutMap =
        [
            ("F1",            "Keyboard shortcuts (this list)"),
            ("F2",            "Rename the open note"),
            ("F3 / Ctrl+F",   "Search notes"),
            ("F4",            "Toggle markdown/HTML preview"),
            ("F6",            "Show / hide the format bar"),
            ("F8",            "Export the open note (.txt / .rtf / .html)"),
            ("F9",            "Collapse / expand the sidebar"),
            ("F12",           "About KillerNotes"),
            ("Ctrl+N",        "New note"),
            ("Ctrl+O",        "Open files as new notes"),
            ("Ctrl+S",        "Save now (autosave runs anyway)"),
            ("Ctrl+B / I / U","Bold / italic / underline"),
            ("Ctrl+Shift+S",  "Strikethrough"),
            ("Ctrl+Shift+M",  "Monospace (code)"),
            ("Ctrl+Shift+H",  "Highlight selection (yellow)"),
            ("Ctrl+Shift+R",  "Insert horizontal rule"),
            ("Ctrl+Shift+L / N", "Bulleted / numbered list"),
            ("Ctrl+V",        "Paste - text, images, tables"),
            ("Delete",        "Delete the selected note (in the list)"),
            ("Esc",           "Close overlay / clear search"),
        ];

        private void InitShortcuts()
        {
            PreviewKeyDown += Shortcuts_PreviewKeyDown;
            PreviewKeyUp += (_, _) => KbSyncLayerFromModifiers();   // KeyboardMap.cs
            BuildShortcutRows();
        }

        private void BuildShortcutRows()
        {
            foreach (var (keys, action) in ShortcutMap)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var key = new TextBlock { Text = keys, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12 };
                key.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");

                var desc = new TextBlock { Text = action, FontSize = 12, TextWrapping = TextWrapping.Wrap };
                desc.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                Grid.SetColumn(desc, 1);

                row.Children.Add(key);
                row.Children.Add(desc);
                ShortcutRows.Children.Add(row);
            }
        }

        private void Shortcuts_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            KbSyncLayerFromModifiers();   // KeyboardMap.cs (holding Ctrl/Shift previews a layer)
            bool ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            // Formatting combos first (Ctrl+Shift+letter; plain Ctrl+B/I/U and the list
            // combos Ctrl+Shift+L/N are RichTextBox built-ins and pass through).
            if (ctrl && shift && _currentId >= 0)
            {
                switch (e.Key)
                {
                    case Key.S: Strike_Click(this, new RoutedEventArgs()); e.Handled = true; return;
                    case Key.M: Mono_Click(this, new RoutedEventArgs()); e.Handled = true; return;
                    case Key.H:
                        ApplyToSelection(System.Windows.Documents.TextElement.BackgroundProperty,
                            new System.Windows.Media.SolidColorBrush(
                                System.Windows.Media.Color.FromRgb(0x7A, 0x6A, 0x00)));
                        e.Handled = true; return;
                    case Key.R: InsertRule_Click(this, new RoutedEventArgs()); e.Handled = true; return;
                }
            }

            switch (e.Key)
            {
                case Key.F1:
                    ToggleShortcutsOverlay();
                    e.Handled = true;
                    break;
                case Key.F2:
                    if (_currentId >= 0) { TitleBox.Focus(); TitleBox.SelectAll(); e.Handled = true; }
                    break;
                case Key.F3:
                case Key.F when ctrl:
                    FocusSearch();                       // Sidebar.cs (expands first if collapsed)
                    e.Handled = true;
                    break;
                case Key.F4:
                    if (PreviewBtn.Visibility == Visibility.Visible)
                    {
                        TogglePreview_Click(this, new RoutedEventArgs());   // Preview.cs
                        e.Handled = true;
                    }
                    break;
                case Key.F6:
                    ToggleFormatBar();                   // FormatBar.cs
                    e.Handled = true;
                    break;
                case Key.F8:
                    ExportOpenNote();                    // ImportExport.cs
                    e.Handled = true;
                    break;
                case Key.F9:
                    ToggleSidebar();                     // Sidebar.cs
                    e.Handled = true;
                    break;
                case Key.F12:
                    if (AboutOverlay.Visibility == Visibility.Visible) FadeOverlayOut(AboutOverlay);
                    else { HideShortcutsOverlay(); ShowAboutOverlay(); }   // About.cs
                    e.Handled = true;
                    break;
                case Key.N when ctrl:
                    NewNote_Click(this, new RoutedEventArgs());             // Notes.cs
                    e.Handled = true;
                    break;
                case Key.O when ctrl:
                    OpenFilesDialog();                   // ImportExport.cs
                    e.Handled = true;
                    break;
                case Key.Delete when NotesList.IsKeyboardFocusWithin && NotesList.SelectedItem is Note sel:
                    DeleteNoteWithConfirm(sel);          // Notes.cs
                    e.Handled = true;
                    break;
                case Key.Escape:
                    e.Handled = HandleEscape();
                    break;
            }
        }

        // ---- Overlay show/hide (mutually exclusive with About, like KillerPDF) ----

        // The "?" rail button (same as F1).
        private void ShortcutHelp_Click(object sender, RoutedEventArgs e) => ToggleShortcutsOverlay();

        private void ToggleShortcutsOverlay()
        {
            if (ShortcutOverlay.Visibility == Visibility.Visible) { HideShortcutsOverlay(); return; }
            if (AboutOverlay.Visibility == Visibility.Visible) FadeOverlayOut(AboutOverlay);
            ApplyPersistedShortcutView();   // KeyboardMap.cs (LIST or KEYBOARD, remembered)
            ShortcutOverlay.Visibility = Visibility.Visible;
            Anim.FadeIn(ShortcutOverlay);
        }

        private void HideShortcutsOverlay() => FadeOverlayOut(ShortcutOverlay);

        private void ShortcutOverlay_Click(object sender, MouseButtonEventArgs e) => HideShortcutsOverlay();
        private void ShortcutCard_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;
        private void ShortcutClose_Click(object sender, RoutedEventArgs e) => HideShortcutsOverlay();

        // Esc: close whichever overlay is up; otherwise clear an active search.
        // Returns false when nothing consumed it, so the key still reaches the editor.
        private bool HandleEscape()
        {
            if (ShortcutOverlay.Visibility == Visibility.Visible) { HideShortcutsOverlay(); return true; }
            if (AboutOverlay.Visibility == Visibility.Visible) { FadeOverlayOut(AboutOverlay); return true; }
            if (SearchBox.IsKeyboardFocusWithin || SearchBox.Text.Length > 0)
            {
                SearchBox.Text = "";
                Editor.Focus();
                return true;
            }
            return false;
        }
    }
}
