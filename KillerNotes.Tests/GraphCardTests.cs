using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The graph window's two right-click cards on the family card rules (Shell/NoteCards.cs), built
    /// on a window that is never shown: every tile names its key, every row is full, an unwritten
    /// note cannot be locked, each tile runs what its old row ran, and nothing on either card runs
    /// past its edge in any language.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class GraphCardTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly string[] Locales =
        [
            "bn", "cs-CZ", "de-DE", "en-US", "es", "fr-FR", "hu-HU", "it-IT", "ja-JP", "kk-KZ",
            "nb-NO", "pl-PL", "pt-BR", "ru-RU", "sl-SI", "tr-TR", "uk-UA", "vi-VN", "zh-CN", "zh-TW",
        ];

        private static List<Button> Tiles(ContextMenu card) => KillerNotes.Shell.NoteCards.Tiles(card).ToList();

        private static Button Tile(ContextMenu card, string id) => CardTileHelper.Tile(card, id);

        private static string? Keys(Button b) =>
            System.Windows.Automation.AutomationProperties.GetAcceleratorKey(b) is { Length: > 0 } k ? k : null;

        private static void NoTextRows(ContextMenu card) =>
            Assert.DoesNotContain(card.Items.Cast<object>(), item => item is Separator || item is MenuItem { Header: string });

        [Fact]
        public void TheNodeCardIsFiveTilesThatNameTheirKeys() => WithTheme("en-US", () =>
        {
            using var store = new TempStore();
            var g = Graph(out _, out _);
            try
            {
                var card = NodeCard(g, "Alpha");
                Assert.Equal("Alpha", System.Windows.Automation.AutomationProperties.GetName((MenuItem)card.Items[0]));
                Assert.Contains("2 links", Texts(card));   // Beta and Missing
                Assert.Equal(KillerNotes.Shell.NoteCards.Width + 44, card.MaxWidth);

                Assert.Equal(new[] { "Open", "Copy link", "Links here", "Distance", "Lock in place" }, Tiles(card).Select(b => (string)b.Tag));
                Assert.Equal(new[] { "Open note (Enter)", "Copy as [[link]] (Ctrl+C)", "Show only what links here (F)", "Arrange by distance (T)" },
                             Tiles(card).Take(4).Select(b => (string)b.ToolTip));
                Assert.EndsWith("(K)", (string)Tile(card, "lock").ToolTip);
                Assert.Equal(new[] { "Enter", "Ctrl+C", "F", "T", "K" }, Tiles(card).Select(Keys));
                // Five tiles: a row of three, then two wider ones, so neither row has a gap.
                Assert.Equal(new[] { 3, 2 }, KillerNotes.Shell.NoteCards.RowSizes(Tiles(card).Count));
                Assert.True(Tile(card, "lock").IsEnabled);
                Assert.False(CardTileHelper.Checked(card, "lock"));
                NoTextRows(card);
            }
            finally { StopTimers(g); }
        });

        [Fact]
        public void AnUnwrittenNoteOffersCreateAndCannotBeLocked() => WithTheme("en-US", () =>
        {
            using var store = new TempStore();
            var g = Graph(out _, out _);
            try
            {
                var card = NodeCard(g, "Missing");
                Assert.Contains("Not written yet", Texts(card));
                var create = Tile(card, "create");
                Assert.Equal("Create this note (Enter)", create.ToolTip);
                Assert.Equal("Create", create.Tag);
                Assert.Null(KillerNotes.Shell.NoteCards.Tile(card, "open"));
                Assert.False(Tile(card, "lock").IsEnabled);
            }
            finally { StopTimers(g); }
        });

        [Fact]
        public void TheNodeCardRunsTheSameHandlersAsItsKeys() => WithTheme("en-US", () =>
        {
            using var store = new TempStore();
            var g = Graph(out var opened, out var created);
            try
            {
                CardTileHelper.Click(NodeCard(g, "Alpha"), "open");
                Assert.Equal(new[] { Of<long>(Titled(g, "Alpha"), "Id") }, opened);

                CardTileHelper.Click(NodeCard(g, "Missing"), "create");
                Assert.Equal(new[] { "Missing" }, created);

                // Links here hides what does not touch Alpha; Gamma is two links away.
                CardTileHelper.Click(NodeCard(g, "Alpha"), "linkshere");
                Assert.Equal(Visibility.Collapsed, Of<FrameworkElement>(Titled(g, "Gamma"), "Dot").Visibility);
                Assert.Equal(Visibility.Visible, Of<FrameworkElement>(Titled(g, "Beta"), "Dot").Visibility);

                // The lock tile locks, as K does, and the next card shows it on.
                CardTileHelper.Click(NodeCard(g, "Beta"), "lock");
                Assert.True(Of<bool>(Titled(g, "Beta"), "Locked"));
                Assert.True(CardTileHelper.Checked(NodeCard(g, "Beta"), "lock"));
                CardTileHelper.Click(NodeCard(g, "Beta"), "lock");
                Assert.False(Of<bool>(Titled(g, "Beta"), "Locked"));
            }
            finally { StopTimers(g); }
        });

        [Fact]
        public void TheCanvasCardIsFiveFullRowsOfTilesThatNameTheirKeys() => WithTheme("en-US", () =>
        {
            using var store = new TempStore();
            var g = Graph(out _, out _);
            try
            {
                var card = CanvasCard(g);
                Assert.Equal("Graph", System.Windows.Automation.AutomationProperties.GetName((MenuItem)card.Items[0]));
                Assert.Equal(KillerNotes.Shell.NoteCards.Width + 44, card.MaxWidth);
                Assert.Equal(new[]
                {
                    "Connection", "Circle", "Grid",
                    "Groups", "Show all", "Pin all",
                    "Labels", "Unwritten", "Colors",
                    "Visualizer", "Shape", "Next shape",
                    "Saved", "Save", "Forget arrangement",
                }, Tiles(card).Select(b => (string)b.Tag));
                Assert.Equal(new[] { "R", "C", "G", "B", "Esc", "P", "L", "H", "Y", "V", "Alt+S", "N", null, "S", "Alt+F" },
                             Tiles(card).Select(Keys));
                Assert.Equal(new[] { "By connection (R)", "In a circle (C)", "In a grid (G)", "By group (B)", "Show everything (Esc)" },
                             Tiles(card).Take(5).Select(b => (string)b.ToolTip));
                Assert.Equal("Save this arrangement... (S)", Tile(card, "save").ToolTip);
                Assert.Equal(new[] { 3, 3, 3, 3, 3 }, KillerNotes.Shell.NoteCards.RowSizes(Tiles(card).Count));

                Assert.All(new[] { "labels", "unwritten", "colors" }, id => Assert.True(CardTileHelper.Checked(card, id)));
                Assert.False(CardTileHelper.Checked(card, "visualizer"));
                // The Shape list and Next shape keep their places, dimmed, while the visualizer is off.
                Assert.False(Tile(card, "shape").IsEnabled);
                Assert.False(Tile(card, "nextshape").IsEnabled);
                // A fresh notebook has nothing saved or to forget, and still offers to save what is on screen.
                Assert.False(Tile(card, "saved").IsEnabled);
                Assert.False(Tile(card, "forget").IsEnabled);
                Assert.True(Tile(card, "save").IsEnabled);
                NoTextRows(card);
            }
            finally { StopTimers(g); }
        });

        [Fact]
        public void TheShapeListAndNextShapeWakeUpWhileTheVisualizerRuns() => WithTheme("en-US", () =>
        {
            using var store = new TempStore();
            var g = Graph(out _, out _);
            try
            {
                // The flag alone: SetVisualizer would start the frame clock, which this test has no use for.
                SetField(g, "_visualizer", true);
                SetField(g, "_shape", 2);
                var card = CanvasCard(g);
                Assert.True(CardTileHelper.Checked(card, "visualizer"));
                Assert.True(Tile(card, "shape").IsEnabled);
                Assert.Equal("Alt+S", Keys(Tile(card, "shape")));

                var shapes = CardTileHelper.Items(card, "shape");
                Assert.Equal(6, shapes.Count);
                Assert.Equal(Enumerable.Range(1, 6).Select(i => "Alt+" + i), shapes.Select(r => r.InputGestureText));
                Assert.Equal(2, shapes.FindIndex(r => r.IsChecked));
                Assert.All(shapes, r => Assert.False(string.IsNullOrWhiteSpace(r.Header as string)));

                var next = Tile(card, "nextshape");
                Assert.True(next.IsEnabled);
                CardTileHelper.Click(next);
                Assert.Equal(3, Of<int>(g, "_shape"));

                CardTileHelper.Items(CanvasCard(g), "shape")[5].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal(5, Of<int>(g, "_shape"));
            }
            finally { SetField(g, "_visualizer", false); StopTimers(g); }
        });

        [Fact]
        public void AltSOpensTheShapeListOnTheCard() => WithTheme("en-US", () =>
        {
            using var store = new TempStore();
            var g = Graph(out _, out _);
            Window? host = null;
            try
            {
                SetField(g, "_visualizer", true);
                var card = CanvasCard(g);
                var rows = CardTileHelper.Rows(card, "shape");
                // A flyout opens beside a card that is up, so the card is opened on a host first.
                host = new Window { Content = new Border(), Width = 200, Height = 120, Left = 0, Top = 0,
                    ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
                host.Show();
                card.PlacementTarget = (FrameworkElement)host.Content;
                card.Placement = PlacementMode.Relative;
                card.IsOpen = true;
                card.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                // What Alt+S hands the card once it is up.
                typeof(GraphWindow).GetMethod("OpenTile", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { card, "shape" });
                card.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                var flyout = Assert.IsType<ContextMenu>(((FrameworkElement)rows.First()).Parent);
                Assert.True(flyout.IsOpen);
                flyout.IsOpen = false;
                card.IsOpen = false;
            }
            finally { SetField(g, "_visualizer", false); StopTimers(g); host?.Close(); }
        });

        [Fact]
        public void TheCanvasCardRunsTheSameHandlersAsItsKeys() => WithTheme("en-US", () =>
        {
            using var store = new TempStore();
            var g = Graph(out _, out _);
            try
            {
                // Labels: the tile flips the same state L does, and the labels follow it.
                CardTileHelper.Click(CanvasCard(g), "labels");
                Assert.False(((MenuItem)Field(g, "_miLabels")).IsChecked);
                Assert.Equal(Visibility.Collapsed, Of<TextBlock>(Titled(g, "Alpha"), "Label").Visibility);
                Assert.False(CardTileHelper.Checked(CanvasCard(g), "labels"));

                CardTileHelper.Click(CanvasCard(g), "unwritten");
                Assert.Equal(Visibility.Collapsed, Of<FrameworkElement>(Titled(g, "Missing"), "Dot").Visibility);

                CardTileHelper.Click(CanvasCard(g), "colors");
                Assert.False(((MenuItem)Field(g, "_miColor")).IsChecked);

                bool held = (bool)Field(g, "_hold");
                CardTileHelper.Click(CanvasCard(g), "pinall");
                Assert.Equal(!held, (bool)Field(g, "_hold"));
                Assert.Equal(!held, CardTileHelper.Checked(CanvasCard(g), "pinall"));

                // Show all undoes Links here, as Esc does.
                SetField(g, "_menuNode", Titled(g, "Alpha"));
                Invoke(g, "IsolateMenuNode");
                CardTileHelper.Click(CanvasCard(g), "showall");
                Assert.All(Nodes(g).Where(n => !Of<bool>(n, "Ghost")), n => Assert.Equal(Visibility.Visible, Of<FrameworkElement>(n, "Dot").Visibility));

                // An arrangement eases the nodes to where it puts them.
                CardTileHelper.Click(CanvasCard(g), "grid");
                Assert.True(((DispatcherTimer)Field(g, "_ease")).IsEnabled);
            }
            finally { StopTimers(g); }
        });

        [Fact]
        public void SavedArrangementsSitInTheSavedAndForgetLists() => WithTheme("en-US", () =>
        {
            using var store = new TempStore();
            var g = GraphWithIds(out var ids);
            try
            {
                NoteStore.SaveGraphLayout("Desk", ids.Select((id, i) => new NoteStore.GraphNodePos(id, 100 + i * 50, 100, false)));
                var card = CanvasCard(g);
                Assert.True(Tile(card, "saved").IsEnabled);
                Assert.True(Tile(card, "forget").IsEnabled);

                var saved = Assert.Single(CardTileHelper.Items(card, "saved"));
                Assert.Equal("Desk", saved.Header);
                Assert.StartsWith("Alt+A", saved.InputGestureText);
                var forget = Assert.Single(CardTileHelper.Items(card, "forget"));
                Assert.Equal("Desk", forget.Header);
                Assert.StartsWith("Alt+F", forget.InputGestureText);

                // Picking a saved arrangement puts every note where it was saved, then fits the
                // lot to the window (the ease that follows starts from those saved places).
                CardTileHelper.Click(CanvasCard(g), "grid");
                saved.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var placed = Nodes(g).Where(n => !Of<bool>(n, "Ghost")).ToDictionary(n => Of<long>(n, "Id"));
                for (int i = 0; i < ids.Count; i++)
                {
                    Assert.Equal(100 + i * 50, Of<double>(placed[ids[i]], "FromX"), 1);
                    Assert.Equal(100, Of<double>(placed[ids[i]], "FromY"), 1);
                }
                NoTextRows(card);
            }
            finally { StopTimers(g); }
        });

        public static TheoryData<string> AllLocales()
        {
            var data = new TheoryData<string>();
            foreach (string l in Locales) data.Add(l);
            return data;
        }

        // Laid out in every language, with the visualizer on so every tile is awake, no tile may
        // end past its row, and no row label may be cut short by the label column.
        [Theory]
        [MemberData(nameof(AllLocales))]
        public void NothingOnEitherCardRunsPastItsEdge(string locale) => WithTheme(locale, () =>
        {
            using var store = new TempStore();
            var g = Graph(out _, out _);
            try
            {
                SetField(g, "_visualizer", true);
                var faults = new List<string>();
                foreach (var card in new[] { NodeCard(g, "Alpha"), NodeCard(g, "Missing"), CanvasCard(g) })
                {
                    Lay(card);
                    foreach (var control in Find<Control>(card).Where(c => c is ButtonBase))
                    {
                        var slot = Ancestor<MenuItem>(control);
                        Rect box = control.TransformToAncestor(slot).TransformBounds(new Rect(control.RenderSize));
                        if (box.Right > slot.ActualWidth + 0.5)
                            faults.Add($"\"{(control as ContentControl)?.Content ?? control.Tag}\" ends at {box.Right:0.#} on a {slot.ActualWidth:0.#} row");
                    }
                    foreach (var label in Find<TextBlock>(card).Where(t => ReferenceEquals(t.Style, Application.Current.TryFindResource("CardLabel"))))
                    {
                        var ft = new FormattedText(label.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                            new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch), label.FontSize, Brushes.Black, 1.0);
                        if (ft.WidthIncludingTrailingWhitespace > label.Width)
                            faults.Add($"the label \"{label.Text}\" needs {ft.WidthIncludingTrailingWhitespace:0.#} of {label.Width}");
                    }
                }
                Assert.True(faults.Count == 0, locale + ": " + string.Join("; ", faults));
            }
            finally { SetField(g, "_visualizer", false); StopTimers(g); }
        });

        [Fact]
        public void EveryGraphCardStringIsInEveryLanguage()
        {
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            string source = File.ReadAllText(Path.Combine(Root(), "Controls", "GraphWindow.cs"));
            var used = System.Text.RegularExpressions.Regex.Matches(source, "\"(Str_GraphCard_[A-Za-z]+)\"")
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.NotEmpty(used);
            foreach (string locale in Locales)
            {
                var keys = XDocument.Load(Path.Combine(Root(), "Strings", locale + ".xaml")).Root!.Elements()
                    .Select(e => (string?)e.Attribute(x + "Key")).OfType<string>().ToHashSet();
                Assert.All(used, key => Assert.True(keys.Contains(key), $"{locale} has no {key}."));
            }
        }

        // ---- harness ----

        /// <summary>Alpha links to Beta and to Missing, a note nobody has written; Beta links to Gamma.</summary>
        private static GraphWindow Graph(out List<long> opened, out List<string> created)
        {
            long a = NoteStore.Create("Alpha");
            long b = NoteStore.Create("Beta");
            long c = NoteStore.Create("Gamma");
            NoteStore.SetLinks(a, WikiLinks.Parse("[[Beta]] [[Missing]]"));
            NoteStore.SetLinks(b, WikiLinks.Parse("[[Gamma]]"));
            var o = opened = [];
            var n = created = [];
            var graph = new GraphWindow(o.Add, n.Add, -1, "Graph");
            graph.Measure(new Size(1000, 700));
            graph.Arrange(new Rect(0, 0, 1000, 700));
            graph.UpdateLayout();
            Invoke(graph, "Seed");
            return graph;
        }

        private static GraphWindow GraphWithIds(out List<long> ids)
        {
            var g = Graph(out _, out _);
            ids = [.. Nodes(g).Where(n => !Of<bool>(n, "Ghost")).Select(n => Of<long>(n, "Id"))];
            return g;
        }

        private static ContextMenu NodeCard(GraphWindow g, string title)
        {
            var node = Titled(g, title);
            SetField(g, "_menuNode", node);
            Invoke(g, "ClearSelection");
            return (ContextMenu)Invoke(g, "BuildNodeCard", node)!;
        }

        private static ContextMenu CanvasCard(GraphWindow g) => (ContextMenu)Invoke(g, "BuildCanvasCard")!;

        /// <summary>Lays a card out on its own. A ContextMenu refuses any parent but its popup, and
        /// opening the popup would put a window on screen, so the card is measured as a root.</summary>
        private static void Lay(ContextMenu card)
        {
            card.Measure(new Size(card.MaxWidth, double.PositiveInfinity));
            card.Arrange(new Rect(card.DesiredSize));
            card.UpdateLayout();
        }

        private static IEnumerable<T> Find<T>(DependencyObject root) where T : class
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is not DependencyObject d) continue;
                if (d is T t) yield return t;
                foreach (var below in Find<T>(d)) yield return below;
            }
        }

        private static List<string> Texts(ContextMenu card) => [.. Find<TextBlock>(card).Select(t => t.Text)];

        private static T Ancestor<T>(DependencyObject from) where T : DependencyObject
        {
            for (DependencyObject? d = VisualTreeHelper.GetParent(from); d != null; d = VisualTreeHelper.GetParent(d))
                if (d is T t) return t;
            throw new InvalidOperationException("No " + typeof(T).Name + " above " + from);
        }

        private static object Field(GraphWindow g, string name) =>
            (typeof(GraphWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).GetValue(g)!;

        private static void SetField(GraphWindow g, string name, object? value) =>
            (typeof(GraphWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).SetValue(g, value);

        private static object? Invoke(GraphWindow g, string name, params object[] args) =>
            (typeof(GraphWindow).GetMethod(name, Members) ?? throw new MissingMethodException(name)).Invoke(g, args);

        private static List<object> Nodes(GraphWindow g) => ((IEnumerable)Field(g, "_nodes")).Cast<object>().ToList();

        private static T Of<T>(object owner, string field) =>
            (T)(owner.GetType().GetField(field, Members) ?? throw new MissingFieldException(field)).GetValue(owner)!;

        private static object Titled(GraphWindow g, string title) => Nodes(g).Single(n => Of<string>(n, "Title") == title);

        private static void StopTimers(GraphWindow g)
        {
            ((DispatcherTimer)Field(g, "_ease")).Stop();
            ((DispatcherTimer)Field(g, "_resizeSettle")).Stop();
            ((DispatcherTimer)Field(g, "_legendFade")).Stop();
        }

        private static string Root()
        {
            for (var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "KillerNotes.csproj"))) return d.FullName;
            throw new InvalidOperationException("Cannot locate the application sources");
        }

        /// <summary>The Dark theme and one language's strings, the way the app merges them, put back
        /// afterwards. The cards resolve CardMenu and its row styles from Controls.xaml.</summary>
        internal static void WithTheme(string language, Action body) => Sta.RunApplication(() =>
        {
            ResourceDictionary theme = ThemeHarness.Create(Theme.Dark, ThemeHarness.FirstRunAccent(Theme.Dark));
            var merged = Application.Current.Resources.MergedDictionaries;
            var saved = merged.ToList();
            merged.Clear();
            merged.Add(theme);
            foreach (string p in new[] { "Controls/Controls.xaml", $"Strings/{language}.xaml", "Controls/AppStyles.xaml", "Controls/OverlayScrollViewer.xaml" })
                merged.Add(new ResourceDictionary { Source = new Uri("/KillerNotes;component/" + p, UriKind.Relative) });
            try { body(); }
            finally
            {
                merged.Clear();
                foreach (var d in saved) merged.Add(d);
            }
        });
    }
}
