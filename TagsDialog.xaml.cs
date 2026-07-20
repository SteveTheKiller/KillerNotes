using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KillerNotes.Services;

namespace KillerNotes
{
    // Manage tags: add / rename / recolor / delete the open database's tag definitions.
    // Rename and delete ripple through every note's CSV (NoteStore). The note store stays
    // OPEN (unlike Manage databases) - these are ordinary row edits on the live db.
    public partial class TagsDialog : Window
    {
        private string _newColor = "#50AEE8";   // default pick for the add row

        /// <summary>Raised after every add/rename/recolor/delete so the owner can refresh
        /// the sidebar chips LIVE, without waiting for the dialog to close.</summary>
        public event Action? TagsChanged;

        private static string Loc(string key) =>
            Application.Current.TryFindResource(key) as string ?? key;

        // Re-fills the dialog's own list AND notifies the owner (live chip refresh).
        private void Changed(string? select = null)
        {
            Refresh(select);
            TagsChanged?.Invoke();
        }

        public TagsDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => Anim.FadeIn(RootBorder);
            NewColorSwatch.Background = BrushFromHex(_newColor);
            Refresh();
        }

        private void Refresh(string? select = null)
        {
            TagList.Items.Clear();
            foreach (var (name, color) in NoteStore.ListTags())
                TagList.Items.Add(BuildRow(name, color));
            if (select != null)
                foreach (ListBoxItem item in TagList.Items)
                    if ((string)item.Tag == select) { item.IsSelected = true; break; }
        }

        // swatch + name + [recolor] [rename] [delete], all carrying the tag name.
        private ListBoxItem BuildRow(string name, string colorHex)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var swatch = new Border
            {
                Width = 14, Height = 14, CornerRadius = new CornerRadius(3),
                Background = BrushFromHex(colorHex), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 10, 0),
            };
            grid.Children.Add(swatch);

            var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(actions, 2);
            actions.Children.Add(RowButton("", Loc("Str_TT_TagRecolor"), () => RecolorTag(name)));
            actions.Children.Add(RowButton("", Loc("Str_TT_TagRename"),  () => BeginRename(grid, label, name)));
            actions.Children.Add(RowButton("", Loc("Str_TT_TagDelete"),  () => DeleteTag(name)));
            grid.Children.Add(actions);

            var row = new ListBoxItem { Content = grid, Tag = name, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            // Double-click the row to rename inline (as well as the rename button).
            row.MouseDoubleClick += (_, _) => BeginRename(grid, label, name);

            // Right-click menu: Rename / Change color / Delete (themed like the rest).
            var menu = new ContextMenu();
            var miRename = new MenuItem { Header = Loc("Str_TT_TagRename") };
            miRename.Click += (_, _) => BeginRename(grid, label, name);
            var miColor = new MenuItem { Header = Loc("Str_TT_TagRecolor") };
            miColor.Click += (_, _) => RecolorTag(name);
            var miDelete = new MenuItem { Header = Loc("Str_TT_TagDelete") };
            miDelete.Click += (_, _) => DeleteTag(name);
            menu.Items.Add(miRename);
            menu.Items.Add(miColor);
            menu.Items.Add(miDelete);
            row.ContextMenu = menu;
            return row;
        }

        private Button RowButton(string glyph, string tip, Action onClick)
        {
            var b = new Button
            {
                Content = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12,
                Width = 26, Height = 22, Margin = new Thickness(2, 0, 0, 0), Padding = new Thickness(0),
                ToolTip = tip, Style = TryFindResource("SurfaceButton") as Style,
            };
            b.Click += (_, _) => onClick();
            return b;
        }

        // ---- Add ----

        private void NewColorSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            var dlg = new ColorPickerDialog(this, ColorFromHex(_newColor)) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _newColor = HexFromColor(dlg.SelectedColor);
                NewColorSwatch.Background = new SolidColorBrush(dlg.SelectedColor);
            }
        }

        private void NewNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Add_Click(sender, e);
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            // Commas would break the notes.tags CSV, so strip them from tag names.
            string name = NewNameBox.Text.Replace(",", "").Trim();
            if (name.Length == 0) { DlgStatus.Text = Loc("Str_Tags_NeedName"); return; }
            if (NoteStore.ListTags().Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            { DlgStatus.Text = Loc("Str_Tags_Exists"); return; }

            NoteStore.AddTag(name, _newColor);
            NewNameBox.Text = "";
            DlgStatus.Text = "";
            Changed(select: name);
        }

        // ---- Per-row actions ----

        private void RecolorTag(string name)
        {
            string cur = NoteStore.ListTags().FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)).Color ?? "#50AEE8";
            var dlg = new ColorPickerDialog(this, ColorFromHex(cur)) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                NoteStore.SetTagColor(name, HexFromColor(dlg.SelectedColor));
                Changed(select: name);
            }
        }

        // Inline rename: swap the row's label for a TextBox (same pattern as Manage
        // databases). Commit on Enter or lost focus; Esc cancels.
        private void BeginRename(Grid grid, TextBlock label, string name)
        {
            var box = new TextBox
            {
                Text = name, Height = 22, VerticalContentAlignment = VerticalAlignment.Center,
                Background = BrushFromResource("PaneBrush"), Foreground = BrushFromResource("TextBrush"),
                BorderBrush = BrushFromResource("InputBorderBrush"), BorderThickness = new Thickness(1),
                CaretBrush = BrushFromResource("TextBrush"), Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(box, 1);
            label.Visibility = Visibility.Collapsed;
            grid.Children.Add(box);
            box.Focus();
            box.SelectAll();

            bool done = false;
            void Commit(bool apply)
            {
                if (done) return;
                done = true;
                grid.Children.Remove(box);
                label.Visibility = Visibility.Visible;
                if (apply) CommitRename(name, box.Text);
            }
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Commit(true);
                else if (e.Key == Key.Escape) Commit(false);
            };
            box.LostFocus += (_, _) => Commit(true);
        }

        private void CommitRename(string oldName, string raw)
        {
            string next = raw.Replace(",", "").Trim();
            if (next.Length == 0 || string.Equals(next, oldName, StringComparison.OrdinalIgnoreCase)) return;
            if (NoteStore.ListTags().Any(t => string.Equals(t.Name, next, StringComparison.OrdinalIgnoreCase)))
            { DlgStatus.Text = Loc("Str_Tags_Exists"); return; }

            NoteStore.RenameTag(oldName, next);
            Changed(select: next);
        }

        private void DeleteTag(string name)
        {
            var confirm = new ConfirmDialog(
                string.Format(Loc("Str_Tags_DeleteHead"), name),
                Loc("Str_Tags_DeleteBody"),
                Loc("Str_Btn_Delete")) { Owner = this };
            confirm.ShowDialog();
            if (!confirm.Confirmed) return;
            NoteStore.DeleteTag(name);
            Changed();
        }

        // ---- Color helpers ----

        private static Brush BrushFromHex(string hex)
        {
            try { return new SolidColorBrush(ColorFromHex(hex)); }
            catch { return Brushes.Gray; }
        }

        private Brush BrushFromResource(string key) =>
            TryFindResource(key) as Brush ?? Brushes.Gray;

        private static Color ColorFromHex(string hex) =>
            (Color)ColorConverter.ConvertFromString(hex);

        private static string HexFromColor(Color c) =>
            $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        // ---- Chrome ----

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
