using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class Release170ShellTests
    {
        [Fact]
        public void DisabledWindowsWheelScrollingDoesNotMoveTheNote() =>
            Assert.Equal(0, Shell.MainWindow.NoteWheelStep(120, 0, 800, 1));

        [Fact]
        public void CardSubmenusExposeTheirActionNames() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            // 1.7.1: the lists are flyout tiles, each named for its action.
            long id = NoteStore.Create("Named note");
            NoteStore.AddGroup("Named group");
            var target = new Button();
            var owner = new Window { Content = target, ShowInTaskbar = false };
            owner.Show();
            var noteCard = (ContextMenu)window.FindName("NoteCard");
            var groupCard = (ContextMenu)window.FindName("GroupCard");
            try
            {
                host.ShowNoteCard(new[] { id }, target, false);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.Equal("Tags", AutomationProperties.GetName(CardTileHelper.Tile(noteCard, "tags")));
                Assert.Equal("Group", AutomationProperties.GetName(CardTileHelper.Tile(noteCard, "group")));
                noteCard.IsOpen = false;
                host.ShowGroupCard("Named group", 0, target, false);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.Equal("Parent", AutomationProperties.GetName(CardTileHelper.Tile(groupCard, "parent")));
            }
            finally { noteCard.IsOpen = false; groupCard.IsOpen = false; owner.Close(); }
        });

        [Fact]
        public void PinnedNoteCardAnnouncesUnpin() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            long id = NoteStore.Create("Pinned note");
            NoteStore.SetPinned(id, true);
            var target = new Button();
            var owner = new Window { Content = target, ShowInTaskbar = false };
            owner.Show();
            try
            {
                host.ShowNoteCard(new[] { id }, target, false);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.Equal("Unpin", AutomationProperties.GetName(CardTileHelper.Tile((ContextMenu)window.FindName("NoteCard"), "pin")));
            }
            finally { ((ContextMenu)window.FindName("NoteCard")).IsOpen = false; owner.Close(); }
        });

        [Fact]
        public void UndoDuplicatePreservesUnsavedEditsAndLeavesAnExistingNoteOpen() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            long source = NoteStore.Create("Original");
            host.OpenNote(source);
            var target = new Button();
            var owner = new Window { Content = target, ShowInTaskbar = false };
            owner.Show();
            try
            {
                host.ShowNoteCard(new[] { source }, target, false);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                CardTileHelper.Click((ContextMenu)window.FindName("NoteCard"), "duplicate");
                long copy = NoteStore.List().Single(n => n.Id != source).Id;
                ((TextBox)window.FindName("TitleBox")).Text = "Edited copy";
                ((TextBox)window.FindName("SearchBox")).Text = "Edited copy";
                Assert.True(host.Undo());
                Assert.Equal("Edited copy", NoteStore.ListTrash().Single(n => n.Id == copy).Title);
                Assert.NotEqual("Edited copy", ((TextBox)window.FindName("TitleBox")).Text);
                Assert.NotNull(NoteStore.GetNote(source));
            }
            finally { ((ContextMenu)window.FindName("NoteCard")).IsOpen = false; owner.Close(); }
        });
        [Fact]
        public void UndoFirstTemplateRemovesOnlyTheGroupItCreated() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            long source = NoteStore.Create("Original");
            host.OpenNote(source);
            var target = new Button();
            var owner = new Window { Content = target, ShowInTaskbar = false };
            owner.Show();
            try
            {
                host.ShowNoteCard(new[] { source }, target, false);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                CardTileHelper.Run((ContextMenu)window.FindName("NoteCard"), "template");
                Assert.Contains(NoteStore.ListGroups(), g => g.Name == "Templates");
                Assert.True(host.Undo());
                Assert.DoesNotContain(NoteStore.ListGroups(), g => g.Name == "Templates");
                Assert.Single(NoteStore.List());
            }
            finally { ((ContextMenu)window.FindName("NoteCard")).IsOpen = false; owner.Close(); }
        });
        [Fact]
        public void SortButtonAdvertisesAllThreeSortShortcuts() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            Assert.Equal("Alt+4, Alt+5, Alt+6", AutomationProperties.GetAcceleratorKey((Button)window.FindName("SortBtn")));
        });

        [Fact]
        public void SortButtonFollowsTheLanguagePicker() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            var previous = LocaleManager.Current;
            try
            {
                ((Button)window.FindName("LangButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var menu = (ContextMenu)window.FindName("LangMenu");
                var columns = (StackPanel)menu.Items[0];
                columns.Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<RadioButton>())
                    .Single(r => (string)r.Tag == "De").IsChecked = true;
                var sort = (Button)window.FindName("SortBtn");
                Assert.Equal((string)window.FindResource("Str_St_SortNewest"), AutomationProperties.GetName(sort));
                Assert.Contains("Alt+4", sort.ToolTip.ToString());
            }
            finally { ((ContextMenu)window.FindName("LangMenu")).IsOpen = false; LocaleManager.Apply(previous); }
        });
        [Fact]
        public void SingleNoteActionsChooseTheLiveNoteInAMixedSelection()
        {
            var trashed = new Models.Note { Id = 1, Deleted = DateTime.Now };
            var live = new Models.Note { Id = 2 };
            Assert.Same(live, Shell.MainWindow.SingleLiveNote(new[] { trashed, live }));
        }

        [Fact]
        public void RetypingARevealedPasswordClearsTheFailedAttempt() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            var dialog = new PasswordDialog("Unlock", "", "Unlock");
            ((PasswordBox)dialog.FindName("PwBox")).Password = "wrong";
            ((Button)dialog.FindName("RevealButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog.UseUnlockLayout("Demo", "Incorrect password");
            ((TextBox)dialog.FindName("PwPlain")).Text = "another attempt";
            Assert.Equal(Visibility.Collapsed, ((StackPanel)dialog.FindName("ErrorLine")).Visibility);
        });
    }
}
