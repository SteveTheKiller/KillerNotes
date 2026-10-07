using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class FontSizeSliderTests
    {
        [Fact]
        public void LongDragLeavesLargeSelectionAloneUntilReleaseAndAppliesOnlyFinalSize() => Sta.Run(() =>
        {
            using var shell = new DetachedEditor(paragraphs: 2000);
            string before = shell.Text;
            int changes = 0;
            shell.Editor.TextChanged += (_, _) => changes++;
            shell.Slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            shell.Queue(18);
            Pump();
            shell.Queue(24);
            Pump();
            Assert.Equal(0, changes);
            Assert.Equal(13d, shell.Size);

            shell.Queue(32);
            shell.Slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Pump();
            Assert.Equal(1, changes);
            Assert.Equal(32d, shell.Size);
            Assert.Equal(before, shell.Text);
        });

        [Fact]
        public void KeyboardChangesAreCoalescedWithoutADrag() => Sta.Run(() =>
        {
            using var shell = new DetachedEditor();
            int changes = 0;
            shell.Editor.TextChanged += (_, _) => changes++;
            shell.Queue(14);
            shell.Queue(16);
            shell.Queue(20);
            Pump();
            Assert.Equal(1, changes);
            Assert.Equal(20d, shell.Size);
        });

        [Theory]
        [InlineData("selection")]
        [InlineData("note")]
        [InlineData("readonly")]
        [InlineData("cancel")]
        public void DeferredChangeCannotFormatAnotherSelectionOrAnUnavailableNote(string transition) => Sta.Run(() =>
        {
            using var shell = new DetachedEditor();
            shell.Queue(24);
            switch (transition)
            {
                case "selection":
                    shell.Editor.Selection.Select(shell.Editor.Document.ContentStart, shell.Editor.Document.ContentStart);
                    break;
                case "note": shell.Set("_currentId", 2L); break;
                case "readonly": shell.Editor.IsReadOnly = true; break;
                case "cancel": shell.Call("CancelPendingFontSize"); break;
            }
            shell.Call("FlushPendingFontSize");
            Assert.Equal(13d, shell.Size);
        });

        [Fact]
        public void FlushingBeforeTheDelayKeepsTheFinalChangeAndDoesNotApplyItTwice() => Sta.Run(() =>
        {
            using var shell = new DetachedEditor();
            int changes = 0;
            shell.Editor.TextChanged += (_, _) => changes++;
            shell.Queue(28);
            shell.Call("FlushPendingFontSize");
            Pump();
            Assert.Equal(28d, shell.Size);
            Assert.Equal(1, changes);
        });

        private static void Pump()
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private sealed class DetachedEditor : IDisposable
        {
            private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            private readonly MainWindow _window = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            public RichTextBox Editor { get; }
            public Slider Slider { get; } = new();
            public string Text => new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;
            public object Size => new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd)
                .GetPropertyValue(TextElement.FontSizeProperty);

            public DetachedEditor(int paragraphs = 1)
            {
                var doc = new FlowDocument { FontSize = 13 };
                for (int i = 0; i < paragraphs; i++) doc.Blocks.Add(new Paragraph(new Run(new string('x', 200))));
                Editor = new RichTextBox(doc);
                Editor.Selection.Select(doc.ContentStart, doc.ContentEnd);
                Set("Editor", Editor);
                Set("FontSizeSlider", Slider);
                Set("FontSizePopup", new ContextMenu());
                Set("FontSizeText", new TextBlock());
                Set("_currentId", 1L);
                Set("_saveTimer", new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) });
                Set("_syntaxTimer", new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) });
                Set("_syntaxHighlight", true);
                Call("InitFontSizeSlider");
            }

            public void Set(string name, object value) => typeof(MainWindow).GetField(name, Members)!.SetValue(_window, value);
            public void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, Members)!.Invoke(_window, args);
            public void Queue(int size) => Call("QueueFontSize", size);
            public void Dispose()
            {
                Call("CancelPendingFontSize");
                foreach (string field in new[] { "_saveTimer", "_syntaxTimer" })
                    ((DispatcherTimer)typeof(MainWindow).GetField(field, Members)!.GetValue(_window)!).Stop();
            }
        }
    }
}
