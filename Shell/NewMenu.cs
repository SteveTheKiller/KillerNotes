using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KillerNotes.Controls;
using KillerNotes.Models;

namespace KillerNotes.Shell
{
    // 1.7.0 homes for what left the note menu. The note card is about one note, so:
    //   - the other ways to START a note (markdown, from template, today's) sit behind the
    //     chevron beside New note (MainWindow.xaml NewMenuBtn / NewMenu);
    //   - whole-library import and export sit on the rail's library button (LibraryRailBtn);
    //   - a right-click on empty sidebar opens nothing, because there is no note to be about.
    public partial class MainWindow
    {
        /// <summary>The chevron's share of the New note column (Sidebar.cs label and wrap math).</summary>
        private double NewMenuBtnWidth() =>
            NewMenuBtn is { Visibility: Visibility.Visible } b ? b.Width + b.Margin.Left + b.Margin.Right : 0;

        private void NewMenuBtn_Click(object sender, RoutedEventArgs e)
        {
            NewMenu.PlacementTarget = NewMenuBtn;
            NewMenu.Placement = PlacementMode.Bottom;
            NewMenu.IsOpen = true;
        }

        private void NewMenu_Opened(object sender, RoutedEventArgs e) => BuildTemplateMenu();   // Templates.cs

        private void LibraryRail_Click(object sender, RoutedEventArgs e)
        {
            FlyoutPlacement.Attach(LibraryMenu, LibraryRailBtn);   // rises with its button
            LibraryMenu.IsOpen = true;
        }

        private void OrganizeRail_Click(object sender, RoutedEventArgs e) => OpenGroupManager();   // GroupManager.cs

        /// <summary>Keeps the note card to notes: a right-click on empty sidebar, or on nothing
        /// selected, opens no card. Group and trash headers carry their own menus and pass through.
        /// The keyboard (Menu key, Shift+F10) still opens the card for the selection.</summary>
        private void NotesList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            for (var d = e.OriginalSource as DependencyObject; d != null && !ReferenceEquals(d, NotesList); d = VisualTreeHelper.GetParent(d))
                if (d is FrameworkElement { ContextMenu: not null }) return;
            bool fromMouse = e.CursorLeft >= 0 && e.CursorTop >= 0;
            if (!NotesList.SelectedItems.OfType<Note>().Any() || (fromMouse && !_noteContextTarget)) e.Handled = true;
        }
    }
}
