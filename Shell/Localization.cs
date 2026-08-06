using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KillerNotes.Controls;

namespace KillerNotes.Shell
{
    // Language picker + live-relocalization glue (KillerScan pattern). Static
    // {DynamicResource Str_*} XAML updates itself on a dictionary swap; everything
    // assembled in code (shortcut rows, keyboard map, status line, tooltips set from
    // code) is re-applied by RelocalizeDynamicUi.
    public partial class MainWindow
    {
        /// <summary>Look up a localized string; falls back to the key name if missing.</summary>
        private string Loc(string key) => Application.Current.TryFindResource(key) as string ?? key;

        // ---- Language menu (rail button next to the theme picker) ----

        private void LangButton_Click(object sender, RoutedEventArgs e)
        {
            if (LangMenu.IsOpen) { LangMenu.IsOpen = false; return; }
            BuildLanguageMenu(LangMenu);
            FlyoutPlacement.UsePane(ContentPane);
            FlyoutPlacement.Attach(LangMenu, LangButton);
            LangMenu.IsOpen = true;
            Anim.FadeIn(LangMenu);
        }

        // English pinned on top; the rest alphabetical by locale code (the file name).
        private static readonly (Services.Locale Loc, string Name, string Code)[] Languages =
        [
            (Services.Locale.EnUS, "English",    "en-US"),
            (Services.Locale.Bn,   "বাংলা",       "bn"),
            (Services.Locale.Cs,   "Čeština",    "cs-CZ"),
            (Services.Locale.De,   "Deutsch",    "de-DE"),
            (Services.Locale.Es,   "Español",    "es"),
            (Services.Locale.Fr,   "Français",   "fr-FR"),
            (Services.Locale.Ja,   "日本語",      "ja-JP"),
            (Services.Locale.TrTR, "Türkçe",     "tr-TR"),
            (Services.Locale.ZhCN, "中文 (简体)", "zh-CN"),
            (Services.Locale.ZhTW, "中文 (繁體)", "zh-TW"),
        ];

        private void BuildLanguageMenu(ContextMenu menu)
        {
            menu.Items.Clear();
            var current = Services.LocaleManager.Current;
            var panel = new StackPanel { Margin = new Thickness(10, 10, 10, 10) };

            foreach (var (loc, name, code) in Languages)
            {
                var grid = new Grid { MinWidth = 138 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var nameBlock = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
                var codeBlock = new TextBlock
                {
                    Text = code,
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                codeBlock.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
                Grid.SetColumn(codeBlock, 1);
                grid.Children.Add(nameBlock);
                grid.Children.Add(codeBlock);

                var item = new RadioButton
                {
                    Content = grid,
                    Tag = loc.ToString(),
                    GroupName = "LangGroup",
                    Style = (Style)FindResource("ThemeRadio"),
                    IsChecked = loc == current,
                };
                item.Checked += Lang_Click;
                panel.Children.Add(item);
            }
            menu.Items.Add(panel);
        }

        private void Lang_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton mi && mi.Tag is string tag
                && Enum.TryParse<Services.Locale>(tag, out var loc))
            {
                Services.LocaleManager.Apply(loc);
                RelocalizeDynamicUi();
                LangMenu.IsOpen = false;
            }
        }

        /// <summary>Re-applies strings to UI assembled in code, so a live language switch
        /// updates them. Static {DynamicResource Str_*} XAML updates itself.</summary>
        private void RelocalizeDynamicUi()
        {
            // Shortcut rows (list view) are built from ShortcutMap into two columns - clear both and rebuild.
            ShortcutColLeft.Children.Clear();
            ShortcutColRight.Children.Clear();
            BuildShortcutRows();                     // Shortcuts.cs

            // Keyboard map: rebuilt lazily on next open; if already built, repaint the
            // current layer so keycap captions pick up the new language.
            if (_kbBuilt)
            {
                _kbBuilt = false;                    // force a rebuild (hint text etc.)
                if (ShortcutKeyboardHost.Visibility == Visibility.Visible)
                    ApplyShortcutView(keyboard: true);
            }

            // Sidebar collapse tooltip is set from code (Sidebar.cs).
            ApplySidebarState();
            UpdateNewNoteLabel();   // re-pick the "New note" wording for the new language

            // Lock/preview tooltips and the status line refresh on their next change;
            // reset the status line to the neutral count now.
            if (NotesList.ItemsSource != null)
                StatusText.Text = string.Format(
                    Loc(string.IsNullOrWhiteSpace(SearchBox.Text) ? "Str_St_NotesCount" : "Str_St_Matches"),
                    _notes.Count);
        }
    }
}
