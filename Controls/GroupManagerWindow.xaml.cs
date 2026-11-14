using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using KillerNotes.Models;
using KillerNotes.Services;

namespace KillerNotes.Controls
{
    /// <summary>What the group manager asks of the main window. Every change goes through the
    /// sidebar's own code there (Shell/GroupManager.cs), so this window keeps no store logic.</summary>
    internal interface IGroupManagerHost
    {
        IReadOnlyList<Note> Notes();
        IReadOnlyList<(string Path, string Parent, bool Collapsed, string Color)> Groups();
        bool ReadOnly { get; }
        void MoveNotes(IReadOnlyCollection<long> ids, string group);
        string? MoveGroup(string path, string newParent);
        void RenameGroup(string path);
        void DeleteGroup(string path);
        void SetGroupColor(string path, string hex);
        void NewGroup(string parent);
        void OpenNote(long id);
        bool Undo();
        string Loc(string key);
        void ShowGroupCard(string path, int count, UIElement target, bool atMouse);
        void ShowNoteCard(IReadOnlyCollection<long> ids, UIElement target, bool atMouse);
        event Action? GroupsChanged;
    }

    /// <summary>A row of the manager's group tree. The two roots ("All notes", "No group")
    /// have Kind set; real groups have a Path.</summary>
    public sealed class GroupNode : INotifyPropertyChanged
    {
        internal enum NodeKind { Group, AllNotes, NoGroup }
        internal NodeKind Kind { get; init; }
        public string Path { get; init; } = "";
        public string Name { get; init; } = "";
        public int Count { get; set; }
        public string CountDisplay => Count > 0 ? Count.ToString(CultureInfo.CurrentCulture) : "";
        public string Glyph { get; init; } = "\uE8B7";
        public Brush? GlyphBrush { get; init; }
        public ObservableCollection<GroupNode> Children { get; } = [];

        private bool _expanded, _selected;
        public bool IsExpanded { get => _expanded; set { _expanded = value; Changed(nameof(IsExpanded)); } }
        public bool IsSelected { get => _selected; set { _selected = value; Changed(nameof(IsSelected)); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>A row of the manager's note table.</summary>
    public sealed class NoteRowItem
    {
        public long Id { get; init; }
        public string Title { get; init; } = "";
        public DateTime Modified { get; init; }
        public string ModifiedDisplay => Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        public string Tags { get; init; } = "";
        public Brush ColorBrush { get; init; } = Brushes.Transparent;
    }

    public partial class GroupManagerWindow : Window
    {
        private const string NotesFormat = "KillerNotes.ManagerNotes";
        private const string GroupFormat = "KillerNotes.ManagerGroup";
        // Renamed from "GroupManagerBounds" when the default narrowed from 920 to 820 and the halo
        // grew to 20: a size saved under the old key carried the old width and the old halo.
        private const string BoundsSetting = "GroupManagerWindowBounds";

        private readonly IGroupManagerHost _host;
        private readonly ObservableCollection<GroupNode> _roots = [];
        private List<Note> _notes = [];
        private string _sortKey = "title";
        private bool _sortAscending = true;
        private bool _closeFaded;
        // The pressed row or group, disarmed by any release in the window (DragGate.cs): a fast
        // click must not leave a press armed for the next click's pointer move to drag.
        private readonly DragGate<DependencyObject> _drag = new();
        private ListBoxItem? _deferredSelection;
        private string? _selectPath;   // the group to select after a reload (null = keep the current one)

        internal GroupManagerWindow(IGroupManagerHost host)
        {
            _host = host;
            InitializeComponent();
            Tree.ItemsSource = _roots;
            UpdateChrome();
            RestoreSavedBounds();
            _host.GroupsChanged += OnGroupsChanged;
            ThemeManager.ThemeChanged += OnThemeChanged;
            LocaleManager.LocaleChanged += OnLocaleChanged;
            Closed += (_, _) =>
            {
                _host.GroupsChanged -= OnGroupsChanged;
                ThemeManager.ThemeChanged -= OnThemeChanged;
                LocaleManager.LocaleChanged -= OnLocaleChanged;
            };
            Loaded += (_, _) => { Anim.FadeIn(RootBorder); SearchBox.Focus(); };
            DialogShortcuts.Bind(this, NewGroupBtn, "Ctrl+G", Key.G, ModifierKeys.Control, NewGroup);
            PreviewKeyDown += Window_PreviewKeyDown;
            Deactivated += (_, _) => { _drag.Release(); _deferredSelection = null; };
            AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) =>
            {
                _drag.Release();
                if (_deferredSelection is { } row)
                {
                    Table.SelectedItems.Clear();
                    row.IsSelected = true;
                }
                _deferredSelection = null;
            }), handledEventsToo: true);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (Anim.FadeOutAndClose(this, ref _closeFaded)) { e.Cancel = true; return; }
            SaveBounds();
            base.OnClosing(e);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        internal void CloseForStoreChange()
        {
            _closeFaded = true;
            Close();
        }

        /// <summary>The halo and resize grab, per theme. The card's shadow is the pane shadow
        /// (16px blur, 5px down), and WPF draws an effect out to its full blur radius, so the
        /// dialogs' 10px halo cut it off square along the bottom. 20 is what the graph and sketch
        /// pad windows use, and the bottom gets 24 because the shadow drops 5px and so reaches 21
        /// below the card. A flat theme (98SE) sits flush with no halo and a thin grab, so the
        /// grab does not swallow the caption.</summary>
        private void UpdateChrome()
        {
            bool flat = TryFindResource("UseDialogCaption") is true;
            Resources["DialogHaloMargin"] = flat ? new Thickness(0) : new Thickness(20, 20, 20, 24);
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(flat ? 4 : 24),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });
        }

