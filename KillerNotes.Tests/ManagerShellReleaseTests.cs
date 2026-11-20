using System;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class ManagerShellReleaseTests
    {
        [Fact]
        public void DatabasePickerClosesManagerBeforeItOpens() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            ((Button)window.FindName("OrganizeRailBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            bool? managerVisible = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var picker = Application.Current.Windows.OfType<DatabasesDialog>().FirstOrDefault();
                if (picker == null) return;
                timer.Stop();
                managerVisible = Application.Current.Windows.OfType<GroupManagerWindow>().Any();
                picker.Close();
            };
            timer.Start();
            try { ((Button)window.FindName("DatabasesBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            finally { timer.Stop(); }
            Assert.False(managerVisible);
        });

        [Fact]
        public void ClosingMainWindowClosesManagerBeforeStore() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            ((Button)window.FindName("OrganizeRailBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var manager = Application.Current.Windows.OfType<GroupManagerWindow>().Single();
            try
            {
                window.Close();
                Assert.DoesNotContain(Application.Current.Windows.OfType<Window>(), candidate => candidate is GroupManagerWindow);
                Assert.False(NoteStore.IsOpen);
            }
            finally { manager.Close(); }
        });

        [Fact]
        public void HiddenSidebarNoteHasTagsOnManagerCard() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            long id = NoteStore.Create("Hidden note");
            NoteStore.SetNoteTags(id, "Release");
            var target = new Button();
            var owner = new Window { Content = target, Width = 160, Height = 80, ShowInTaskbar = false };
            owner.Show();
            try
            {
                host.ShowNoteCard(new[] { id }, target, false);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                // 1.7.1: the note's tags are the checked rows of the card's Tags flyout.
                var rows = CardTileHelper.Items((ContextMenu)window.FindName("NoteCard"), "tags");
                Assert.Contains(rows, row => row.Header is Grid head
                    && head.Children.OfType<TextBlock>().Any(t => t.Text == "Release")
                    && head.Children.OfType<TextBlock>().Any(t => t.Text == "✓"));
            }
            finally { ((ContextMenu)window.FindName("NoteCard")).IsOpen = false; owner.Close(); }
        });

        [Theory]
        [InlineData(false, Theme.Cyanotic)]
        [InlineData(true, Theme.SE98)]
        public void HostUndoRestoresBranchMembershipsAndRoles(bool rename, Theme theme) => CardColorBehaviorTests.WithShell((window, host) =>
        {
            var previousTheme = ThemeManager.Current;
            ThemeManager.Apply(theme);
            string child = NoteStore.GroupPath("Work", "Child");
            NoteStore.AddGroup("Work");
            NoteStore.AddGroup(child, "Work");
            NoteStore.SetGroupColor(child, "#123456");
            long id = NoteStore.Create("Child note");
            NoteStore.SetNoteGroup(id, child);
            var target = new Button();
            var owner = new Window { Content = target, Width = 160, Height = 80, ShowInTaskbar = false };
            owner.Show();
            try
            {
                host.ShowGroupCard(child, 1, target, false);
                var groupCard = (ContextMenu)window.FindName("GroupCard");
                CardTileHelper.Run(groupCard, "templates");
                host.ShowGroupCard(child, 1, target, false);
                CardTileHelper.Run(groupCard, "daily");
                ((ContextMenu)window.FindName("GroupCard")).IsOpen = false;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                var expected = NoteStore.ListGroupTree();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                bool accepted = false;
                timer.Tick += (_, _) =>
                {
                    Window? dialog = rename ? Application.Current.Windows.OfType<InputDialog>().FirstOrDefault()
                        : Application.Current.Windows.OfType<ConfirmDialog>().FirstOrDefault();
                    if (dialog == null) return;
                    timer.Stop();
                    if (rename) ((TextBox)dialog.FindName("ValueBox")).Text = "Job";
                    accepted = true;
                    ((Button)dialog.FindName("OkButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                timer.Start();
                try { if (rename) host.RenameGroup("Work"); else host.DeleteGroup("Work"); }
                finally { timer.Stop(); }
                Assert.True(accepted);
                Assert.True(host.Undo());
                Assert.Equal(expected, NoteStore.ListGroupTree());
                Assert.Equal(child, NoteStore.List().Single(n => n.Id == id).Notebook);
                host.ShowGroupCard(child, 1, target, false);
                Assert.True(CardTileHelper.Checked(groupCard, "templates"));
                Assert.True(CardTileHelper.Checked(groupCard, "daily"));
                ((ContextMenu)window.FindName("GroupCard")).IsOpen = false;
                string? renderDirectory = Environment.GetEnvironmentVariable("KN_MANAGER_RENDER_DIR");
                if (!string.IsNullOrEmpty(renderDirectory))
                {
                    ((Button)window.FindName("OrganizeRailBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var manager = Application.Current.Windows.OfType<GroupManagerWindow>().Single();
                    manager.Reload(child);
                    ((FrameworkElement)manager.FindName("RootBorder")).BeginAnimation(UIElement.OpacityProperty, null);
                    ((FrameworkElement)manager.FindName("RootBorder")).Opacity = 1;
                    manager.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)manager.ActualWidth, (int)manager.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(manager);
                    Directory.CreateDirectory(renderDirectory);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(renderDirectory, "manager-restored-" + theme + ".png"));
                    encoder.Save(output);
                }
            }
            finally { ((ContextMenu)window.FindName("GroupCard")).IsOpen = false; owner.Close(); ThemeManager.Apply(previousTheme); }
        });
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void HostUndoRefusesCollisionAndRetainsEntryForRetry(bool rename) => CardColorBehaviorTests.WithShell((window, host) =>
        {
            NoteStore.AddGroup("Work");
            long original = NoteStore.Create("Original");
            NoteStore.SetNoteGroup(original, "Work");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                Window? dialog = rename ? Application.Current.Windows.OfType<InputDialog>().FirstOrDefault()
                    : Application.Current.Windows.OfType<ConfirmDialog>().FirstOrDefault();
                if (dialog == null) return;
                timer.Stop();
                if (rename) ((TextBox)dialog.FindName("ValueBox")).Text = "Job";
                ((Button)dialog.FindName("OkButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
            try { if (rename) host.RenameGroup("Work"); else host.DeleteGroup("Work"); }
            finally { timer.Stop(); }
            NoteStore.AddGroup("Work");
            NoteStore.SetGroupColor("Work", "#ABCDEF");
            long recreated = NoteStore.Create("Recreated");
            NoteStore.SetNoteGroup(recreated, "Work");
            var before = NoteStore.ListGroupTree();

            Assert.True(host.Undo());
            Assert.Equal(before, NoteStore.ListGroupTree());
            Assert.Equal(rename ? "Job" : "", NoteStore.List().Single(n => n.Id == original).Notebook);
            Assert.Equal("Work", NoteStore.List().Single(n => n.Id == recreated).Notebook);
            NoteStore.DeleteGroup("Work");
            Assert.True(host.Undo());
            Assert.Equal("Work", NoteStore.List().Single(n => n.Id == original).Notebook);
            Assert.Equal("", NoteStore.List().Single(n => n.Id == recreated).Notebook);
            Assert.False(host.Undo());
        });
        [Fact]
        public void FailedCopyUndoKeepsSaveFailureAndCanRetry() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            long original = NoteStore.Create("Original");
            host.OpenNote(original);
            var target = new Button();
            var owner = new Window { Content = target, ShowInTaskbar = false };
            owner.Show();
            try
            {
                host.ShowNoteCard(new[] { original }, target, false);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                CardTileHelper.Click((ContextMenu)window.FindName("NoteCard"), "duplicate");
                long copy = NoteStore.List().Single(n => n.Id != original).Id;
                ((TextBox)window.FindName("TitleBox")).Text = "Unsaved copy edit";
                NoteStore.Close();
                Assert.True(host.Undo());
                string prefix = ((string)window.FindResource("Str_St_SaveFailed")).Split('{')[0];
                Assert.StartsWith(prefix, ((TextBlock)window.FindName("StatusText")).Text);
                NoteStore.Open();
                Assert.NotNull(NoteStore.GetNote(copy));
                Assert.True(host.Undo());
                Assert.Equal("Unsaved copy edit", NoteStore.ListTrash().Single(n => n.Id == copy).Title);
            }
            finally { ((ContextMenu)window.FindName("NoteCard")).IsOpen = false; owner.Close(); }
        });
    }
}
