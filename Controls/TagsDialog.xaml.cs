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

        private const string DefaultTagColor = "#50AEE8";
        private string _newColor = DefaultTagColor;   // default pick for the add row

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
            // A double-click inside the rename box selects a word; it must not start a second rename.
            row.MouseDoubleClick += (_, e) =>
            {
                if (IsInsideTextBox(e.OriginalSource as DependencyObject)) return;
                BeginRename(grid, label, name);
            };

            // Right-click card: the tag and its Delete over Rename and Color tiles.
            row.ContextMenu = BuildTagCard(name, colorHex, palette,
                rename: () => BeginRename(grid, label, name),
                recolor: () => RecolorTag(name),
                setColor: hex => SetTagColor(name, hex),
                delete: () => DeleteTag(name));
            return row;
        }

        /// <summary>A tag's right-click card on the family card rules (Shell/NoteCards.cs): its name beside its color
        /// with Delete as the header's icon button, then Rename and Color tiles. Color slides the swatch row out under the tiles; its swatches
        /// recolor the tag in one click and whose picker does what Change color (Alt+C) does. Built from plain values, so a test can build it
        /// without a database.</summary>
        internal static ContextMenu BuildTagCard(string name, string colorHex, IEnumerable<Color> palette,
            Action rename, Action recolor, Action<string> setColor, Action delete)
        {
            var card = Shell.NoteCards.Menu();

            // The family header (Shell/NoteCards.cs): the tag's name with Delete as its icon button. The dot is the
            // note card's CardColorDot, the tag's color before its name.
            var header = Shell.NoteCards.Header(card, name, null, Loc("Str_TT_TagDelete"), "Ctrl+D", delete);
            var head = TagHeaderDock(header);
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 10, Height = 10, Margin = new Thickness(0, 0, 10, 0), StrokeThickness = 1,
                VerticalAlignment = VerticalAlignment.Center, Fill = BrushFromHex(colorHex),
            };
            dot.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "MenuBorderBrush");
            DockPanel.SetDock(dot, Dock.Left);
            head.Children.Insert(head.Children.Count - 1, dot);   // before the name, which fills what is left
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
            // Rename and Color side by side. Color keeps the card open and slides the swatch row out under the tiles:
            // the card's bottom grows to show it, and a second click slides it back.
            swatches.HorizontalAlignment = HorizontalAlignment.Center;
            swatches.Margin = new Thickness(0, 4, 0, 2);
            // Hidden while slid in, so Tab and a screen reader never land on a swatch nobody can see.
            var drawer = new Border { Child = swatches, ClipToBounds = true, MaxHeight = 0, Visibility = Visibility.Hidden };
            card.Items.Add(Shell.NoteCards.Grid(card, new[]
            {
                new KillerFamily.Controls.CardTile("\uE8AC", Loc("Str_TT_TagRename"), "F2", rename) { Id = "rename" },
                new KillerFamily.Controls.CardTile("\uE790", Loc("Str_Card_Color"), null, () => SlideSwatches(drawer))
                    { Id = "color", Checked = false, KeepsOpen = true },
            }));
            card.Items.Add(CardBuilder.Slot(drawer, new Thickness(0)));
            return card;
        }

        /// <summary>Slides the tag card's swatch row out under its tiles, or back in when it is out. The Color tile's
        /// fill follows the same click, so the row stays out across reopenings exactly while the tile shows on.</summary>
        internal static void SlideSwatches(Border drawer)
        {
            bool opening = drawer.Tag is not true;
            drawer.Tag = opening;
            drawer.Child.Measure(new Size(drawer.ActualWidth > 0 ? drawer.ActualWidth : double.PositiveInfinity, double.PositiveInfinity));
            double full = drawer.Child.DesiredSize.Height;
            var slide = new System.Windows.Media.Animation.DoubleAnimation(opening ? full : 0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            };
            // The row shows as it starts out and hides once it is all the way back in; a click mid-slide
            // flips Tag first, so a finished close never hides a drawer that is opening again.
            if (opening) drawer.Visibility = Visibility.Visible;
            else slide.Completed += (_, _) => { if (drawer.Tag is not true) drawer.Visibility = Visibility.Hidden; };
            drawer.BeginAnimation(FrameworkElement.MaxHeightProperty, slide);
        }

        /// <summary>The DockPanel inside a NoteCards header: the Delete button docked right, then the title.</summary>
        internal static DockPanel TagHeaderDock(MenuItem header)
        {
            var queue = new Queue<object>([header.Header]);
            while (queue.Count > 0)
            {
                object at = queue.Dequeue();
                if (at is DockPanel dock) return dock;
                if (at is DependencyObject node)
                    foreach (object child in LogicalTreeHelper.GetChildren(node)) queue.Enqueue(child);
            }
            throw new InvalidOperationException("The card header has no DockPanel");
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
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)).Color ?? DefaultTagColor;
            var dlg = new ColorPickerDialog(this, PickerStartColor(cur)) { Owner = this };
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
            // F2 or a double-click while the row is already being renamed keeps the open box.
            if (grid.Children.OfType<TextBox>().Any()) return;
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

        private static bool IsInsideTextBox(DependencyObject? source)
        {
            for (var d = source; d != null;
                 d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is TextBox) return true;
            return false;
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

        /// <summary>The color the picker opens on for a stored hex value.</summary>
        internal static Color PickerStartColor(string? hex)
        {
            // A stored value written by hand or by an MCP client may not be valid hex.
            try { if (!string.IsNullOrWhiteSpace(hex)) return ColorFromHex(hex!); }
            catch (FormatException) { }
            catch (NotSupportedException) { }
            return ColorFromHex(DefaultTagColor);
        }

        private static string HexFromColor(Color c) =>
            $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        // ---- Chrome ----

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
