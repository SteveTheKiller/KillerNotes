using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using KillerFamily.Controls;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Shell
{
    // 1.7.0 homes for what left the note menu. The note card is about one note, so:
    //   - the other ways to START a note (markdown, from template, today's) sit on the tile card
    //     behind the chevron beside New note (MainWindow.xaml NewMenuBtn / NewMenu);
    //   - folder import sits in the New note menu; whole-notepad export uses LibraryRailBtn;
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

        /// <summary>The New in group and From template lists the card's flyout tiles open.</summary>
        private readonly MenuItem NewInGroupMenu = new(), TemplateMenu = new();

        /// <summary>The creation card: New note, Markdown and Today's note, then the In group and Template
        /// lists and folder import, each tile with its shortcut.</summary>
        private void NewMenu_Opened(object sender, RoutedEventArgs e)
        {
            BuildTemplateMenu();      // Templates.cs
            BuildNewInGroupMenu();
            NoteCards.Prepare(NewMenu);
            NewMenu.Items.Add(NoteCards.Header(NewMenu, Loc("Str_KS_NewNote")));
            CardTile Act(string id, string glyph, string caption, string? shown, string keys, RoutedEventHandler run) =>
                new(glyph, Loc(caption), keys, () => run(this, new RoutedEventArgs())) { Id = id, Shown = shown == null ? null : Loc(shown) };
            NewMenu.Items.Add(NoteCards.Grid(NewMenu, new[]
            {
                Act("rich", "", "Str_KS_NewNote", "Str_Card_Rich", "Ctrl+N", NewNote_Click),
                Act("markdown", "", "Str_Ctx_NewMarkdownNote", "Str_Card_Markdown", "Alt+N", NewMarkdownNote_Click),
                Act("today", "", "Str_Ctx_TodayNote", null, "Alt+D", TodayNote_Click),
                NoteCards.Flyout("", Loc("Str_Ctx_NewNoteInGroupMenu"), null, NewInGroupMenu.Items.Cast<object>().ToList(), id: "ingroup")
                    with { Shown = Loc("Str_Card_Group") },
                NoteCards.Flyout("", Loc("Str_Ctx_NewFromTemplate"), "Alt+T", TemplateMenu.Items.Cast<object>().ToList(), id: "template")
                    with { Shown = Loc("Str_Card_Templates") },
                Act("import", "", "Str_Ctx_ImportFolder", "Str_Card_Import", "Alt+Shift+I", ImportFolder_Click),
            }));
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (NewMenu.IsOpen) NoteCards.FirstTile(NewMenu)?.Focus();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        /// <summary>New note in group: every group by its full path, so a subgroup reads
        /// unambiguously. Picking one makes the note there and opens it, like the group card's
        /// New note button (GroupCard.cs NewNoteInGroup).</summary>
        private void BuildNewInGroupMenu()
        {
            NewInGroupMenu.Items.Clear();
            var groups = NoteStore.IsOpen ? NoteStore.ListGroupTree() : [];
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
