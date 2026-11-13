using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Controls;
using KillerNotes.Models;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The Groups window (GroupManagerWindow). Its shadow was cut off square along the bottom
    /// because the dialogs' 10 px halo is smaller than the pane shadow's reach, it opened wider
    /// than it needed to with a 270 px tree, it repeated the group in a column next to the tree
    /// that already names it, and right-click did nothing.
    /// </summary>
    [Collection("Application resources")]
    public sealed class GroupManagerWindowTests
    {
        [Fact]
        public void LanguageChangesRefreshManagerLabelsAndKeepItsView() => WithDark(() =>
        {
            var win = new GroupManagerWindow(new FakeHost());
            var (host, _) = Host(win);
            win.Reload("Killer Apps");
            host.UpdateLayout();
            var tree = (TreeView)win.FindName("Tree");
            var search = (TextBox)win.FindName("SearchBox");
            search.Text = "Release";
            var previous = LocaleManager.Current;
            try
            {
                LocaleManager.Apply(Locale.De);
                host.UpdateLayout();
                Assert.Equal("Alle Notizen", tree.Items.Cast<GroupNode>().First().Name);
                Assert.StartsWith("Titel", ((Button)win.FindName("SortTitleBtn")).Content.ToString());
                Assert.Equal("Release", search.Text);
                Assert.Equal("Killer Apps", ((GroupNode)tree.SelectedItem).Path);
            }
            finally { LocaleManager.Apply(previous); win.Close(); }
        });

        [Fact]
        public void ChildrenStayUnderTheirParentWhenStoreOrderPutsThemFirst() => WithDark(() =>
        {
            var fake = new FakeHost
            {
                GroupRows = [("Work" + NoteStore.GroupSep + "Second", "Work", false, ""),
                    ("Work" + NoteStore.GroupSep + "First", "Work", false, ""),
                    ("Other", "", false, ""), ("Work", "", false, "")]
            };
            var win = new GroupManagerWindow(fake);
            win.Reload();
            var tree = (TreeView)win.FindName("Tree");
            var roots = tree.Items.Cast<GroupNode>().Where(n => n.Path.Length > 0).ToList();
            Assert.Equal(new[] { "Other", "Work" }, roots.Select(n => n.Path));
            Assert.Equal(new[] { "Second", "First" }, roots[1].Children.Select(n => n.Name));
        });

        [Fact]
        public void TheShadowFadesOutInsideTheWindow() => WithDark(() =>
        {
            var win = new GroupManagerWindow(new FakeHost());
            win.Width = 820; win.Height = 620;
            var (host, content) = Host(win);
            content.Opacity = 1;
            var bmp = new RenderTargetBitmap(820, 620, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(host);
            var px = new byte[820 * 4];
            int Alpha(int y)
            {
                bmp.CopyPixels(new Int32Rect(0, y, 820, 1), px, 820 * 4, 0);
                return px[410 * 4 + 3];
            }

            double haloBottom = ((Thickness)win.Resources["DialogHaloMargin"]).Bottom;
            int cardBottom = 620 - (int)haloBottom;
            Assert.True(Alpha(cardBottom + 4) > 20, "There is no shadow under the card, so this test proves nothing.");
            Assert.True(Alpha(619) < 6, $"The shadow is still {Alpha(619)}/255 at the window's bottom edge, so it gets cut off square.");
        });

        [Fact]
        public void TheWindowIsNarrowerAndDropsTheGroupColumnAndHint() => WithDark(() =>
        {
            var win = new GroupManagerWindow(new FakeHost());
            Assert.Equal(820, win.Width);
            Assert.Null(win.FindName("SortGroupBtn"));
            Assert.Null(win.FindName("HintText"));
            var tree = (TreeView)win.FindName("Tree");
            var columns = (Grid)((FrameworkElement)tree.Parent).Parent;
            Assert.Equal(200, columns.ColumnDefinitions[0].Width.Value);
        });

        [Fact]
        public void RightClickOpensTheSidebarsOwnCards() => WithDark(() =>
        {
            var fake = new FakeHost();
            var win = new GroupManagerWindow(fake);
            win.Width = 820; win.Height = 620;
            var (host, content) = Host(win);
            win.Reload();
            host.UpdateLayout();

            var tree = (TreeView)win.FindName("Tree");
            var row = Descendants(tree).OfType<TreeViewItem>().First(i => i.DataContext is GroupNode { Name: "Killer Apps" });
            RightClick(tree, Descendants(row).OfType<TextBlock>().First(t => t.Text == "Killer Apps"));
            Assert.Equal("Killer Apps", fake.GroupCardPath);
            Assert.Equal(2, fake.GroupCardCount);

            host.UpdateLayout();
            var table = (ListBox)win.FindName("Table");
            var rows = Descendants(table).OfType<ListBoxItem>().ToList();
            Assert.Equal(2, rows.Count);
            RightClick(table, Descendants(rows[1]).OfType<TextBlock>().First());
            Assert.Equal(new[] { ((NoteRowItem)rows[1].DataContext).Id }, fake.NoteCardIds);

            // Inside a multi-selection the card acts on all of it.
            rows[0].IsSelected = true; rows[1].IsSelected = true;
            RightClick(table, Descendants(rows[0]).OfType<TextBlock>().First());
            Assert.Equal(2, fake.NoteCardIds!.Count);
        });


        private static void RightClick(UIElement handlerOwner, UIElement under)
        {
            handlerOwner.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
                { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent, Source = under });
            handlerOwner.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
                { RoutedEvent = UIElement.MouseRightButtonUpEvent, Source = under });
        }

        private sealed class FakeHost : IGroupManagerHost
        {
            public string? GroupCardPath;
            public int GroupCardCount;
            public List<long>? NoteCardIds;
            public IReadOnlyList<(string Path, string Parent, bool Collapsed, string Color)> GroupRows =
                [("App Ideas", "", false, ""), ("Killer Apps", "", false, "")];

            public IReadOnlyList<Note> Notes() =>
            [
                new Note { Id = 1, Title = "Alternativeto.net", Notebook = "Killer Apps", Modified = new DateTime(2026, 8, 23) },
                new Note { Id = 2, Title = "Release notes", Notebook = "Killer Apps", Modified = new DateTime(2026, 9, 1) },
                new Note { Id = 3, Title = "Loose", Notebook = "", Modified = new DateTime(2026, 9, 2) },
            ];

            public IReadOnlyList<(string Path, string Parent, bool Collapsed, string Color)> Groups() =>
                GroupRows;

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
            public void ShowGroupCard(string path, int count, UIElement target, bool atMouse) { GroupCardPath = path; GroupCardCount = count; }
            public void ShowNoteCard(IReadOnlyCollection<long> ids, UIElement target, bool atMouse) => NoteCardIds = [.. ids];
            public event Action? GroupsChanged { add { } remove { } }
        }

        private static void WithDark(Action body) => Sta.RunApplication(() =>
        {
            using var settings = new UiSettingsScope();
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var merged = app.Resources.MergedDictionaries;
            var saved = merged.ToList();
            merged.Clear();
            merged.Add(ThemeHarness.Create(Theme.Dark, ThemeHarness.FirstRunAccent(Theme.Dark)));
            foreach (string p in new[] { "Controls/Controls.xaml", "Strings/en-US.xaml", "Controls/AppStyles.xaml", "Controls/OverlayScrollViewer.xaml" })
                merged.Add(new ResourceDictionary { Source = new Uri("/KillerNotes;component/" + p, UriKind.Relative) });
            try { body(); }
            finally
            {
                merged.Clear();
                foreach (var d in saved) merged.Add(d);
            }
        });

        /// <summary>Lays the window's content out at its size without showing it. The window's own
        /// resources (its halo) go along, since the content resolves them from the window.</summary>
        private static (Grid Host, FrameworkElement Content) Host(Window win)
        {
            var content = (FrameworkElement)win.Content;
            win.Content = null;
            var host = new Grid { Width = win.Width, Height = win.Height };
            foreach (object key in win.Resources.Keys) host.Resources[key] = win.Resources[key];
            foreach (var dp in new DependencyProperty[] { TextElement.ForegroundProperty, TextElement.FontFamilyProperty,
                         TextElement.FontSizeProperty, TextOptions.TextFormattingModeProperty })
                host.SetValue(dp, win.GetValue(dp));
            host.Children.Add(content);
            host.Measure(new Size(win.Width, win.Height));
            host.Arrange(new Rect(0, 0, win.Width, win.Height));
            host.UpdateLayout();
            return (host, content);
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (DependencyObject d in Descendants(child)) yield return d;
            }
        }
    }
}
