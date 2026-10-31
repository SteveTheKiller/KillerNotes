using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// What the graph's arrangements, saved layouts, isolation and lock ring do to the nodes on a
    /// window that is built and never shown.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class GraphArrangeTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        /// <summary>A chain: Alpha links to Beta, Beta to Gamma, Gamma to Delta.</summary>
        private static GraphWindow Graph(out long[] ids)
        {
            long a = NoteStore.Create("Alpha");
            long b = NoteStore.Create("Beta");
            long c = NoteStore.Create("Gamma");
            long d = NoteStore.Create("Delta");
            NoteStore.SetLinks(a, WikiLinks.Parse("[[Beta]]"));
            NoteStore.SetLinks(b, WikiLinks.Parse("[[Gamma]]"));
            NoteStore.SetLinks(c, WikiLinks.Parse("[[Delta]]"));
            ids = [a, b, c, d];
            var palette = ThemeHarness.Create(Theme.Dark, ThemeHarness.FirstRunAccent(Theme.Dark));
            Application.Current.Resources.MergedDictionaries.Add(palette);
            var graph = new GraphWindow(_ => { }, _ => { }, -1, "Graph");
            graph.Measure(new Size(1000, 700));
            graph.Arrange(new Rect(0, 0, 1000, 700));
            graph.UpdateLayout();
            Invoke(graph, "Seed");
            return graph;
        }

        private static object Field(GraphWindow g, string name) =>
            (typeof(GraphWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).GetValue(g)!;

        private static void SetField(GraphWindow g, string name, object? value) =>
            (typeof(GraphWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).SetValue(g, value);

        private static object? Invoke(GraphWindow g, string name, params object[] args) =>
            (typeof(GraphWindow).GetMethod(name, Members) ?? throw new MissingMethodException(name)).Invoke(g, args);

        private static List<object> Nodes(GraphWindow g) => ((IEnumerable)Field(g, "_nodes")).Cast<object>().ToList();

        private static T Of<T>(object node, string field) => (T)node.GetType().GetField(field, Members)!.GetValue(node)!;

        private static void Put(object node, string field, object value) => node.GetType().GetField(field, Members)!.SetValue(node, value);

        private static object Titled(GraphWindow g, string title) => Nodes(g).Single(n => Of<string>(n, "Title") == title);

        private static void StopTimers(GraphWindow g)
        {
            ((DispatcherTimer)Field(g, "_ease")).Stop();
            ((DispatcherTimer)Field(g, "_resizeSettle")).Stop();
        }

        private static void Pump(Func<bool> done)
        {
            var frame = new DispatcherFrame();
            var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            var give = DateTime.UtcNow.AddSeconds(15);
            poll.Tick += (_, _) => { if (done() || DateTime.UtcNow > give) { poll.Stop(); frame.Continue = false; } };
            poll.Start();
            Dispatcher.PushFrame(frame);
        }

        [Fact]
        public void ArrangingByConnectionLeavesALockedNoteWhereItIs() => Sta.RunApplication(() =>
        {
            using var store = new TempStore();
            var g = Graph(out _);
            try
            {
                var locked = Titled(g, "Beta");
                Put(locked, "X", 333.0); Put(locked, "Y", 222.0);
                Invoke(g, "SetLock", locked, true);

                Invoke(g, "ArrangeForce");
                Pump(() => !Of<bool>(g, "_solving") && !Of<bool>(g, "_solveQueued"));

                Assert.Equal(333.0, Of<double>(locked, "ToX"), 6);
                Assert.Equal(222.0, Of<double>(locked, "ToY"), 6);
            }
            finally { StopTimers(g); }
        });

        [Fact]
        public void ChoosingASavedArrangementFitsItIntoTheWindow() => Sta.RunApplication(() =>
        {
            using var store = new TempStore();
            var g = Graph(out var ids);
            try
            {
                // Saved on a far bigger screen: these coordinates mean nothing in this window.
                NoteStore.SaveGraphLayout("Big monitor", ids.Select((id, i) => new NoteStore.GraphNodePos(id, 4000 + i * 900, 3000 + i * 500, false)));
                Invoke(g, "RefreshArrangeMenu");
                var menu = (MenuItem)Field(g, "_miArrange");
                var row = menu.Items.OfType<MenuItem>().Single(m => (string)m.Header == "Big monitor");

                row.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

                var (l, t, r, b) = ((double L, double T, double R, double B))Invoke(g, "UsableBox")!;
                foreach (var n in Nodes(g))
                {
                    Assert.InRange(Of<double>(n, "ToX"), l - 0.5, r + 0.5);
                    Assert.InRange(Of<double>(n, "ToY"), t - 0.5, b + 0.5);
                }
            }
            finally { StopTimers(g); }
        });

        [Fact]
        public void IsolatingANodeAndShowingEverythingKeepLabelsOffWhenTheyAreTurnedOff() => Sta.RunApplication(() =>
        {
            using var store = new TempStore();
            var g = Graph(out _);
            try
            {
                var labels = (MenuItem)Field(g, "_miLabels");
                labels.IsChecked = false;
                Invoke(g, "ApplyLabelVisibility");
                SetField(g, "_menuNode", Titled(g, "Alpha"));

                Invoke(g, "IsolateMenuNode");
                foreach (var n in Nodes(g)) Assert.Equal(Visibility.Collapsed, Of<TextBlock>(n, "Label").Visibility);

                Invoke(g, "ClearIsolation");
                foreach (var n in Nodes(g)) Assert.Equal(Visibility.Collapsed, Of<TextBlock>(n, "Label").Visibility);
            }
            finally { StopTimers(g); }
        });

        [Fact]
        public void IsolatingANodeShowsLabelsOnlyForWhatItKeepsWhenLabelsAreOn() => Sta.RunApplication(() =>
        {
            using var store = new TempStore();
            var g = Graph(out _);
            try
            {
                var labels = (MenuItem)Field(g, "_miLabels");
                labels.IsChecked = true;
                Invoke(g, "ApplyLabelVisibility");
                SetField(g, "_menuNode", Titled(g, "Alpha"));

                Invoke(g, "IsolateMenuNode");
                Assert.Equal(Visibility.Visible, Of<TextBlock>(Titled(g, "Alpha"), "Label").Visibility);
                Assert.Equal(Visibility.Visible, Of<TextBlock>(Titled(g, "Beta"), "Label").Visibility);
                Assert.Equal(Visibility.Collapsed, Of<TextBlock>(Titled(g, "Delta"), "Label").Visibility);

                Invoke(g, "ClearIsolation");
                foreach (var n in Nodes(g)) Assert.Equal(Visibility.Visible, Of<TextBlock>(n, "Label").Visibility);
            }
            finally { StopTimers(g); }
        });

        [Fact]
        public void ColoringByGroupKeepsTheRingOnALockedNote() => Sta.RunApplication(() =>
        {
            using var store = new TempStore();
            var g = Graph(out _);
            try
            {
                var locked = Titled(g, "Beta");
                Put(locked, "GroupBrush", System.Windows.Media.Brushes.Red);   // a group color unlike the lock ring's
                Invoke(g, "SetLock", locked, true);

                ((MenuItem)Field(g, "_miColor")).IsChecked = true;
                Invoke(g, "ApplyNodeColors");

                var dot = Of<System.Windows.Shapes.Ellipse>(locked, "Dot");
                Assert.Same(System.Windows.Media.Brushes.Red, dot.Fill);
                Assert.NotSame(dot.Fill, dot.Stroke);   // the ring is its own color, not the fill's
                Assert.Equal(2, dot.StrokeThickness);
                Assert.NotNull(dot.StrokeDashArray);
            }
            finally { StopTimers(g); }
        });
    }
}
