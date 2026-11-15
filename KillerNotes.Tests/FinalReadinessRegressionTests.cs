using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KillerFamily.Controls;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class FinalReadinessRegressionTests
    {
        [Fact]
        public void ReusedKeyboardCardReleasesItsEarlierTarget()
        {
            ContextMenu? menu = null;
            WeakReference? earlier = null;
            CardColorBehaviorTests.WithShell((window, host) =>
            {
                var panel = new StackPanel();
                var owner = new Window { Content = panel, ShowInTaskbar = false };
                owner.Show();
                menu = CardBuilder.Menu(200);
                menu.Items.Add(new MenuItem { Header = "Action" });
                try
                {
                    earlier = OpenEarlierTarget(menu, panel);
                    var current = new Button { Content = "Current target" };
                    panel.Children.Add(current);
                    OpenAndDismiss(menu, current);
                }
                finally { menu.IsOpen = false; owner.Close(); }
            });
            Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.NotNull(earlier);
            Assert.False(earlier!.IsAlive, "A shared card must release a removed target after a later opening.");
            GC.KeepAlive(menu);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference OpenEarlierTarget(ContextMenu menu, Panel panel)
        {
            var target = new Button { Content = "Earlier target" };
            panel.Children.Add(target);
            OpenAndDismiss(menu, target);
            panel.Children.Remove(target);
            return new WeakReference(target);
        }

        private static void OpenAndDismiss(ContextMenu menu, FrameworkElement target)
        {
            bool opened = false, closed = false;
            RoutedEventHandler onOpened = (_, _) => opened = true;
            RoutedEventHandler onClosed = (_, _) => closed = true;
            menu.Opened += onOpened;
            menu.Closed += onClosed;
            try
            {
                CardBuilder.Open(menu, target, true, new Rect(0, target.ActualHeight, 0, 0));
                WaitFor(() => opened, "The card must complete opening.");
                menu.IsOpen = false;
                WaitFor(() => closed, "The card must complete dismissal.");
            }
            finally { menu.Opened -= onOpened; menu.Closed -= onClosed; }
        }

        [Fact]
        public void ReusedKeyboardCardReturnsFocusToItsLatestTarget() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            var first = new Button { Content = "First target" };
            var second = new Button { Content = "Second target" };
            var panel = new StackPanel();
            panel.Children.Add(first);
            panel.Children.Add(second);
            var owner = new Window { Content = panel, ShowInTaskbar = false };
            owner.Show();
            owner.Activate();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var menu = CardBuilder.Menu(200);
            menu.Items.Add(CardBuilder.QuickStrip("Actions", new[] { new CardAction("A", "Action", "One action", null, () => { }) }));
            try
            {
                foreach (var target in new[] { first, second })
                {
                    Assert.True(target.Focus(), "The card target must accept focus.");
                    CardBuilder.Open(menu, target, true, new Rect(0, target.ActualHeight, 0, 0));
                    WaitFor(() => menu.IsKeyboardFocusWithin, "The card action must receive keyboard focus.");
                    var focused = (UIElement)Keyboard.FocusedElement;
                    InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(focused), 0, Key.Escape)
                        { RoutedEvent = Keyboard.KeyDownEvent, Source = focused });
                    WaitFor(() => !menu.IsOpen && target.IsKeyboardFocused, "Dismissal must return focus to the latest card target.");
                }
            }
            finally { menu.IsOpen = false; owner.Close(); }
        });

        private static void WaitFor(Func<bool> ready, string message)
        {
            var frame = new DispatcherFrame();
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { if (ready() || elapsed.Elapsed.TotalSeconds > 3) frame.Continue = false; };
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
            Assert.True(ready(), message);
        }

        [Fact]
        public void FirstMenuKeyRequestOpensTheFocusedGroupsCard() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            MarkdownAuthoringTests.Ready(window);
            NoteStore.AddGroup("Fresh keyboard group");
            ((Button)window.FindName("NewNoteBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var list = (ListBox)window.FindName("NotesList");
            var group = list.Items.OfType<Models.GroupHeader>().Single(item => item.Path == "Fresh keyboard group");
            list.ScrollIntoView(group);
            window.UpdateLayout();
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(group);
            Border Header(DependencyObject node)
            {
                if (node is Border { Focusable: true, DataContext: Models.GroupHeader } border) return border;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    if (Header(VisualTreeHelper.GetChild(node, i)) is { } child) return child;
                return null!;
            }
            var header = Header(row);
            Assert.NotNull(header);
            header.Focus();
            Assert.True(header.IsKeyboardFocused);
            try
            {
                InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(header), 0, Key.Apps)
                    { RoutedEvent = Keyboard.KeyUpEvent, Source = header });
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.True(((ContextMenu)window.FindName("GroupCard")).IsOpen);
                Assert.Equal("Fresh keyboard group", ((TextBlock)window.FindName("GroupCardTitle")).Text);
                Assert.False(((ContextMenu)window.FindName("NoteCard")).IsOpen);
            }
            finally { ((ContextMenu)window.FindName("GroupCard")).IsOpen = false; ((ContextMenu)window.FindName("NoteCard")).IsOpen = false; }
        });

        [Fact]
        public void OpenManagerTracksOrdinaryNoteEditsWithoutLosingSelection() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            MarkdownAuthoringTests.Ready(window);
            ((Button)window.FindName("NewNoteBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            long original = host.Notes().First().Id;
            int before = host.Notes().Count;
            var manager = OpenManager(window);
            var table = (ListBox)manager.FindName("Table");
            void Pump() => window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            try
            {
                table.SelectedItem = table.Items.OfType<NoteRowItem>().Single(note => note.Id == original);
                ((Button)window.FindName("NewNoteBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump();
                Assert.Equal(before + 1, table.Items.Count);
                long created = host.Notes().Single(note => note.Id != original).Id;
                ((TextBox)window.FindName("TitleBox")).Text = "Renamed while manager stays open";
                host.OpenNote(original);
                Pump();
                Assert.Equal("Renamed while manager stays open", table.Items.OfType<NoteRowItem>().Single(note => note.Id == created).Title);
                Assert.Equal(original, Assert.Single(table.SelectedItems.OfType<NoteRowItem>()).Id);
                var list = (ListBox)window.FindName("NotesList");
                list.SelectedItem = list.Items.OfType<Models.Note>().Single(note => note.Id == created);
                var card = (ContextMenu)window.FindName("NoteCard");
                card.IsOpen = true;
                Pump();
                var tag = ((MenuItem)window.FindName("TagsMenu")).Items.OfType<MenuItem>().First();
                string label = ((Grid)tag.Header).Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 2).Text;
                tag.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                card.IsOpen = false;
                Pump();
                Assert.Contains(label, table.Items.OfType<NoteRowItem>().Single(note => note.Id == created).Tags);
                Assert.True(host.Undo());
                Pump();
                Assert.DoesNotContain(label, table.Items.OfType<NoteRowItem>().Single(note => note.Id == created).Tags);
            }
            finally { ((ContextMenu)window.FindName("NoteCard")).IsOpen = false; manager.CloseForStoreChange(); }
        });

        [Theory]
        [InlineData("unchanged")]
        [InlineData("different")]
        [InlineData("reselected")]
        [InlineData("daily")]
        public void TemplateCreationUndoPreservesLaterGroupRoleChoices(string scenario) => CardColorBehaviorTests.WithShell((window, host) =>
        {
            long source = NoteStore.Create("Template source");
            NoteStore.AddGroup("Later templates");
            var target = new Button();
            var owner = new Window { Content = target, ShowInTaskbar = false };
            owner.Show();
            void CloseCard(string name)
            {
                ((ContextMenu)window.FindName(name)).IsOpen = false;
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            }
            void Toggle(string group, string control)
            {
                host.ShowGroupCard(group, 0, target, false);
                ((ToggleButton)window.FindName(control)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                CloseCard("GroupCard");
            }
            bool Selected(string group, string control)
            {
                host.ShowGroupCard(group, 0, target, false);
                bool selected = ((ToggleButton)window.FindName(control)).IsChecked == true;
                CloseCard("GroupCard");
                return selected;
            }
            try
            {
                host.ShowNoteCard(new[] { source }, target, false);
                ((MenuItem)window.FindName("CardSaveTemplateItem")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                CloseCard("NoteCard");
                string createdGroup = host.Notes().Single(note => note.Id != source).Notebook;
                if (scenario is "different" or "reselected") Toggle("Later templates", "GroupCardTemplatesToggle");
                if (scenario == "reselected") Toggle(createdGroup, "GroupCardTemplatesToggle");
                if (scenario == "daily") Toggle(createdGroup, "GroupCardDailyToggle");
                Assert.True(host.Undo());
                Assert.Equal(source, Assert.Single(host.Notes()).Id);
                Assert.Equal(scenario == "different", Selected("Later templates", "GroupCardTemplatesToggle"));
                if (scenario is "reselected" or "daily")
                {
                    Assert.Contains(host.Groups(), group => group.Path == createdGroup);
                    Assert.True(Selected(createdGroup, scenario == "daily" ? "GroupCardDailyToggle" : "GroupCardTemplatesToggle"));
                }
            }
            finally { CloseCard("NoteCard"); CloseCard("GroupCard"); owner.Close(); }
        });

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void MultiNoteCardDeleteConfirmsTheWholeSelection(bool managerCard, bool confirm) => CardColorBehaviorTests.WithShell((window, host) =>
        {
            long first = NoteStore.Create("First selected note");
            long second = NoteStore.Create("Second selected note");
            long untouched = NoteStore.Create("Unselected control note");
            var list = (ListBox)window.FindName("NotesList");
            list.ItemsSource = host.Notes();
            foreach (var note in list.Items.Cast<Models.Note>().Where(note => note.Id != untouched)) list.SelectedItems.Add(note);
            var target = new Button();
            var owner = new Window { Content = target, ShowInTaskbar = false };
            owner.Show();
            bool prompted = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<ConfirmDialog>().FirstOrDefault();
                if (dialog == null) return;
                timer.Stop();
                prompted = true;
                Assert.Contains("2", ((TextBlock)dialog.FindName("HeadingText")).Text);
                if (confirm) ((Button)dialog.FindName("OkButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else dialog.Close();
            };
            try
            {
                if (managerCard) host.ShowNoteCard(new[] { first, second }, target, false);
                timer.Start();
                ((MenuItem)window.FindName("CardDeleteItem")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.True(prompted, "Deleting a multi-selection must show one confirmation.");
                Assert.Equal(confirm ? 1 : 3, host.Notes().Count);
                Assert.Contains(host.Notes(), note => note.Id == untouched);
                if (confirm)
                {
                    Assert.True(host.Undo());
                    Assert.Equal(new[] { first, second, untouched }, host.Notes().Select(note => note.Id).OrderBy(id => id));
                }
            }
            finally { timer.Stop(); ((ContextMenu)window.FindName("NoteCard")).IsOpen = false; owner.Close(); }
        });

        [Fact]
        public void ManagerNoteRowsExposeTheirDisplayedTitlesToAutomation() => CardColorBehaviorTests.WithShell((window, host) =>
        {
            NoteStore.Create("First accessible note");
            NoteStore.Create("Second accessible note");
            var manager = OpenManager(window);
            try
            {
                var table = (ListBox)manager.FindName("Table");
                manager.UpdateLayout();
                var newGroup = (Button)manager.FindName("NewGroupBtn");
                Assert.Equal(manager.FindResource("Str_Mgr_NewGroup"), UIElementAutomationPeer.CreatePeerForElement(newGroup).GetName());
                Assert.Equal(2, table.Items.Count);
                foreach (NoteRowItem note in table.Items)
                {
                    table.ScrollIntoView(note);
                    manager.UpdateLayout();
                    var row = (ListBoxItem)table.ItemContainerGenerator.ContainerFromItem(note);
                    Assert.Equal(note.Title, UIElementAutomationPeer.CreatePeerForElement(row).GetName());
                }
            }
            finally { manager.CloseForStoreChange(); }
        });
        private static GroupManagerWindow OpenManager(KillerNotes.Shell.MainWindow window)
        {
            var existing = Application.Current.Windows.OfType<GroupManagerWindow>().ToArray();
            ((Button)window.FindName("OrganizeRailBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return Application.Current.Windows.OfType<GroupManagerWindow>().Single(manager => !existing.Contains(manager));
        }
    }
}
