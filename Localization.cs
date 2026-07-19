using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KillerNotes
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
            if (sender is Button b && b.ContextMenu != null)
            {
                BuildLanguageMenu(b.ContextMenu);
                b.ContextMenu.PlacementTarget = b;
                b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                b.ContextMenu.IsOpen = true;
                Anim.FadeIn(b.ContextMenu);
            }
        }

        // English pinned on top; the rest alphabetical by locale code (the file name).
        private static readonly (Services.Locale Loc, string Name, string Code)[] Languages =
        [
            (Services.Locale.EnUS, "English",    "en-US"),
            (Services.Locale.Bn,   "বাংলা",       "bn"),
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

            foreach (var (loc, name, code) in Languages)
            {
                var grid = new Grid { MinWidth = 160 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var nameBlock = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
                var codeBlock = new TextBlock
                {
                    Text = "(" + code + ")",
                    Opacity = 0.5,
                    Margin = new Thickness(22, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(codeBlock, 1);
                grid.Children.Add(nameBlock);
                grid.Children.Add(codeBlock);

                var item = new MenuItem
                {
                    Header = grid,
                    Tag = loc.ToString(),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    IsChecked = loc == current,
                };
                if (loc == current && TryFindResource("PrimaryBrush") is Brush accent)
                {
                    nameBlock.Foreground = accent;
                    nameBlock.FontWeight = FontWeights.SemiBold;
                    codeBlock.Foreground = accent;
                    codeBlock.Opacity = 0.85;
                }
                item.Click += Lang_Click;
                menu.Items.Add(item);
            }
        }

        private void Lang_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string tag
                && Enum.TryParse<Services.Locale>(tag, out var loc))
            {
                Services.LocaleManager.Apply(loc);
                RelocalizeDynamicUi();
            }
        }

        /// <summary>Re-applies strings to UI assembled in code, so a live language switch
        /// updates them. Static {DynamicResource Str_*} XAML updates itself.</summary>
        private void RelocalizeDynamicUi()
        {
            // Shortcut rows (list view) are built from ShortcutMap - rebuild them.
            ShortcutRows.Children.Clear();
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

            // Lock/preview tooltips and the status line refresh on their next change;
            // reset the status line to the neutral count now.
            if (NotesList.ItemsSource != null)
                StatusText.Text = string.Format(
                    Loc(string.IsNullOrWhiteSpace(SearchBox.Text) ? "Str_St_NotesCount" : "Str_St_Matches"),
                    _notes.Count);
        }
    }
}
