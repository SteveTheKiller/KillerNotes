using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KillerFamily.Controls;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    /// <summary>
    /// KillerNotes' family tile cards, the KillerUI card rules (CardBuilder.TileGroups) the way KillerShot uses them:
    /// 1. Three tiles across, every tile an MDL2 glyph over a short caption with its shortcut on a second line.
    /// 2. Tiles run on in order, related ones side by side, so every row is full but the last.
    /// 3. Most used first, destructive last; Delete on a card for one thing sits in the header.
    /// 4. A tile that only sometimes applies stays in its place, dimmed, so the rows never shift.
    /// 5. No text rows: everything is a tile, or a flyout tile for a list such as Move to.
    /// 6. A big card is customizable (Editable, the kit's CardEdit); a small card stays fixed (Grid).
    /// </summary>
    internal static class NoteCards
    {
        /// <summary>The card width for three tiles across (the menu adds its 22px halo on each side).</summary>
        internal const double Width = 276;

        /// <summary>Tiles across a card row.</summary>
        internal const int Columns = 3;

        /// <summary>The tag on a card header's Delete button.</summary>
        internal const string HeaderDeleteTag = "carddelete";

        private static readonly DependencyProperty DeleteButtonProperty =
            DependencyProperty.RegisterAttached("DeleteButton", typeof(Button), typeof(NoteCards));

        private static readonly DependencyProperty WatchedProperty =
            DependencyProperty.RegisterAttached("Watched", typeof(bool), typeof(NoteCards), new PropertyMetadata(false));

        private static readonly DependencyProperty TilesProperty =
            DependencyProperty.RegisterAttached("Tiles", typeof(Dictionary<string, CardTile>), typeof(NoteCards));

        private static string Loc(string key) => LocaleManager.Loc(key);

        /// <summary>A new card at the three-across width.</summary>
        internal static ContextMenu Menu() => CardBuilder.Menu(Width);

        /// <summary>Gives a menu made elsewhere (XAML) the card look and width, and empties it.</summary>
        internal static ContextMenu Prepare(ContextMenu menu)
        {
            menu.Items.Clear();
            menu.SetResourceReference(FrameworkElement.StyleProperty, "CardMenu");
            menu.MinWidth = menu.MaxWidth = Width + 44;
            return menu;
        }

        /// <summary>A small card with an optional plain header and one fixed tile grid.</summary>
        internal static ContextMenu Build(string? title, params IEnumerable<CardTile>[] groups)
        {
            var card = Menu();
            Fill(card, title, groups);
            return card;
        }

        /// <summary>Replaces a card's items with an optional plain header and one fixed tile grid.</summary>
        internal static void Fill(ContextMenu card, string? title, params IEnumerable<CardTile>[] groups)
        {
            card.Items.Clear();
            if (!string.IsNullOrEmpty(title)) card.Items.Add(CardBuilder.Header(title!));
            card.Items.Add(Grid(card, groups));
        }

        /// <summary>
        /// The card's header: its title, an optional dim line under it, and, on a card for one thing, that
        /// thing's Delete as an icon button at the right end.
        /// </summary>
        internal static MenuItem Header(ContextMenu card, string title, string? meta = null,
            string? deleteName = null, string? deleteKeys = null, Action? delete = null, bool deleteEnabled = true,
            Brush? titleBrush = null)
        {
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var head = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            if (titleBrush != null) head.Foreground = titleBrush;
            else head.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            text.Children.Add(head);
            if (!string.IsNullOrEmpty(meta))
            {
                var sub = new TextBlock { Text = meta, FontSize = 11, Margin = new Thickness(0, 1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                sub.SetResourceReference(TextBlock.ForegroundProperty, "DimTextBrush");
                text.Children.Add(sub);
            }
            var dock = new DockPanel { LastChildFill = true };
            if (delete != null && deleteName != null)
            {
                var button = new Button
                {
                    Content = "", Tag = HeaderDeleteTag, IsEnabled = deleteEnabled,
                    ToolTip = CardBuilder.Tip(deleteName, deleteKeys),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
                    FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14,
                };
                button.SetResourceReference(FrameworkElement.StyleProperty, "ToolbarButton");
                AutomationProperties.SetName(button, deleteName);
                if (!string.IsNullOrEmpty(deleteKeys)) AutomationProperties.SetAcceleratorKey(button, deleteKeys);
                ToolTipService.SetShowOnDisabled(button, true);
                var run = delete;
                button.Click += (_, _) => { card.IsOpen = false; run(); };
                DockPanel.SetDock(button, Dock.Right);
                dock.Children.Add(button);
                card.SetValue(DeleteButtonProperty, button);
                if (!(bool)card.GetValue(WatchedProperty))
                {
                    // A card opened from the keyboard starts on its first tile, never on Delete: Enter there would delete.
                    card.SetValue(WatchedProperty, true);
                    card.Opened += (_, _) => card.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        if (ReferenceEquals(Keyboard.FocusedElement, card.GetValue(DeleteButtonProperty))) FirstTile(card)?.Focus();
                    }));
                    card.PreviewKeyDown += (_, e) =>
                    {
                        if (!ReferenceEquals(Keyboard.FocusedElement, card)) return;
                        if (e.Key is not (Key.Down or Key.Up or Key.Left or Key.Right or Key.Tab or Key.Home)) return;
                        if (FirstTile(card) is { } first) { first.Focus(); e.Handled = true; }
                    };
                }
            }
            dock.Children.Add(text);
            var slot = CardBuilder.Slot(dock, new Thickness(4, 2, 4, 6));
            AutomationProperties.SetName(slot, title);
            return slot;
        }

        /// <summary>A fixed tile grid for a small card: the tiles fill rows of three in order.</summary>
        internal static MenuItem Grid(ContextMenu card, params IEnumerable<CardTile>[] groups) => BuildGrid(card, null, groups);

        /// <summary>
        /// A customizable tile grid for a big card (the kit's CardEdit): a pencil in the corner opens edit mode, and
        /// tiles move between the card and its Hidden drawer. Locked tiles always show; spares start in the drawer
        /// (both named by tile Id). The layout is kept as the "Card.{name}.Hidden" setting.
        /// </summary>
        internal static MenuItem Editable(ContextMenu card, string name, string[] locked, string[] spares, params IEnumerable<CardTile>[] groups)
        {
            var edit = new CardEdit(() => App.GetSetting($"Card.{name}.Hidden") is { Length: > 0 } saved ? saved : null,
                value => App.SetSetting($"Card.{name}.Hidden", value ?? ""))
            {
                Locked = t => locked.Contains(t.Id ?? t.Glyph),
                DefaultHidden = t => spares.Contains(t.Id ?? t.Glyph),
                EditTip = Loc("Str_Card_Edit"), DoneTip = Loc("Str_Card_EditDone"), ResetTip = Loc("Str_Card_EditReset"),
                ResetLabel = Loc("Str_Card_EditResetShort"), HiddenLabel = Loc("Str_Card_Hidden"),
                HiddenHint = Loc("Str_Card_HiddenHint"), LockedTip = Loc("Str_Card_AlwaysShown"),
                MovedText = Loc("Str_Card_TileMoved"),
            };
            return BuildGrid(card, edit, groups);
        }

        private static MenuItem BuildGrid(ContextMenu card, CardEdit? edit, IEnumerable<CardTile>[] groups)
        {
            var tiles = groups.SelectMany(g => g).ToList();
            var byName = new Dictionary<string, CardTile>();
            foreach (var tile in tiles)
                if (!byName.ContainsKey(Trimmed(tile.Caption))) byName[Trimmed(tile.Caption)] = tile;
            card.SetValue(TilesProperty, byName);
            string name = Loc("Str_Card_Quick");
            // A fixed card never leaves a gap in a row: rows of three, and where three do not come out even, the
            // last row or two hold two wider tiles (RowSizes). A customizable card keeps three across, since its
            // tiles come and go.
            FrameworkElement strip;
            var sizes = RowSizes(tiles.Count);
            if (edit != null || sizes.Distinct().Count() == 1)
                strip = CardBuilder.TileGroups(card, name, tiles.Count > 0 ? new[] { tiles } : new List<CardTile>[0], edit,
                    edit != null ? Columns : Math.Max(1, sizes[0]));
            else
            {
                var rows = new StackPanel();
                int at = 0;
                foreach (int size in sizes)
                {
                    rows.Children.Add(CardBuilder.TileGroups(card, name, new[] { tiles.GetRange(at, size) }, size));
                    at += size;
                }
                strip = rows;
            }
            var slot = CardBuilder.Slot(strip, new Thickness(0));
            AutomationProperties.SetName(slot, name);
            // A tile with an Id carries it as its button's Uid, the command name the tests read.
            foreach (var button in Descendants(strip).OfType<Button>())
                if (byName.TryGetValue(Trimmed(AutomationProperties.GetName(button) ?? ""), out var t) && t.Id is { } id) button.Uid = id;
            return slot;
        }

        /// <summary>The tiles in each row of a fixed card, every row full: up to three in one row, then rows of three
        /// with the leftovers as rows of two (4 is 2+2, 5 is 3+2, 7 is 3+2+2, 8 is 3+3+2).</summary>
        internal static IReadOnlyList<int> RowSizes(int tiles)
        {
            if (tiles <= Columns) return new[] { Math.Max(tiles, 0) };
            int twos = (Columns - tiles % Columns) % Columns;   // 0, 1 or 2 rows of two
            var rows = Enumerable.Repeat(Columns, (tiles - 2 * twos) / Columns).ToList();
            rows.AddRange(Enumerable.Repeat(2, twos));
            return rows;
        }

        private static string Trimmed(string caption) => caption.TrimEnd('.', '…').TrimEnd();

        private static CardTile? TileFor(Button button)
        {
            if (AutomationProperties.GetName(button) is not { Length: > 0 } name) return null;
            DependencyObject? at = button;
            while (at != null && at is not ContextMenu) at = LogicalTreeHelper.GetParent(at) ?? VisualTreeHelper.GetParent(at);
            return at?.GetValue(TilesProperty) is Dictionary<string, CardTile> map && map.TryGetValue(Trimmed(name), out var tile) ? tile : null;
        }

        /// <summary>The rows a flyout tile opens, or null for a tile that acts.</summary>
        internal static IList<object>? GetFlyoutRows(Button tile) => TileFor(tile)?.Flyout;

        /// <summary>A tile that opens a list beside itself. The rows are taken out of whatever held them.</summary>
        internal static CardTile Flyout(string glyph, string caption, string? keys, IEnumerable<object> rows, bool enabled = true, string? id = null)
        {
            var list = rows.ToList();
            return new CardTile(glyph, caption, keys, null, false, enabled && list.Count > 0) { Flyout = list, Id = id };
        }

        /// <summary>The rows of an ItemsControl built as a submenu, ready for a flyout tile.</summary>
        internal static IEnumerable<object> RowsOf(ItemsControl source)
        {
            var rows = source.Items.Cast<object>().ToList();
            source.Items.Clear();
            return rows;
        }

        /// <summary>Every tile and header button on a card, in order.</summary>
        internal static IEnumerable<Button> Buttons(ItemsControl card) => card.Items.OfType<MenuItem>().SelectMany(ButtonsIn);

        /// <summary>The tile buttons on a card, in order (a tile carries its shown caption as its Tag).</summary>
        internal static IEnumerable<Button> Tiles(ItemsControl card) =>
            Buttons(card).Where(b => b.Tag is string tag && tag != HeaderDeleteTag && b.Parent is not KillerFamily.Controls.SwatchRow);

        /// <summary>The tile with this Id, or null.</summary>
        internal static Button? Tile(ItemsControl card, string id) => Buttons(card).FirstOrDefault(b => b.Uid == id);

        /// <summary>The tile record with this Id on a built card, shown or in the Hidden drawer, or null.</summary>
        internal static CardTile? Find(DependencyObject card, string id) =>
            card.GetValue(TilesProperty) is Dictionary<string, CardTile> map ? map.Values.FirstOrDefault(t => t.Id == id) : null;

        /// <summary>The header's Delete button, or null on a card without one.</summary>
        internal static Button? HeaderDelete(ItemsControl card) => Buttons(card).FirstOrDefault(b => Equals(b.Tag, HeaderDeleteTag));

        /// <summary>The tiles or header buttons of one card item, in order.</summary>
        internal static IEnumerable<Button> ButtonsIn(MenuItem item) => Descendants(item.Header as DependencyObject).OfType<Button>();

        /// <summary>The first tile that can be used.</summary>
        internal static Button? FirstTile(ItemsControl card) =>
            Buttons(card).FirstOrDefault(b => !Equals(b.Tag, HeaderDeleteTag) && b.IsEnabled && b.Parent is not KillerFamily.Controls.SwatchRow);

        // The card's own controls: a customizable card's hidden measuring copy of its grid, its drawer (a popup)
        // and its edit pencil (a button with no caption) are not tiles.
        private static IEnumerable<DependencyObject> Descendants(DependencyObject? root)
        {
            if (root == null || root is Popup || root is UIElement { Visibility: not Visibility.Visible }) yield break;
            if (root is Button { Tag: null }) yield break;
            yield return root;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                foreach (var d in Descendants(child)) yield return d;
        }
    }
}