        private void OnGroupsChanged() => Dispatcher.BeginInvoke(new Action(() => Reload()));
        private void OnThemeChanged() { UpdateChrome(); Reload(); }
        private void OnLocaleChanged() => Reload();

        // ---- Loading ----

        /// <summary>Rebuilds both panes from the store, keeping the selected group, the open
        /// branches and the search. selectPath picks a group to show instead.</summary>
        internal void Reload(string? selectPath = null)
        {
            if (selectPath != null) _selectPath = selectPath;
            string? keep = _selectPath ?? SelectedNode()?.Path;
            var keepKind = SelectedNode()?.Kind ?? GroupNode.NodeKind.AllNotes;
            var open = new HashSet<string>(AllNodes(_roots).Where(n => n.IsExpanded).Select(n => n.Path), StringComparer.OrdinalIgnoreCase);
            bool firstLoad = _roots.Count == 0;

            _notes = [.. _host.Notes()];
            var groups = _host.Groups();
            if (selectPath != null && SelectedNode() is { Kind: GroupNode.NodeKind.Group } previous
                && !groups.Any(g => string.Equals(g.Path, previous.Path, StringComparison.OrdinalIgnoreCase)))
            {
                open = new HashSet<string>(open.Select(path =>
                    string.Equals(path, previous.Path, StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith(previous.Path + NoteStore.GroupSep, StringComparison.OrdinalIgnoreCase)
                        ? selectPath + path[previous.Path.Length..] : path), StringComparer.OrdinalIgnoreCase);
            }
            string filter = SearchBox.Text.Trim();

            var counts = _notes.GroupBy(n => n.Notebook, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var byPath = new Dictionary<string, GroupNode>(StringComparer.OrdinalIgnoreCase);
            var tops = new List<GroupNode>();
            foreach (var (path, parent, _, color) in groups)
            {
                var node = new GroupNode
                {
                    Path = path,
                    Name = NoteStore.GroupNameOf(path),
                    Count = counts.TryGetValue(path, out int c) ? c : 0,
                    GlyphBrush = BrushOf(color) ?? TryFindResource("PrimaryBrush") as Brush,
                    IsExpanded = firstLoad || open.Contains(path),
                };
                byPath[path] = node;
            }
            foreach (var (path, parent, _, _) in groups)
            {
                var node = byPath[path];
                if (parent.Length > 0 && byPath.TryGetValue(parent, out var p)) p.Children.Add(node);
                else tops.Add(node);
            }

            _roots.Clear();
            var dim = TryFindResource("DimTextBrush") as Brush;
            _roots.Add(new GroupNode { Kind = GroupNode.NodeKind.AllNotes, Name = _host.Loc("Str_Mgr_AllNotes"),
                Count = _notes.Count, Glyph = "\uE8A9", GlyphBrush = dim });
            _roots.Add(new GroupNode { Kind = GroupNode.NodeKind.NoGroup, Name = _host.Loc("Str_Mgr_NoGroup"),
                Count = counts.TryGetValue("", out int loose) ? loose : 0, Glyph = "\uE8B7", GlyphBrush = dim });
            foreach (var top in tops)
                if (filter.Length == 0 || KeepForFilter(top, filter)) _roots.Add(top);

            // Back to the group that was showing, else the one asked for, else All notes.
            GroupNode? target = keep != null && byPath.TryGetValue(keep, out var k) && Visible(k) ? k
                : _roots.FirstOrDefault(r => r.Kind == keepKind && keepKind != GroupNode.NodeKind.Group) ?? _roots[0];
            foreach (var a in Ancestors(target, byPath)) a.IsExpanded = true;
            target.IsSelected = true;
            _selectPath = null;
            FillTable();
        }

        private bool Visible(GroupNode n) => AllNodes(_roots).Contains(n);

        private static IEnumerable<GroupNode> Ancestors(GroupNode node, Dictionary<string, GroupNode> byPath)
        {
            string parent = NoteStore.GroupParentOf(node.Path);
            while (parent.Length > 0 && byPath.TryGetValue(parent, out var p))
            {
                yield return p;
                parent = NoteStore.GroupParentOf(parent);
            }
        }

        /// <summary>Search keeps a group whose name matches, or that holds a matching note, or
        /// that has such a group under it; the branch stays open so the match shows.</summary>
        private bool KeepForFilter(GroupNode node, string filter)
        {
            var keepKids = node.Children.Where(c => KeepForFilter(c, filter)).ToList();
            node.Children.Clear();
            foreach (var c in keepKids) node.Children.Add(c);
            bool self = node.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
                || _notes.Any(n => string.Equals(n.Notebook, node.Path, StringComparison.OrdinalIgnoreCase) && Matches(n, filter));
            if (keepKids.Count > 0) node.IsExpanded = true;
            return self || keepKids.Count > 0;
        }

        private static bool Matches(Note n, string filter) =>
            n.Title.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0
            || n.Tags.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;

        private static IEnumerable<GroupNode> AllNodes(IEnumerable<GroupNode> nodes)
        {
            foreach (var n in nodes)
            {
                yield return n;
                foreach (var c in AllNodes(n.Children)) yield return c;
            }
        }

        private GroupNode? SelectedNode() => Tree.SelectedItem as GroupNode;

        /// <summary>The table: the selected group's own notes, or with a search, every match in
        /// the selected group (All notes = everywhere). Sorted by the chosen column.</summary>
        private void FillTable()
        {
            var node = SelectedNode();
            string filter = SearchBox.Text.Trim();
            IEnumerable<Note> rows = node?.Kind switch
            {
                GroupNode.NodeKind.NoGroup => _notes.Where(n => n.Notebook.Length == 0),
                GroupNode.NodeKind.Group => _notes.Where(n => string.Equals(n.Notebook, node.Path, StringComparison.OrdinalIgnoreCase)),
                _ => _notes,
            };
            if (filter.Length > 0) rows = rows.Where(n => Matches(n, filter));

            var items = rows.Select(n => new NoteRowItem
            {
                Id = n.Id,
                Title = n.Title.Length > 0 ? n.Title : _host.Loc("Str_Untitled"),
                Modified = n.Modified,
                Tags = string.Join(", ", n.Tags.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0)),
                ColorBrush = BrushOf(n.TitleColor) ?? Brushes.Transparent,
            });
            Func<NoteRowItem, object> key = _sortKey switch
            {
                "modified" => r => r.Modified,
                "tags" => r => r.Tags,
                _ => r => r.Title,
            };
            var sorted = (_sortAscending ? items.OrderBy(key, Comparer<object>.Default) : items.OrderByDescending(key, Comparer<object>.Default)).ToList();
            var keepIds = Table.SelectedItems.OfType<NoteRowItem>().Select(r => r.Id).ToHashSet();
            Table.ItemsSource = sorted;
            foreach (var r in sorted.Where(r => keepIds.Contains(r.Id))) Table.SelectedItems.Add(r);
            EmptyText.Visibility = sorted.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            MarkSortColumn();
        }

        private void MarkSortColumn()
        {
            foreach (var b in new[] { SortTitleBtn, SortModifiedBtn, SortTagsBtn })
            {
                string label = _host.Loc(b.Name switch
                {
                    nameof(SortModifiedBtn) => "Str_Mgr_ColModified",
                    nameof(SortTagsBtn) => "Str_Mgr_ColTags",
                    _ => "Str_Mgr_ColTitle",
                });
                b.Content = (string)b.Tag == _sortKey ? label + (_sortAscending ? "  \u25B4" : "  \u25BE") : label;
            }
        }

        private static Brush? BrushOf(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch (FormatException) { return null; }
        }

        // ---- Events ----

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (IsLoaded) Reload();
        }

