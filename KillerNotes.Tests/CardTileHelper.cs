using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>Drives the 1.7.1 tile cards the way a click does: open the card, find the tile by its Id, click it.</summary>
    internal static class CardTileHelper
    {
        /// <summary>A private field of the main window, for state that moved from XAML into code.</summary>
        internal static T Field<T>(object owner, string name) =>
            (T)owner.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner)!;

        /// <summary>Clicks a tile the way the mouse does (the kit closes the card, then runs the tile).</summary>
        internal static void Click(Button tile) => tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        /// <summary>Clicks the tile with this Id on an open card.</summary>
        internal static void Click(ContextMenu card, string id) => Click(Tile(card, id));

        /// <summary>Runs a tile's action the way the kit's click does (close the card, then act). Reaches a tile the
        /// card keeps in its Hidden drawer too, which a keyboard shortcut also runs.</summary>
        internal static void Run(ContextMenu card, string id)
        {
            var tile = Assert.IsType<KillerFamily.Controls.CardTile>(NoteCards.Find(card, id));
            Assert.True(tile.Enabled, $"The {id} tile is dimmed.");
            card.IsOpen = false;
            tile.Run!();
        }

        /// <summary>Whether a tile record (shown or hidden) is on.</summary>
        internal static bool Checked(ContextMenu card, string id) =>
            Assert.IsType<KillerFamily.Controls.CardTile>(NoteCards.Find(card, id)).Checked == true;

        /// <summary>The header's Delete button.</summary>
        internal static Button HeaderDelete(ContextMenu card) => Assert.IsType<Button>(NoteCards.HeaderDelete(card));

        /// <summary>The header's title text.</summary>
        internal static string Title(ContextMenu card) =>
            ((DockPanel)((MenuItem)card.Items[0]).Header).Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().First().Text;

        /// <summary>The rows a flyout tile opens (tags, group, parent, color, template lists).</summary>
        internal static IList<object> Rows(ContextMenu card, string id) =>
            Assert.IsAssignableFrom<IList<object>>(NoteCards.GetFlyoutRows(Tile(card, id)));

        /// <summary>Clicks a flyout tile and returns the list it opened beside the card.</summary>
        internal static ContextMenu OpenFlyout(ContextMenu card, string id)
        {
            var rows = Rows(card, id);
            Click(card, id);
            card.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var flyout = Assert.IsType<ContextMenu>(((FrameworkElement)rows.First()).Parent);
            Assert.True(flyout.IsOpen);
            return flyout;
        }

        /// <summary>The menu rows of a flyout tile.</summary>
        internal static List<MenuItem> Items(ContextMenu card, string id) => Rows(card, id).OfType<MenuItem>().ToList();

        /// <summary>The color palette a card's Color flyout holds.</summary>
        internal static KillerFamily.Controls.SwatchRow Swatches(ContextMenu card) =>
            Rows(card, "color").OfType<MenuItem>().Select(m => m.Header).OfType<KillerFamily.Controls.SwatchRow>().Single();

        /// <summary>Whether a checkable tile shows as on (the kit fills an on tile: CardBuilder.ShowTileState).</summary>
        internal static bool IsOn(ContextMenu card, string id) =>
            Tile(card, id).ReadLocalValue(Control.BackgroundProperty) != DependencyProperty.UnsetValue;

        /// <summary>Opens the new-note card from its chevron and returns the card.</summary>
        internal static ContextMenu OpenNewMenu(Window window)
        {
            ((Button)window.FindName("NewMenuBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            return (ContextMenu)window.FindName("NewMenu");
        }

        /// <summary>The tile with this Id on an open card; fails the test when it is missing.</summary>
        internal static Button Tile(ContextMenu card, string id) => Assert.IsType<Button>(NoteCards.Tile(card, id));

        /// <summary>Clicks a tile on the new-note card ("rich", "markdown", "today", "import").</summary>
        internal static void ClickNewTile(Window window, string id)
        {
            var card = OpenNewMenu(window);
            Tile(card, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            card.IsOpen = false;
        }
    }
}
