using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class ManagerReleaseRegressionTests
    {
        [Fact]
        public void ObservedOutsideReleaseCannotBecomeALaterDrag()
        {
            var gate = new DragGate<object>();
            gate.Press(new object(), new Point(0, 0));
            Assert.Null(gate.TryTake(new Point(50, 50), pressed: false));
            Assert.Null(gate.TryTake(new Point(50, 50), pressed: true));
        }

        [Fact]
        public void PressingSelectedRowPreservesTheSelectionUntilRelease() => WithManager((win, host) =>
        {
            var table = (ListBox)win.FindName("Table");
            var first = (ListBoxItem)table.ItemContainerGenerator.ContainerFromIndex(0);
            var second = (ListBoxItem)table.ItemContainerGenerator.ContainerFromIndex(1);
            first.IsSelected = second.IsSelected = true;
            var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = first };
            table.RaiseEvent(press);
            Assert.True(press.Handled);
            Assert.Equal(2, table.SelectedItems.Count);
            first.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = Mouse.MouseUpEvent });
            Assert.Single(table.SelectedItems);
            Assert.Same(first.DataContext, table.SelectedItem);
        });

        [Fact]
        public void RenameSelectionKeepsExpandedBranch() => WithManager((win, host) =>
        {
            win.Reload("Work");
            var tree = (TreeView)win.FindName("Tree");
            ((GroupNode)tree.SelectedItem).IsExpanded = true;
            host.GroupRows = [("Job", "", false, ""), (NoteStore.GroupPath("Job", "Child"), "Job", false, "")];
            win.Reload("Job");
            var selected = (GroupNode)tree.SelectedItem;
            Assert.Equal("Job", selected.Path);
            Assert.True(selected.IsExpanded);
        });

        [Fact]
        public void ThemeSwitchRefreshesUncoloredGroupGlyphs() => WithManager((win, host) =>
        {
            var previous = ThemeManager.Current;
            try
            {
                ThemeManager.Apply(Theme.Light);
                var tree = (TreeView)win.FindName("Tree");
                var group = tree.Items.OfType<GroupNode>().Single(n => n.Path == "Work");
                Assert.Same(win.FindResource("PrimaryBrush"), group.GlyphBrush);
            }
            finally { ThemeManager.Apply(previous); }
        });

        [Fact]
        public void KeyboardCardAnchorsToHeaderOfExpandedGroup() => WithManager((win, host) =>
        {
            win.Reload("Work");
            var tree = (TreeView)win.FindName("Tree");
            var row = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(tree.SelectedItem);
            IntPtr handle = new WindowInteropHelper(win).EnsureHandle();
            row.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, HwndSource.FromHwnd(handle), 0, Key.Apps)
                { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.NotNull(host.GroupCardTarget);
            Assert.NotSame(row, host.GroupCardTarget);
            Assert.Same(row.Template.FindName("PART_Header", row), host.GroupCardTarget);
        });

        private static void WithManager(Action<GroupManagerWindow, FakeHost> body) => Sta.RunApplication(() =>
        {
            using var settings = new UiSettingsScope();
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var dictionaries = app.Resources.MergedDictionaries;
            var saved = dictionaries.ToList();
            dictionaries.Clear();
            dictionaries.Add(ThemeHarness.Create(Theme.Dark, ThemeHarness.FirstRunAccent(Theme.Dark)));
            foreach (string path in new[] { "Controls/Controls.xaml", "Strings/en-US.xaml", "Controls/AppStyles.xaml", "Controls/OverlayScrollViewer.xaml" })
                dictionaries.Add(new ResourceDictionary { Source = new Uri("/KillerNotes;component/" + path, UriKind.Relative) });
            var fake = new FakeHost();
            var win = new GroupManagerWindow(fake);
            try
            {
                win.Reload();
                var content = (FrameworkElement)win.Content;
                content.Measure(new Size(820, 620));
                content.Arrange(new Rect(0, 0, 820, 620));
                content.UpdateLayout();
                body(win, fake);
            }
            finally
            {
                win.Close();
                dictionaries.Clear();
                foreach (var resource in saved) dictionaries.Add(resource);
            }
        });

        private sealed class FakeHost : IGroupManagerHost
        {
            public UIElement? GroupCardTarget;
            public IReadOnlyList<(string Path, string Parent, bool Collapsed, string Color)> GroupRows =
                [("Work", "", false, ""), (NoteStore.GroupPath("Work", "Child"), "Work", false, "")];
            public IReadOnlyList<Note> Notes() => [new Note { Id = 1, Title = "First" }, new Note { Id = 2, Title = "Second" }];
            public IReadOnlyList<(string Path, string Parent, bool Collapsed, string Color)> Groups() => GroupRows;
            public bool ReadOnly => false;
            public void MoveNotes(IReadOnlyCollection<long> ids, string group) { }
            public string? MoveGroup(string path, string newParent) => null;
            public void RenameGroup(string path) { }
            public void DeleteGroup(string path) { }
            public void SetGroupColor(string path, string hex) { }
            public void NewGroup(string parent) { }
            public void OpenNote(long id) { }
            public bool Undo() => false;
            public string Loc(string key) => LocaleManager.Loc(key);
            public void ShowGroupCard(string path, int count, UIElement target, bool atMouse) => GroupCardTarget = target;
            public void ShowNoteCard(IReadOnlyCollection<long> ids, UIElement target, bool atMouse) { }
            public event Action? GroupsChanged { add { } remove { } }
        }
    }
}
