using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Controls.Dictation;
using KillerNotes.Controls.Sketch;
using KillerNotes.Models;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class FloatingToolKeyboardTests
    {
        [Fact]
        public void AppsOpensTheWaveformCardAndEscapeReturnsFocusToTheWaveform() => CardColorBehaviorTests.WithShell((_, _) =>
        {
            var pad = new DictationWindow(null, _ => { }, (_, _, _, _, _) => null, _ => "");
            Assert.True(pad.LoadForEdit(DictationWorkflowTests.Silence(2000), "take", 1, null));
            var wave = Descendants((DependencyObject)pad.Content).OfType<WaveformView>().Single();
            try
            {
                pad.Show();
                pad.Activate();
                pad.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.True(wave.Focus(), "The waveform must be reachable by keyboard focus.");
                Send(wave, Key.Apps, Keyboard.KeyUpEvent);
                pad.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

                Assert.NotNull(wave.ContextMenu);
                Assert.True(wave.ContextMenu.IsOpen);
                var focused = Assert.IsType<Button>(Keyboard.FocusedElement);
                Assert.True(focused.IsEnabled);
                Send(focused, Key.Escape, Keyboard.KeyDownEvent);
                pad.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.False(wave.ContextMenu.IsOpen);
                Assert.True(wave.IsKeyboardFocused);
            }
            finally { if (wave.ContextMenu != null) wave.ContextMenu.IsOpen = false; ClosePad(pad); }
        });

        [Fact]
        public void AppsOpensTheSketchCanvasCardAndItsClearActionWorksFromTheKeyboard() => CardColorBehaviorTests.WithShell((_, _) =>
        {
            int printedObjects = -1;
            var pad = new SketchPadWindow(null, (objects, _, _) => printedObjects = objects.Count);
            pad.LoadObjects(new[] { new SketchObject { Kind = SketchKind.Text, Text = "Label", X = 20, Y = 20 } });
            pad.Show();
            pad.Activate();
            pad.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            var canvas = Descendants((DependencyObject)pad.Content).OfType<Canvas>().OrderByDescending(c => c.Width * c.Height).First();
            try
            {
                Assert.True(canvas.Focus(), "The sketch canvas must be reachable by keyboard focus.");
                Send(canvas, Key.Apps, Keyboard.KeyUpEvent);
                pad.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.NotNull(canvas.ContextMenu);
                Assert.True(canvas.ContextMenu.IsOpen);
                Assert.IsType<Button>(Keyboard.FocusedElement);
                var clear = Assert.IsType<Button>(KillerNotes.Shell.NoteCards.Tile(canvas.ContextMenu, "clearall"));
                Assert.True(clear.Focus());
                Send(clear, Key.Enter, Keyboard.KeyDownEvent);
                pad.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Assert.False(canvas.ContextMenu.IsOpen);
                Assert.True(canvas.IsKeyboardFocused);
                var print = Descendants((DependencyObject)pad.Content).OfType<Button>()
                    .Single(b => System.Windows.Automation.AutomationProperties.GetAcceleratorKey(b) == "Ctrl+Enter");
                print.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(0, printedObjects);
            }
            finally { if (canvas.ContextMenu != null) canvas.ContextMenu.IsOpen = false; ClosePad(pad); }
        });

        private static void ClosePad(Window pad)
        {
            pad.BeginAnimation(UIElement.OpacityProperty, null);
            pad.Opacity = 0;
            pad.Close();
            Assert.False(pad.IsVisible, "The fixture must close its pad before releasing shared resources.");
        }

        private static void Send(UIElement target, Key key, RoutedEvent routed) =>
            InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key)
                { RoutedEvent = routed, Source = target });

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
    }
}
