using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KillerNotes.Controls.Dictation;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>These open the real speakers and microphone, so they run only where that disturbs
    /// nobody: set KN_LAB_AUDIO=1 on a lab machine. A developer's own PC may be on a call.</summary>
    public sealed class LabAudioFactAttribute : FactAttribute
    {
        public LabAudioFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("KN_LAB_AUDIO") != "1")
                Skip = "Opens real audio devices: set KN_LAB_AUDIO=1 on a lab machine";
        }
    }

    /// <summary>
    /// The dictation pad's take handling, driven through the pad's own methods on a window that is
    /// built and never shown. Starting a new take must leave nothing of the previous one behind:
    /// its playback, its undo step and its slice marks.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class DictationTakeTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        /// <summary>Silence, 16-bit mono at 8 kHz, so playback opens the device and makes no sound.</summary>
        private static byte[] Silence(int ms)
        {
            int data = 8000 * 2 * ms / 1000;
            using var ms2 = new MemoryStream();
            using var w = new BinaryWriter(ms2);
            w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            w.Write(36 + data);
            w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(8000); w.Write(16000); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            w.Write(data);
            w.Write(new byte[data]);
            w.Flush();
            return ms2.ToArray();
        }

        private static DictationWindow Pad()
        {
            var palette = ThemeHarness.Create(Theme.Dark, ThemeHarness.FirstRunAccent(Theme.Dark));
            Application.Current.Resources.MergedDictionaries.Add(palette);
            return new DictationWindow(null, _ => { }, (_, _, _, _, _) => { });
        }

        private static object? Get(DictationWindow pad, string name) =>
            (typeof(DictationWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).GetValue(pad);

        private static void Set(DictationWindow pad, string name, object? value) =>
            (typeof(DictationWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).SetValue(pad, value);

        private static object? Invoke(DictationWindow pad, string name, params object[] args) =>
            (typeof(DictationWindow).GetMethod(name, Members) ?? throw new MissingMethodException(name)).Invoke(pad, args);

        /// <summary>Presses Record, then makes sure no microphone stays open whatever the machine has.</summary>
        private static void PressRecord(DictationWindow pad)
        {
            try { Invoke(pad, "ToggleRecord", true); }
            finally
            {
                ((DispatcherTimer)Get(pad, "_tick")!).Stop();
                if (DictationRecorder.IsRecording) DictationRecorder.Stop();
            }
        }

        [LabAudioFact]
        public void RecordingANewTakeStopsThePlaybackOfTheOldOne() => Sta.RunApplication(() =>
        {
            var pad = Pad();
            var old = Silence(5000);
            Set(pad, "_wav", old);
            try
            {
                Assert.True(DictationPlayer.Play(old), DictationPlayer.LastError);
                Assert.True(DictationPlayer.IsOpen);

                PressRecord(pad);

                Assert.False(DictationPlayer.IsOpen);
            }
            finally { DictationPlayer.Stop(); }
        });

        [LabAudioFact]
        public void RecordingANewTakeDropsTheUndoStepOfTheOldOne() => Sta.RunApplication(() =>
        {
            var pad = Pad();
            Set(pad, "_wav", Silence(1000));
            Set(pad, "_undo", Silence(2000));

            PressRecord(pad);

            Assert.Null(Get(pad, "_undo"));
        });

        [LabAudioFact]
        public void RecordingANewTakeClearsTheSliceMarksOfTheOldOne() => Sta.RunApplication(() =>
        {
            var pad = Pad();
            Set(pad, "_wav", Silence(1000));
            var wave = (WaveformView)Get(pad, "_wave")!;
            wave.AddCut(0.25);
            wave.AddCut(0.75);
            Assert.Equal(2, wave.Cuts.Count);

            PressRecord(pad);

            Assert.Empty(wave.Cuts);
        });

        [LabAudioFact]
        public void EditingATakeAfterEmbeddingItOffersEmbedAgain() => Sta.RunApplication(() =>
        {
            var pad = Pad();
            Set(pad, "_wav", Silence(2000));
            Set(pad, "_durationMs", 2000);
            var embed = (Button)Get(pad, "_embedBtn")!;
            embed.IsEnabled = true;

            Invoke(pad, "Embed");
            Assert.False(embed.IsEnabled);              // a second Embed of the same audio would duplicate it

            Invoke(pad, "ApplyEdit", Silence(1000), 0.0);   // the take changed, so it is worth saving again
            Assert.True(embed.IsEnabled);
        });

        [LabAudioFact]
        public void ScrubbingTheWaveformSeeksToTheLatestPositionOnceTheMouseSettles() => Sta.RunApplication(() =>
        {
            var pad = Pad();
            var wave = (WaveformView)Get(pad, "_wave")!;
            Set(pad, "_wav", Silence(5000));
            Set(pad, "_durationMs", 5000);
            try
            {
                for (int i = 1; i <= 9; i++) Invoke(pad, "ScrubTo", i / 10.0);

                // Nothing has been restarted yet: a burst of mouse moves is one seek, not nine.
                Assert.False(DictationPlayer.IsOpen);

                var frame = new DispatcherFrame();
                var stop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                stop.Tick += (_, _) => { stop.Stop(); frame.Continue = false; };
                stop.Start();
                Dispatcher.PushFrame(frame);

                Assert.True(DictationPlayer.IsOpen);
                Assert.Equal(0.9, wave.Progress, 3);
            }
            finally { DictationPlayer.Stop(); }
        });
    }
}