        private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => FillTable();

        private void Sort_Click(object sender, RoutedEventArgs e)
        {
            string key = (string)((Button)sender).Tag;
            _sortAscending = key != _sortKey || !_sortAscending;
            _sortKey = key;
            FillTable();
        }

        private void NewGroup_Click(object sender, RoutedEventArgs e) => NewGroup();

        private void NewGroup()
        {
            if (_host.ReadOnly) return;
            var node = SelectedNode();
            _host.NewGroup(node?.Kind == GroupNode.NodeKind.Group ? node.Path : "");
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && !SearchBox.IsKeyboardFocusWithin)
            {
                _host.Undo();
                e.Handled = true;
            }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        }

        private void Tree_KeyDown(object sender, KeyEventArgs e)
        {
            if (IsMenuKey(e) && SelectedNode() is { Kind: GroupNode.NodeKind.Group } menuNode
                && e.OriginalSource is TreeViewItem { DataContext: GroupNode } menuRow)
            {
                menuRow.ApplyTemplate();
                var header = menuRow.Template.FindName("PART_Header", menuRow) as UIElement ?? menuRow;
                _host.ShowGroupCard(menuNode.Path, menuNode.Count, header, atMouse: false);
                e.Handled = true;
                return;
            }
            if (SelectedNode() is not { Kind: GroupNode.NodeKind.Group } node || _host.ReadOnly) return;
            if (e.Key == Key.F2) { _host.RenameGroup(node.Path); e.Handled = true; }
            else if (e.Key == Key.Delete) { _host.DeleteGroup(node.Path); e.Handled = true; }
        }

        private void Table_KeyDown(object sender, KeyEventArgs e)
        {
            if (IsMenuKey(e) && Table.SelectedItem is NoteRowItem sel
                && Table.ItemContainerGenerator.ContainerFromItem(sel) is ListBoxItem selRow)
            {
                _host.ShowNoteCard(SelectedIds(), selRow, atMouse: false);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && Table.SelectedItem is NoteRowItem row) { _host.OpenNote(row.Id); e.Handled = true; }
        }

        private void Table_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ItemUnder<ListBoxItem>(e.OriginalSource)?.DataContext is NoteRowItem row) _host.OpenNote(row.Id);
        }

        // ---- Right-click: the sidebar's own cards ----

        private static bool IsMenuKey(KeyEventArgs e) =>
            e.Key == Key.Apps || (e.Key == Key.System && e.SystemKey == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift);

        private List<long> SelectedIds() => [.. Table.SelectedItems.OfType<NoteRowItem>().Select(r => r.Id)];

        /// <summary>Right-click selects the group under the cursor first, like a left click.</summary>
        private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ItemUnder<TreeViewItem>(e.OriginalSource) is TreeViewItem { DataContext: GroupNode node } row)
            {
                node.IsSelected = true;
                row.Focus();
            }
        }

        private void Tree_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ItemUnder<TreeViewItem>(e.OriginalSource) is not TreeViewItem { DataContext: GroupNode { Kind: GroupNode.NodeKind.Group } node } row) return;
            _host.ShowGroupCard(node.Path, node.Count, row, atMouse: true);
            e.Handled = true;
        }

        /// <summary>Right-click on a row outside the selection selects just that row, the way the
        /// sidebar does; inside the selection it keeps all of them, so the card acts on the lot.</summary>
        private void Table_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ItemUnder<ListBoxItem>(e.OriginalSource) is ListBoxItem { IsSelected: false } row)
            {
                Table.SelectedItems.Clear();
                row.IsSelected = true;
                row.Focus();
            }
        }

        private void Table_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ItemUnder<ListBoxItem>(e.OriginalSource) is not ListBoxItem row) return;
            var ids = SelectedIds();
            if (ids.Count == 0) return;
            _host.ShowNoteCard(ids, row, atMouse: true);
            e.Handled = true;
        }

        // ---- Drag and drop ----

        private void Table_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = ItemUnder<ListBoxItem>(e.OriginalSource);
            _deferredSelection = row is { IsSelected: true } && Table.SelectedItems.Count > 1
                && Keyboard.Modifiers == ModifierKeys.None ? row : null;
            if (_deferredSelection != null) e.Handled = true;
            _drag.Press(row, e.GetPosition(this));
        }

        private void Table_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_host.ReadOnly || _drag.TryTake(e.GetPosition(this), e.LeftButton == MouseButtonState.Pressed) is not ListBoxItem) return;
            _deferredSelection = null;
            var ids = Table.SelectedItems.OfType<NoteRowItem>().Select(r => r.Id).ToList();
            if (ids.Count == 0) return;
            DragDrop.DoDragDrop(Table, new DataObject(NotesFormat, ids), DragDropEffects.Move);
        }

        private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            _drag.Press(ItemUnder<TreeViewItem>(e.OriginalSource) is { DataContext: GroupNode { Kind: GroupNode.NodeKind.Group } } row ? row : null,
                e.GetPosition(this));

        // Drags the group that was PRESSED, not whatever row the pointer has reached by the time
        // it passes the threshold.
        private void Tree_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_host.ReadOnly || _drag.TryTake(e.GetPosition(this), e.LeftButton == MouseButtonState.Pressed)
                    is not TreeViewItem { DataContext: GroupNode { Kind: GroupNode.NodeKind.Group } node }) return;
            DragDrop.DoDragDrop(Tree, new DataObject(GroupFormat, node.Path), DragDropEffects.Move);
        }

        private void Tree_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DropTarget(e) != null ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void Tree_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (DropTarget(e) is not string target) return;
            if (e.Data.GetData(NotesFormat) is List<long> ids) _host.MoveNotes(ids, target);
            else if (e.Data.GetData(GroupFormat) is string path && _host.MoveGroup(path, target) is string moved)
                Reload(moved);
        }

        /// <summary>Where a drop would land, as a group path ("" = no group / the top level), or
        /// null when it cannot land there: notes on All notes, a group into itself or its own
        /// branch, or a group onto the parent it already has.</summary>
        private string? DropTarget(DragEventArgs e)
        {
            if (ItemUnder<TreeViewItem>(e.OriginalSource)?.DataContext is not GroupNode node) return null;
            if (e.Data.GetDataPresent(NotesFormat))
                return node.Kind == GroupNode.NodeKind.AllNotes ? null : node.Path;
            if (e.Data.GetData(GroupFormat) is not string path) return null;
            string target = node.Kind == GroupNode.NodeKind.Group ? node.Path : "";
            if (string.Equals(target, path, StringComparison.OrdinalIgnoreCase)) return null;
            if (target.StartsWith(path + NoteStore.GroupSep, StringComparison.OrdinalIgnoreCase)) return null;
            if (string.Equals(target, NoteStore.GroupParentOf(path), StringComparison.OrdinalIgnoreCase)) return null;
            return target;
        }

        private static T? ItemUnder<T>(object source) where T : DependencyObject
        {
            for (var d = source as DependencyObject; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D
                     ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is T t) return t;
            return null;
        }

        // ---- Bounds ----

        private void RestoreSavedBounds()
        {
            string? s = App.GetSetting(BoundsSetting);
            if (string.IsNullOrWhiteSpace(s)) return;
            var f = s!.Split(',');
            if (f.Length != 4) return;
            var v = new double[4];
            for (int i = 0; i < 4; i++)
                if (!double.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return;
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                  SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (!screen.IntersectsWith(new Rect(v[0], v[1], Math.Max(1, v[2]), 40))) return;   // keep the caption reachable
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = v[0]; Top = v[1];
            Width = Math.Max(MinWidth, v[2]); Height = Math.Max(MinHeight, v[3]);
        }

        private void SaveBounds()
        {
            var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (r.IsEmpty || double.IsNaN(r.X)) return;
            App.SetSetting(BoundsSetting, string.Join(",", new[] { r.X, r.Y, r.Width, r.Height }
                .Select(x => x.ToString("0.##", CultureInfo.InvariantCulture))));
        }
    }
}
