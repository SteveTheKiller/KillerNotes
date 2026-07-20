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
        // (keys, string-resource key) pairs - BuildShortcutRows renders these into the F1
        // overlay via Loc(), so this list IS the documentation. Keep it current when
        // adding hotkeys; the English text lives in Strings/en-US.xaml.
        private static readonly (string Keys, string Action)[] ShortcutMap =
        [
            ("F1",            "Str_KS_ThisList"),
            ("F2",            "Str_KS_Rename"),
            ("F3 / Ctrl+F",   "Str_KS_Search"),
            ("F4",            "Str_KS_Preview"),
            ("F5",            "Str_KS_Sidebar"),
            ("F6",            "Str_KS_FormatBar"),
            ("F7",            "Str_KS_ManageTags"),
            ("F8",            "Str_KS_Export"),
            ("F12",           "Str_KS_About"),
            ("Ctrl+N",        "Str_KS_NewNote"),
            ("Ctrl+O",        "Str_KS_OpenFiles"),
            ("Ctrl+S",        "Str_KS_Save"),
            ("Ctrl+B / I / U","Str_KS_BIU"),
            ("Ctrl+Shift+S",  "Str_KS_Strike"),
            ("Ctrl+Shift+M",  "Str_KS_Mono"),
            ("Ctrl+Shift+H",  "Str_KS_Highlight"),
            ("Ctrl+Shift+R",  "Str_KS_Rule"),
            ("Ctrl+Shift+L / N", "Str_KS_Lists"),
            ("Ctrl+1 - 9",     "Str_KS_Tags"),
            ("Ctrl+Shift+> / <", "Str_KS_FontSize"),
            ("Ctrl+Wheel / Ctrl+0", "Str_KS_Zoom"),
            ("Ctrl+V",        "Str_KS_Paste"),
            ("Delete",        "Str_KS_Delete"),
            ("Esc",           "Str_KS_Esc"),
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

                var desc = new TextBlock { Text = Loc(action), FontSize = 12, TextWrapping = TextWrapping.Wrap };
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

            // AltGr arrives as Ctrl+Alt on Windows, so on international layouts
            // AltGr+O (ó on Polish) looked like Ctrl+O and hijacked the keystroke (#5).
            // No shortcut here uses Alt: let every Ctrl+Alt combo pass through to the
            // editor untouched so dead keys and AltGr characters type normally.
            if (ctrl && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;

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
                    // Explicit rather than relying on the RichTextBox built-ins, so the
                    // combo works with focus anywhere (title box, sidebar), like the rest.
                    case Key.OemPeriod: System.Windows.Documents.EditingCommands.IncreaseFontSize.Execute(null, Editor); e.Handled = true; return;
                    case Key.OemComma:  System.Windows.Documents.EditingCommands.DecreaseFontSize.Execute(null, Editor); e.Handled = true; return;
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
                case Key.F7:
                    if (NoteStore.IsOpen) OpenTagsDialog();   // Tags.cs (needs a db, not a note)
                    e.Handled = true;
                    break;
                case Key.F8:
                    ExportOpenNote();                    // ImportExport.cs
                    e.Handled = true;
                    break;
                case Key.F5:
                    ToggleSidebar();                     // Sidebar.cs (moved from F9: F5/F6
                    e.Handled = true;                    // sit together as the two pane toggles)
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
                case Key.D0 when ctrl:
                case Key.NumPad0 when ctrl:
                    SetEditorZoom(1.0);                  // Editor.cs
                    e.Handled = true;
                    break;
                // Ctrl+1..9: toggle the Nth defined tag on the open note (Tags.cs).
                case >= Key.D1 and <= Key.D9 when ctrl && !shift:
                    ToggleTagByIndex(e.Key - Key.D1);
                    e.Handled = true;
                    break;
                case >= Key.NumPad1 and <= Key.NumPad9 when ctrl && !shift:
                    ToggleTagByIndex(e.Key - Key.NumPad1);
                    e.Handled = true;
                    break;
                case Key.Delete when NotesList.IsKeyboardFocusWithin && NotesList.SelectedItems.Count > 0:
                    DeleteNotesWithConfirm(NotesList.SelectedItems.Cast<Note>().ToList());   // Notes.cs
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
            FadeOverlayIn(ShortcutOverlay); // About.cs (also hides the preview browser - airspace)
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
