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
    /// The graph window's two right-click cards (1.7.0), built on a window that is never shown:
    /// every control names its key, an unwritten note cannot be locked, each control runs what
    /// its old menu row ran, and nothing on either card runs past its edge in any language.
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

        [Fact]
        public void TheNodeCardNamesTheNoteAndEveryKey() => WithTheme("en-US", () =>
        {
            using var store = new TempStore();
            var g = Graph(out _, out _);
            try
            {
                var card = NodeCard(g, "Alpha");
                Assert.Equal("Alpha", System.Windows.Automation.AutomationProperties.GetName((MenuItem)card.Items[0]));
                Assert.Contains("Alpha", Texts(card));
                Assert.Contains("2 links", Texts(card));   // Beta and Missing

                Assert.Equal(new[] { "Open note (Enter)", "Copy as [[link]] (Ctrl+C)", "Show only what links here (F)", "Arrange by distance (T)" },
                             Find<Button>(card).Select(b => (string)b.ToolTip));
                Assert.Equal(new[] { "Open", "Copy link", "Links here", "Distance" }, Find<Button>(card).Select(b => (string)b.Tag));

                var lockChip = Find<ToggleButton>(card).Single(t => t is not RadioButton);
                Assert.Equal("Lock in place", lockChip.Content);
                Assert.EndsWith("(K)", (string)lockChip.ToolTip);
                Assert.True(lockChip.IsEnabled);
                Assert.False(lockChip.IsChecked);
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
                var create = Find<Button>(card).First();
                Assert.Equal("Create this note (Enter)", create.ToolTip);
                Assert.Equal("Create", create.Tag);
                Assert.False(Find<ToggleButton>(card).Single().IsEnabled);
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
                Click(Find<Button>(NodeCard(g, "Alpha")).First());
                Assert.Equal(new[] { Of<long>(Titled(g, "Alpha"), "Id") }, opened);

                Click(Find<Button>(NodeCard(g, "Missing")).First());
                Assert.Equal(new[] { "Missing" }, created);

                // Links here hides what does not touch Alpha; Gamma is two links away.
                Click(Find<Button>(NodeCard(g, "Alpha")).Single(b => (string)b.Tag == "Links here"));
                Assert.Equal(Visibility.Collapsed, Of<FrameworkElement>(Titled(g, "Gamma"), "Dot").Visibility);
                Assert.Equal(Visibility.Visible, Of<FrameworkElement>(Titled(g, "Beta"), "Dot").Visibility);

                // The lock chip locks, as K does, and the next card shows it on.
                Click(Find<ToggleButton>(NodeCard(g, "Beta")).Single());
                Assert.True(Of<bool>(Titled(g, "Beta"), "Locked"));
                Assert.True(Find<ToggleButton>(NodeCard(g, "Beta")).Single().IsChecked);
                Click(Find<ToggleButton>(NodeCard(g, "Beta")).Single());
                Assert.False(Of<bool>(Titled(g, "Beta"), "Locked"));
            }
            finally { StopTimers(g); }
        });

        [Fact]
        public void TheCanvasCardNamesEveryKey() => WithTheme("en-US", () =>
        {
            using var store = new TempStore();
            var g = Graph(out _, out _);
            try
            {
                var card = CanvasCard(g);
                Assert.Equal("Graph", System.Windows.Automation.AutomationProperties.GetName((MenuItem)card.Items[0]));
                Assert.Equal(new[] { "By connection (R)", "In a circle (C)", "In a grid (G)", "By group (B)", "Show everything (Esc)" },
                             Find<Button>(card).Select(b => (string)b.ToolTip));
                var strip = card.Items.OfType<MenuItem>().Single(m => System.Windows.Automation.AutomationProperties.GetName(m) == "Arrange");
                Assert.Equal("Alt+A", System.Windows.Automation.AutomationProperties.GetAcceleratorKey(strip));

                var chips = Find<ToggleButton>(card).ToList();
                Assert.Equal(new[] { "Labels", "Unwritten", "Colors", "Visualizer", "Pin all" }, chips.Select(c => (string)c.Content));
                Assert.Equal(new[] { "(L)", "(H)", "(Y)", "(V)", "(P)" }, chips.Select(c => ((string)c.ToolTip).Substring(((string)c.ToolTip).LastIndexOf('('))));
                Assert.All(chips.Take(3), c => Assert.True(c.IsChecked));

                // No Shape row and no Next shape while the visualizer is off.
                Assert.Empty(Find<RadioButton>(card));
                Assert.DoesNotContain(card.Items.OfType<MenuItem>(), m => Equals(m.Header, "Next shape"));
                // A fresh notebook still offers to save the arrangement on screen.
                var save = card.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Save this arrangement..."));
                Assert.Equal("S", save.InputGestureText);
            }
            finally { StopTimers(g); }
        });

        [Fact]
        public void TheShapeRowAndNextShapeAppearWhileTheVisualizerRuns() => WithTheme("en-US", () =>
        {
            using var store = new TempStore();
            var g = Graph(out _, out _);
            try
            {
                // The flag alone: SetVisualizer would start the frame clock, which this test has no use for.
                SetField(g, "_visualizer", true);
                SetField(g, "_shape", 2);
                var card = CanvasCard(g);
                var shapes = Find<RadioButton>(card).ToList();
                Assert.Equal(new[] { "Free", "Circle", "Grid", "Groups", "Spiral", "Wave" }, shapes.Select(r => (string)r.Content));
                Assert.Equal(Enumerable.Range(1, 6).Select(i => "(Alt+" + i + ")"),
                             shapes.Select(r => ((string)r.ToolTip).Substring(((string)r.ToolTip).LastIndexOf('('))));
                Assert.Equal(2, shapes.FindIndex(r => r.IsChecked == true));
                var row = card.Items.OfType<MenuItem>().Single(m => System.Windows.Automation.AutomationProperties.GetName(m) == "Shape");
                Assert.Equal("Alt+S", System.Windows.Automation.AutomationProperties.GetAcceleratorKey(row));
                Assert.True(Find<ToggleButton>(card).Single(t => (string)t.Content == "Visualizer").IsChecked);

                var next = card.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Next shape"));
                Assert.Equal("N", next.InputGestureText);
                next.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal(3, Of<int>(g, "_shape"));

                Click(shapes[5]);
                Assert.Equal(5, Of<int>(g, "_shape"));
            }
            finally { SetField(g, "_visualizer", false); StopTimers(g); }
        });

        [Fact]
        public void TheCanvasCardRunsTheSameHandlersAsItsKeys() => WithTheme("en-US", () =>
        {
            using var store = new TempStore();
            var g = Graph(out _, out _);
            try
            {
                // Labels: the chip flips the same state L does, and the labels follow it.
                Click(Find<ToggleButton>(CanvasCard(g)).Single(t => (string)t.Content == "Labels"));
                Assert.False(((MenuItem)Field(g, "_miLabels")).IsChecked);
                Assert.Equal(Visibility.Collapsed, Of<TextBlock>(Titled(g, "Alpha"), "Label").Visibility);
                Assert.False(Find<ToggleButton>(CanvasCard(g)).Single(t => (string)t.Content == "Labels").IsChecked);

                Click(Find<ToggleButton>(CanvasCard(g)).Single(t => (string)t.Content == "Unwritten"));
                Assert.Equal(Visibility.Collapsed, Of<FrameworkElement>(Titled(g, "Missing"), "Dot").Visibility);

                Click(Find<ToggleButton>(CanvasCard(g)).Single(t => (string)t.Content == "Colors"));
                Assert.False(((MenuItem)Field(g, "_miColor")).IsChecked);

                // Show all undoes Links here, as Esc does.
                SetField(g, "_menuNode", Titled(g, "Alpha"));
                Invoke(g, "IsolateMenuNode");
                Click(Find<Button>(CanvasCard(g)).Single(b => (string)b.Tag == "Show all"));
                Assert.All(Nodes(g).Where(n => !Of<bool>(n, "Ghost")), n => Assert.Equal(Visibility.Visible, Of<FrameworkElement>(n, "Dot").Visibility));

                // An arrangement eases the nodes to where it puts them.
                Click(Find<Button>(CanvasCard(g)).Single(b => (string)b.Tag == "Grid"));
                Assert.True(((DispatcherTimer)Field(g, "_ease")).IsEnabled);
            }
            finally { StopTimers(g); }
        });

        [Fact]
        public void SavedArrangementsAreTheShortListAtTheBottom() => WithTheme("en-US", () =>
        {
            using var store = new TempStore();
            var g = GraphWithIds(out var ids);
            try
            {
                NoteStore.SaveGraphLayout("Desk", ids.Select((id, i) => new NoteStore.GraphNodePos(id, 100 + i * 50, 100, false)));
                var card = CanvasCard(g);
                var rows = card.Items.OfType<MenuItem>().Where(m => m.Header is string).ToList();
                Assert.Equal(new[] { "Desk", "Save this arrangement...", "Forget arrangement" }, rows.Select(m => (string)m.Header));
                Assert.StartsWith("Alt+A", rows[0].InputGestureText);
                Assert.Equal("S", rows[1].InputGestureText);
                Assert.Equal("Alt+F", rows[2].InputGestureText);
                Assert.Equal("Desk", ((MenuItem)rows[2].Items[0]).Header);
                Assert.IsType<Separator>(card.Items[card.Items.IndexOf(rows[0]) - 1]);
            }
            finally { StopTimers(g); }
        });

        public static TheoryData<string> AllLocales()
        {
            var data = new TheoryData<string>();
            foreach (string l in Locales) data.Add(l);
            return data;
        }

        // A segment row and a quick strip do not wrap, so a translation that is too long runs off
        // the card's edge instead of onto a second line. Laid out in every language, with the
        // visualizer's Shape row in, no control may end past its row, and no row label may be
        // cut short by the label column.
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

        private static void Click(ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, b));

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
