using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using KillerNotes.Services;
using CardBuilder = KillerFamily.Controls.CardBuilder;
using SwatchRow = KillerFamily.Controls.SwatchRow;

namespace KillerNotes.Controls
{
    // Manage tags: add / rename / recolor / delete the open database's tag definitions.
    // Rename and delete ripple through every note's CSV (NoteStore). The note store stays
    // OPEN (unlike Manage databases) - these are ordinary row edits on the live db.
    public partial class TagsDialog : Window
    {
        // Cancel the first close, fade out, then close for real (Anim.FadeOutAndClose). A
        // DialogResult set before this survives the cancel and is delivered by the real close.
        private bool _closeFaded;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (Anim.FadeOutAndClose(this, ref _closeFaded)) { e.Cancel = true; return; }
            base.OnClosing(e);
        }

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
            DialogShortcuts.Button(this, AddBtn, "Ctrl+N", Key.N, ModifierKeys.Control);
            DialogShortcuts.Bind(this, AddBtn, "Enter", Key.Enter, ModifierKeys.None, () => Add_Click(this, new RoutedEventArgs()));
            DialogShortcuts.Describe(AddBtn, "Enter / Ctrl+N");
            DialogShortcuts.Button(this, DoneBtn, "Esc", Key.Escape);
            DialogShortcuts.Describe(TitleBar, "Esc");
            DialogShortcuts.Bind(this, NewColorSwatch, "Alt+N", Key.N, ModifierKeys.Alt, () => NewColorSwatch_Click(this, null!));
            DialogShortcuts.Bind(this, TagList, "F2", Key.F2, ModifierKeys.None, () => InvokeSelectedRow(1));
            DialogShortcuts.Bind(this, TagList, "Alt+C", Key.C, ModifierKeys.Alt, () => InvokeSelectedRow(0));
            DialogShortcuts.Bind(this, TagList, "Ctrl+D", Key.D, ModifierKeys.Control, () => InvokeSelectedRow(2));
            DialogShortcuts.Describe(TagList, "F2 / Alt+C / Ctrl+D");
            Loaded += (_, _) => Anim.FadeIn(RootBorder);
            NewColorSwatch.Background = BrushFromHex(_newColor);
            Refresh();
        }

        private void Refresh(string? select = null)
        {
            TagList.Items.Clear();
            // One read of the saved swatches for every row's card, not one per tag.
            var palette = ColorPickerDialog.UserSwatches();
            foreach (var (name, color) in NoteStore.ListTags())
                TagList.Items.Add(BuildRow(name, color, palette));
            if (select != null)
                foreach (ListBoxItem item in TagList.Items)
                    if ((string)item.Tag == select) { item.IsSelected = true; break; }
        }

        // swatch + name + [recolor] [rename] [delete], all carrying the tag name.
        private ListBoxItem BuildRow(string name, string colorHex, IReadOnlyList<Color> palette)
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
            label.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1)
            });
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(actions, 2);
            actions.Children.Add(RowButton("", Loc("Str_TT_TagRecolor"), () => RecolorTag(name), "Alt+C"));
            actions.Children.Add(RowButton("", Loc("Str_TT_TagRename"),  () => BeginRename(grid, label, name), "F2"));
            actions.Children.Add(RowButton("", Loc("Str_TT_TagDelete"),  () => DeleteTag(name), "Ctrl+D"));
            grid.Children.Add(actions);

            var row = new ListBoxItem { Content = grid, Tag = name, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            System.Windows.Automation.AutomationProperties.SetName(row, name);
            // Double-click the row to rename inline (as well as the rename button).
            row.MouseDoubleClick += (_, _) => BeginRename(grid, label, name);

            // Right-click card (1.7.0): the tag over its color row, then Rename and Delete.
            row.ContextMenu = BuildTagCard(name, colorHex, palette,
                rename: () => BeginRename(grid, label, name),
                recolor: () => RecolorTag(name),
                setColor: hex => SetTagColor(name, hex),
                delete: () => DeleteTag(name));
            return row;
        }

        /// <summary>A tag's right-click card: its name beside its color, a Color row whose
        /// swatches recolor it in one click and whose picker does what Change color (Alt+C) does,
        /// then Rename and Delete as rows with their shortcuts. A strip holding Rename alone would
        /// be a lone button, so both stay rows. Built from plain values, so a test can build it
        /// without a database.</summary>
        internal static ContextMenu BuildTagCard(string name, string colorHex, IEnumerable<Color> palette,
            Action rename, Action recolor, Action<string> setColor, Action delete)
        {
            var card = CardBuilder.Menu(360);

            // The header's dot is the note card's CardColorDot: the tag's color before its name.
            var header = CardBuilder.Header(name);
            var text = (StackPanel)header.Header;
            header.Header = null;
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 10, Height = 10, Margin = new Thickness(0, 0, 10, 0), StrokeThickness = 1,
                VerticalAlignment = VerticalAlignment.Center, Fill = BrushFromHex(colorHex),
            };
            dot.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "MenuBorderBrush");
            var head = new DockPanel { Margin = text.Margin };
            text.Margin = new Thickness(0);
            DockPanel.SetDock(dot, Dock.Left);
            head.Children.Add(dot);
            head.Children.Add(text);
            header.Header = head;
            card.Items.Add(header);

            var swatches = new SwatchRow { PickerName = CardBuilder.Tip(Loc("Str_TT_TagRecolor"), "Alt+C") };
            swatches.Show(palette, colorHex);
            // Every tag has a color (the chips fall back to gray without one), so the row's
            // no-color outline has nothing to mean here.
            swatches.Children[0].Visibility = Visibility.Collapsed;
            swatches.ColorChosen += hex =>
            {
                CardBuilder.Close(swatches);
                if (hex.Length > 0) setColor(hex);
            };
            swatches.PickRequested += () =>
            {
                CardBuilder.Close(swatches);
                recolor();
            };
            card.Items.Add(CardBuilder.Labeled(Loc("Str_Card_Color"), swatches));

            card.Items.Add(new Separator());
            var miRename = CardBuilder.Row(Loc("Str_TT_TagRename"), "F2", "\uE8AC", rename);
            DialogShortcuts.Describe(miRename, "F2");
            var miDelete = CardBuilder.Row(Loc("Str_TT_TagDelete"), "Ctrl+D", "\uE74D", delete);
            DialogShortcuts.Describe(miDelete, "Ctrl+D");
            card.Items.Add(miRename);
            card.Items.Add(miDelete);
            return card;
        }

        private void InvokeSelectedRow(int index)
        {
            if (TagList.SelectedItem is not ListBoxItem { Content: Grid grid }) return;
            if (grid.Children.OfType<StackPanel>().FirstOrDefault() is not StackPanel actions) return;
            if (actions.Children[index] is Button button && button.IsEnabled)
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        }

        private Button RowButton(string glyph, string tip, Action onClick, string gesture)
        {
            var b = new Button
            {
                Content = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12,
                Width = 26, Height = 22, Margin = new Thickness(2, 0, 0, 0), Padding = new Thickness(0),
                ToolTip = tip, Style = TryFindResource("SurfaceButton") as Style,
            };
            b.Click += (_, _) => onClick();
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            DialogShortcuts.Describe(b, gesture);
            return b;
        }

        // ---- Add ----

        private void NewColorSwatch_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ColorPickerDialog(this, ColorFromHex(_newColor)) { Owner = this };
            // Confirmed, not ShowDialog() == true: the picker's close fade nulls DialogResult
            // (ColorPickerDialog.Confirmed doc), so the == true check silently dropped the pick.
            dlg.ShowDialog();
            if (dlg.Confirmed)
            {
                _newColor = HexFromColor(dlg.SelectedColor);
                NewColorSwatch.Background = new SolidColorBrush(dlg.SelectedColor);
            }
        }

        private void NewNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { Add_Click(sender, e); e.Handled = true; }
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
            dlg.ShowDialog();
            if (dlg.Confirmed) SetTagColor(name, HexFromColor(dlg.SelectedColor));
        }

        // The picker and the card's swatches both land here.
        private void SetTagColor(string name, string hex)
        {
            NoteStore.SetTagColor(name, hex);
            Changed(select: name);
        }

        // Inline rename: swap the row's label for a TextBox (same pattern as Manage
        // databases). Commit on Enter or lost focus; Esc cancels.
        private void BeginRename(Grid grid, TextBlock label, string name)
        {
            var box = CreateRenameBox(name);
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
                if (Keyboard.Modifiers != ModifierKeys.None) return;
                if (e.Key == Key.Enter) { Commit(true); e.Handled = true; }
                else if (e.Key == Key.Escape) { Commit(false); e.Handled = true; }
            };
            box.LostFocus += (_, _) => Commit(true);
        }

        private void CommitRename(string oldName, string raw)
        {
            string next = raw.Replace(",", "").Trim();
            // Tags keep the capitals they were typed with, so a change of capitals alone is a rename.
            if (next.Length == 0 || string.Equals(next, oldName, StringComparison.Ordinal)) return;
            if (NoteStore.ListTags().Any(t => !string.Equals(t.Name, oldName, StringComparison.Ordinal)
                    && string.Equals(t.Name, next, StringComparison.OrdinalIgnoreCase)))
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

        internal static TextBox CreateRenameBox(string name)
        {
            var box = new TextBox
            {
                Text = name, VerticalContentAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(1), Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(0, 0, 8, 0),
            };
            box.SetResourceReference(Control.BackgroundProperty, "PaneBrush");
            box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            box.SetResourceReference(Control.BorderBrushProperty, "InputBorderBrush");
            box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "TextBrush");
            box.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, "Str_TT_TagRename");
            return box;
        }

        private Brush BrushFromResource(string key) =>
            TryFindResource(key) as Brush ?? Brushes.Gray;

        private static Color ColorFromHex(string hex) =>
            (Color)ColorConverter.ConvertFromString(hex);

        private static string HexFromColor(Color c) =>
            $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        // ---- Chrome ----

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
