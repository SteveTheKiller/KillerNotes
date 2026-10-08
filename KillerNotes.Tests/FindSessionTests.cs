using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public sealed class FindSessionTests
    {
        [Fact]
        public void LargeNoteLoadDoesNotSearchEachNormalizationNotification() => Sta.Run(() =>
        {
            using var shell = new FindEditor();
            shell.Set("_loadingNote", true);
            shell.Editor.Document = Document(4000);
            int notifications = 0;
            shell.Editor.TextChanged += (_, _) =>
            {
                notifications++;
                Assert.True(shell.Stale);
                Assert.Null(shell.Pending);
                Assert.False(shell.Window.FindIsOpen);
            };
            MainWindow.NormalizeThemeColors(shell.Editor.Document);
            Assert.True(notifications >= 4000);
            shell.Set("_loadingNote", false);
            shell.Call("RefreshFindAfterNoteLoad");
            Assert.Equal(8000, shell.Window.FindHits.Count);
            Assert.Equal(0, shell.Window.FindCurrentIndex);
            Assert.False(shell.Stale);
            Assert.Null(shell.Pending);
        });

        [Fact]
        public void FormattingBurstQueuesOneScanAndKeepsTheCurrentMatch() => Sta.Run(() =>
        {
            using var shell = new FindEditor();
            shell.Editor.Document = Document(4000);
            shell.Call("RunFind", false);
            shell.Set("_findIndex", 7);
            MainWindow.NormalizeThemeColors(shell.Editor.Document);
            var pending = shell.Pending;
            Assert.NotNull(pending);
            Assert.True(shell.Stale);
            shell.Call("InvalidateFindCache");
            Assert.Same(pending, shell.Pending);
            Pump();
            Assert.Null(shell.Pending);
            Assert.False(shell.Stale);
            Assert.Equal(7, shell.Window.FindCurrentIndex);
            Assert.Equal(8000, shell.Window.FindHits.Count);
        });

        [Fact]
        public void RepeatedInterruptedLoadsNeverReuseTheOutgoingNotesHits() => Sta.Run(() =>
        {
            using var shell = new FindEditor();
            for (int pass = 0; pass < 6; pass++)
            {
                shell.Editor.AppendText(" and queued edit");
                var pending = shell.Pending;
                Assert.NotNull(pending);
                shell.Set("_loadingNote", true);
                shell.Editor.Document.Blocks.Clear();
                using var blob = new MemoryStream();
                var doc = pass % 2 == 0 ? Document(4000) : new FlowDocument(new Paragraph(new Run("unmatched")));
                new TextRange(doc.ContentStart, doc.ContentEnd).Save(blob, DataFormats.XamlPackage);
                blob.Position = 0;
                new TextRange(shell.Editor.Document.ContentStart, shell.Editor.Document.ContentEnd)
                    .Load(blob, DataFormats.XamlPackage);
                MainWindow.NormalizeThemeColors(shell.Editor.Document);
                shell.Set("_loadingNote", false);
                shell.Call("RefreshFindAfterNoteLoad");
                Assert.Equal(DispatcherOperationStatus.Aborted, pending!.Status);
                int expected = pass % 2 == 0 ? 8000 : 0;
                Assert.Equal(expected, shell.Window.FindHits.Count);
                Assert.Equal(expected == 0 ? -1 : 0, shell.Window.FindCurrentIndex);
                Pump();
                Assert.Equal(expected, shell.Window.FindHits.Count);
                Assert.False(shell.Stale);
            }
        });

        [Fact]
        public void CloseAndReopenCancelThePendingEditAndSearchTheFinalText() => Sta.Run(() =>
        {
            using var shell = new FindEditor();
            shell.Editor.AppendText("and and");
            var pending = shell.Pending;
            shell.Window.CloseFindBar();
            Assert.Equal(DispatcherOperationStatus.Aborted, pending!.Status);
            Pump();
            Assert.Empty(shell.Window.FindHits);
            Assert.False(shell.Window.FindIsOpen);
            shell.Set("_findOpen", true);
            shell.Call("RunFind", false);
            Assert.Equal(2, shell.Window.FindHits.Count);
        });

        [Fact]
        public void NavigationAndRepeatedReplacementFlushPendingEdits() => Sta.Run(() =>
        {
            using var shell = new FindEditor();
            shell.Editor.AppendText("and and and");
            shell.Call("StepFind", 1);
            Assert.Null(shell.Pending);
            Assert.Equal(1, shell.Window.FindCurrentIndex);
            shell.Set("_findIndex", 0);
            shell.Call("ReplaceCurrent_Click", shell.Window, new RoutedEventArgs());
            shell.Call("ReplaceCurrent_Click", shell.Window, new RoutedEventArgs());
            Assert.Contains("andand andand and", shell.Text);
            Assert.Equal(4, shell.Window.FindCurrentIndex);
            Assert.Null(shell.Pending);
        });

        [Fact]
        public void ActiveMatchHasAnOutlineAcrossThemeChangesAndScrolling() => Sta.Run(() =>
        {
            using var shell = new FindEditor();
            shell.Editor.Document = Document(80);
            shell.Call("RunFind", false);
            var host = new Window
            {
                Content = shell.Editor, Width = 640, Height = 320, Left = -32000, Top = -32000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            };
            try
            {
                host.Show();
                Pump();
                var adorner = new FindMatchAdorner(shell.Editor, shell.Window);
                foreach (string theme in new[] { "Dark", "Light", "Black", "98SE", "Blood", "Greed", "Cyanotic",
                    "Ectoplasm", "Decay", "Malaise", "Sepulchre", "Delirium", "Mourning" })
                {
                    shell.Editor.Resources.MergedDictionaries.Clear();
                    shell.Editor.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/KillerNotes;component/Themes/" + theme + ".xaml", UriKind.Relative),
                    });
                    for (int offset = 0; offset <= 100; offset += 100)
                    {
                        shell.Editor.ScrollToVerticalOffset(offset);
                        Pump();
                        var top = shell.Editor.GetPositionFromPoint(new Point(0, 0), true)!;
                        int index = Enumerable.Range(0, shell.Window.FindHits.Count).First(i =>
                            shell.Window.FindPointerFor(shell.Window.FindHits[i].Start)!.CompareTo(top) >= 0);
                        shell.Set("_findIndex", index);
                        string before = shell.Text;
                        var drawing = new DrawingGroup();
                        using (var dc = drawing.Open())
                            typeof(FindMatchAdorner).GetMethod("RenderMatches", Members)!.Invoke(adorner, new object[] { dc });
                        var shapes = GeometryDrawings(drawing).ToList();
                        Assert.True(shapes.Count > 1, theme);
                        var outlined = Assert.Single(shapes, shape => shape.Pen != null);
                        Assert.Equal(2d, outlined.Pen.Thickness);
                        Assert.Same(shell.Editor.FindResource("TextBrush"), outlined.Pen.Brush);
                        Assert.Equal(before, shell.Text);
                        Assert.False(adorner.IsHitTestVisible);
                    }
                }
            }
            finally { host.Close(); }
        });

        private static IEnumerable<GeometryDrawing> GeometryDrawings(DrawingGroup group)
        {
            foreach (var drawing in group.Children)
                if (drawing is GeometryDrawing geometry) yield return geometry;
                else if (drawing is DrawingGroup nested)
                    foreach (var child in GeometryDrawings(nested)) yield return child;
        }

        [Fact]
        public void MatchOutlineSpansFormattingBoundaries() => Sta.Run(() =>
        {
            using var shell = new FindEditor();
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run("a"));
            var ending = new Run("nd");
            paragraph.Inlines.Add(new Bold(ending));
            paragraph.Inlines.Add(new Run(" next and end"));
            shell.Editor.Document = new FlowDocument(paragraph);
            shell.Call("RunFind", false);
            var host = new Window
            {
                Content = shell.Editor, Width = 640, Height = 320, Left = -32000, Top = -32000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            };
            try
            {
                host.Show();
                Pump();
                var drawing = new DrawingGroup();
                using (var dc = drawing.Open())
                    typeof(FindMatchAdorner).GetMethod("RenderMatches", Members)!
                        .Invoke(new FindMatchAdorner(shell.Editor, shell.Window), new object[] { dc });
                var active = Assert.Single(GeometryDrawings(drawing), shape => shape.Pen != null);
                Assert.Equal(ending.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Right,
                    active.Geometry.Bounds.Right, precision: 3);
            }
            finally { host.Close(); }
        });

        private static FlowDocument Document(int paragraphs)
        {
            var doc = new FlowDocument();
            for (int i = 0; i < paragraphs; i++)
                doc.Blocks.Add(new Paragraph(new Run("and another synthetic line and ending") { Foreground = Brushes.White }));
            return doc;
        }

        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => frame.Continue = false), DispatcherPriority.ApplicationIdle);
            Dispatcher.PushFrame(frame);
        }

        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private sealed class FindEditor : IDisposable
        {
            public MainWindow Window { get; } = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            public RichTextBox Editor { get; } = new();
            public bool Stale => (bool)Get("_findPlainStale")!;
            public DispatcherOperation? Pending => (DispatcherOperation?)Get("_findRefreshOperation");
            public string Text => new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;

            public FindEditor()
            {
                Set("Editor", Editor);
                Set("FindRailBtn", new Button());
                Set("ReplaceRow", new StackPanel());
                Set("FindBar", new Border());
                Set("ReplaceBox", new TextBox { Text = "andand" });
                Set("_findHits", new List<(int Start, int Length)>());
                Set("_findRuns", new List<(int Offset, TextPointer Start, int Length)>());
                Set("_findTerm", "and");
                Set("_findPlain", "");
                Set("_findPlainStale", true);
                Set("_findOpen", true);
                Editor.TextChanged += (_, _) => Call("InvalidateFindCache");
            }

            public object? Get(string name) => typeof(MainWindow).GetField(name, Members)!.GetValue(Window);
            public void Set(string name, object value) => typeof(MainWindow).GetField(name, Members)!.SetValue(Window, value);
            public void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, Members)!.Invoke(Window, args);
            public void Dispose() => Call("CancelFindRefresh");
        }
    }
}
