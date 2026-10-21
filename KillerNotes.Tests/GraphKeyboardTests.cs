using System;
using System.Collections.Generic;
using System.Reflection;
using KillerNotes.Controls;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Every node in the graph can be reached and opened from the keyboard: Tab and Shift+Tab
    /// step through the visible nodes in title order, the current one is selected and announced
    /// by title and link count, and Enter opens it (or creates a note for a ghost).
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class GraphKeyboardTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [Fact]
        public void TabStepsThroughNodesAndEnterOpensTheCurrentOne() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long alpha = NoteStore.Create("Alpha");
            long beta = NoteStore.Create("Beta");
            NoteStore.SetLinks(alpha, WikiLinks.Parse("[[Beta]] [[Ghost note]]"));
            var opened = new List<long>();
            var created = new List<string>();
            // The window loads its icon by pack URI, which needs to know the app's assembly when
            // there is no app running. Settable once per process.
            if (System.Windows.Application.ResourceAssembly == null)
                System.Windows.Application.ResourceAssembly = typeof(GraphWindow).Assembly;
            var graph = new GraphWindow(opened.Add, created.Add, -1, "Graph");

            Assert.Equal("Alpha", Step(graph, +1));
            Assert.Equal("Beta", Step(graph, +1));
            Assert.Equal("Ghost note", Step(graph, +1));
            Assert.Equal("Alpha", Step(graph, +1));            // wraps
            Assert.Equal("Ghost note", Step(graph, -1));       // and back
            Assert.Contains("Ghost note", Announced(graph));

            Step(graph, -1);                                   // Beta
            Assert.Contains("Beta", Announced(graph));
            Assert.Contains("1", Announced(graph));            // one link
            Invoke(graph, "ActivateKeyboardNode");
            Assert.Equal(new[] { beta }, opened);
        });

        private static string Step(GraphWindow graph, int direction)
        {
            Invoke(graph, "MoveKeyboardNode", direction);
            return (string)Invoke(graph, "KeyboardNodeTitle")!;
        }

        private static string Announced(GraphWindow graph) => (string)Invoke(graph, "AnnouncedText")!;

        private static object? Invoke(GraphWindow graph, string name, params object[] args) =>
            (typeof(GraphWindow).GetMethod(name, Members) ?? throw new MissingMethodException(name)).Invoke(graph, args);
    }
}
