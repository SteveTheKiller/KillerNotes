using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using KillerFamily.Controls;

namespace KillerNotes.Shell
{
    /// <summary>
    /// KillerNotes' small tile cards, the KillerUI card rules (CardBuilder.TileGroups) the way KillerShot uses them:
    /// three tiles across, every tile an MDL2 glyph over a short caption with its shortcut on a second line, related
    /// tiles side by side, and no text rows. A small card is fixed; it has no edit pencil.
    /// </summary>
    internal static class NoteCards
    {
        /// <summary>The card width for three tiles across (the menu adds its 22px halo on each side).</summary>
        internal const double Width = 276;

        /// <summary>A new card at the three-across width.</summary>
        internal static ContextMenu Menu() => CardBuilder.Menu(Width);

        /// <summary>
        /// A card with an optional header and one fixed tile grid. The tiles run on in order, so every row is full but
        /// the last.
        /// </summary>
        internal static ContextMenu Build(string? title, params IEnumerable<CardTile>[] groups)
        {
            var card = Menu();
            Fill(card, title, groups);
            return card;
        }

        /// <summary>Replaces a card's items with an optional header and one fixed tile grid.</summary>
        internal static void Fill(ContextMenu card, string? title, params IEnumerable<CardTile>[] groups)
        {
            card.Items.Clear();
            if (!string.IsNullOrEmpty(title)) card.Items.Add(CardBuilder.Header(title!));
            string name = title ?? (Application.Current?.TryFindResource("Str_Card_Actions") as string ?? "Actions");
            var tiles = groups.SelectMany(g => g).ToList();
            var strip = CardBuilder.TileGroups(card, name, tiles.Count > 0 ? new[] { tiles } : new List<CardTile>[0],
                CardBuilder.CardColumns);
            var slot = CardBuilder.Slot(strip, new Thickness(0));
            AutomationProperties.SetName(slot, name);
            card.Items.Add(slot);
        }

        /// <summary>The tile buttons on a card, in order (a tile carries its shown caption as its Tag).</summary>
        internal static IEnumerable<Button> Tiles(ContextMenu card) =>
            card.Items.OfType<MenuItem>().SelectMany(i => Logical(i.Header as DependencyObject))
                .OfType<Button>().Where(b => b.Tag is string);

        private static IEnumerable<DependencyObject> Logical(DependencyObject? root)
        {
            if (root == null) yield break;
            yield return root;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                foreach (var d in Logical(child)) yield return d;
        }
    }
}
