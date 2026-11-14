using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

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

        private void NewMenu_Opened(object sender, RoutedEventArgs e)
        {
            BuildTemplateMenu();      // Templates.cs
            BuildNewInGroupMenu();
            NewRichQuickBtn.ToolTip = KillerFamily.Controls.CardBuilder.Tip(Loc("Str_KS_NewNote"), "Ctrl+N");
            NewMarkdownQuickBtn.ToolTip = KillerFamily.Controls.CardBuilder.Tip(Loc("Str_Ctx_NewMarkdownNote"), "Alt+N");
            NewTodayQuickBtn.ToolTip = KillerFamily.Controls.CardBuilder.Tip(Loc("Str_Ctx_TodayNote"), "Alt+D");
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (NewMenu.IsOpen) NewRichQuickBtn.Focus();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void NewCardRich_Click(object sender, RoutedEventArgs e)
        { NewMenu.IsOpen = false; NewNote_Click(sender, e); }

        private void NewCardMarkdown_Click(object sender, RoutedEventArgs e)
        { NewMenu.IsOpen = false; NewMarkdownNote_Click(sender, e); }

        private void NewCardToday_Click(object sender, RoutedEventArgs e)
        { NewMenu.IsOpen = false; TodayNote_Click(sender, e); }

        /// <summary>New note in group: every group by its full path, so a subgroup reads
        /// unambiguously. Picking one makes the note there and opens it, like the group card's
        /// New note button (GroupCard.cs NewNoteInGroup).</summary>
        private void BuildNewInGroupMenu()
        {
            NewInGroupMenu.Items.Clear();
            var groups = NoteStore.IsOpen ? NoteStore.ListGroupTree() : [];
            NewInGroupMenu.Visibility = groups.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var g in groups)
            {
                string path = g.Path;
                // A TextBlock header, so an underscore in a group name is not taken as an access key.
                var item = new MenuItem { Header = new TextBlock { Text = path.Replace(NoteStore.GroupSep, " / ") } };
                item.Click += (_, _) => NewNoteInGroup(path);
                NewInGroupMenu.Items.Add(item);
            }
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
