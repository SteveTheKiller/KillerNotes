using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using KillerNotes.Controls.Dictation;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection("Application resources")]
    public sealed class DictationWorkflowTests
    {
        [Fact]
        public void ACardOpenedBeforeTranscriptionCannotDeleteAudioWhileRecognitionIsBusy() => CardColorBehaviorTests.WithShell((_, _) =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            int embeddedDuration = 0;
            var pad = new DictationWindow(null, _ => { }, (wav, _, _, _, _) =>
            {
                embeddedDuration = WavEdit.DurationMs(wav);
                return null;
            }, wav => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return "Recording A"; });
            var transcribe = Button(pad, "Alt+T");
            try
            {
                Assert.True(pad.LoadForEdit(Silence(2000), "take-a", 1, null));
                var wave = Descendants(pad.Content as DependencyObject).OfType<WaveformView>().Single();
                wave.AddCut(0.5);
                wave.SelectedSegment = 0;
                var card = pad.BuildWaveCard();
                var delete = card.Items.OfType<MenuItem>().Single(m => m.Header as string == "Delete segment");
                Assert.True(delete.IsEnabled);

                Click(transcribe);
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "The supplied recognizer must be running.");
                delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                release.Set();
                WaitFor(() => transcribe.IsEnabled);
                Click(Button(pad, "Alt+E"));

                Assert.Equal(2000, embeddedDuration);
            }
            finally { release.Set(); WaitFor(() => transcribe.IsEnabled); pad.Close(); }
        });

        [Fact]
        public void LoadingAnotherRecordingWaitsForTheCurrentTranscription() => CardColorBehaviorTests.WithShell((_, _) =>
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            string? printed = null;
            var pad = new DictationWindow(null, text => printed = text, (_, _, _, _, _) => null,
                wav => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return "Recording A"; });
            var transcribe = Button(pad, "Alt+T");
            try
            {
                Assert.True(pad.LoadForEdit(Silence(2000), "take-a", 1, null));
                Click(transcribe);
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

                Assert.False(pad.LoadForEdit(Silence(1000), "take-b", 2, null));
                release.Set();
                WaitFor(() => transcribe.IsEnabled);
                Click(Button(pad, "Ctrl+Enter"));
                Assert.Equal("Recording A", printed);
                Assert.True(pad.LoadForEdit(Silence(1000), "take-b", 2, null));
                Assert.Equal("", Descendants(pad.Content as DependencyObject).OfType<TextBox>().Single().Text);
            }
            finally { release.Set(); WaitFor(() => transcribe.IsEnabled); pad.Close(); }
        });

        private static Button Button(DictationWindow pad, string gesture) => Descendants(pad.Content as DependencyObject)
            .OfType<Button>().Single(b => AutomationProperties.GetAcceleratorKey(b) == gesture);

        private static void Click(ButtonBase button)
        {
            Assert.True(button.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject? root)
        {
            if (root == null) yield break;
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
        }

        private static void WaitFor(Func<bool> ready)
        {
            var clock = Stopwatch.StartNew();
            while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(5))
            {
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start();
                Dispatcher.PushFrame(frame);
            }
            Assert.True(ready(), "The dictation operation did not finish within its deadline.");
        }

        internal static byte[] Silence(int milliseconds)
        {
            int dataLength = 16 * milliseconds;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            writer.Write(36 + dataLength);
            writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
            writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            writer.Write(dataLength); writer.Write(new byte[dataLength]); writer.Flush();
            return stream.ToArray();
        }
    }
}
