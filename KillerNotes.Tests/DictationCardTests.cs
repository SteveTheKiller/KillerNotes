using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using KillerNotes.Controls.Dictation;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The dictation waveform's right-click card (1.7.0 card menus): Slice, Copy, Paste and Undo
    /// on the quick strip, Delete segment and Clear slices as list rows under a rule. The pad is
    /// built and never shown, and nothing here opens a microphone or speaker; every control is
    /// checked against the Alt shortcut the waveform binds for it.
    /// </summary>
    [Collection("Application resources")]
    public sealed class DictationCardTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>16-bit mono 8 kHz silence; the card's edits only measure and splice it.</summary>
        private static byte[] Silence(int ms)
        {
            int data = 8000 * 2 * ms / 1000;
            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
            w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            w.Write(36 + data);
            w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(8000); w.Write(16000); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            w.Write(data);
            w.Write(new byte[data]);
            w.Flush();
            return stream.ToArray();
        }

        private static DictationWindow Pad()
        {
            var pad = new DictationWindow(null, _ => { }, (_, _, _, _, _) => null);
            Assert.True(pad.LoadForEdit(Silence(2000), "hash", 1, null));
            return pad;
        }

        private static object? Get(DictationWindow pad, string name) =>
            (typeof(DictationWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).GetValue(pad);

        private static void Set(DictationWindow pad, string name, object? value) =>
            (typeof(DictationWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).SetValue(pad, value);

        private static WaveformView Wave(DictationWindow pad) => (WaveformView)Get(pad, "_wave")!;

        /// <summary>Opens the card at a point, the way the right-click handler does.</summary>
        private static ContextMenu CardAt(DictationWindow pad, double fraction)
        {
            Set(pad, "_menuFraction", fraction);
            Wave(pad).SelectedSegment = Wave(pad).SegmentAt(fraction);
            return pad.BuildWaveCard();
        }

        private static List<Button> Quick(ContextMenu card)
        {
            var strip = card.Items.OfType<MenuItem>().Single(m => m.Header is Border { Child: UniformGrid });
            return ((UniformGrid)((Border)strip.Header).Child).Children.OfType<Button>().ToList();
        }

        private static Button QuickNamed(ContextMenu card, string name) =>
            Quick(card).Single(b => AutomationProperties.GetName(b) == name);

        private static List<MenuItem> Rows(ContextMenu card) =>
            card.Items.OfType<MenuItem>().Where(m => m.Header is string).ToList();

        private static void Click(ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        [Fact]
        public void TheStripHoldsSliceCopyPasteAndUndoWithTheirShortcuts() => WithDark(() =>
        {
            var card = CardAt(Pad(), 0.5);

            Assert.Equal("Recording", AutomationProperties.GetName((MenuItem)card.Items[0]));
            Assert.Equal(new[] { "Slice here", "Copy segment", "Paste segment here", "Undo edit" },
                Quick(card).Select(b => AutomationProperties.GetName(b)));
            Assert.Equal(new[] { "Slice here", "Copy", "Paste", "Undo" }, Quick(card).Select(b => (string)b.Tag));
            Assert.Equal(new[] { "Slice here (Alt+S)", "Copy segment (Alt+C)", "Paste segment here (Alt+V)", "Undo edit (Alt+Z)" },
                Quick(card).Select(b => (string)b.ToolTip));
            Assert.Equal(300 + 44, card.MaxWidth);
        });

        [Fact]
        public void WithNoCutsDeleteIsGreyedClearIsHiddenAndPasteAndUndoWait() => WithDark(() =>
        {
            var card = CardAt(Pad(), 0.5);

            Assert.False(QuickNamed(card, "Paste segment here").IsEnabled);
            Assert.False(QuickNamed(card, "Undo edit").IsEnabled);
            var delete = Assert.Single(Rows(card));
            Assert.Equal("Delete segment", delete.Header);
            Assert.Equal("Alt+D", delete.InputGestureText);
            Assert.False(delete.IsEnabled);
            Assert.True(card.Items[card.Items.IndexOf(delete) - 1] is MenuItem { Header: Border { Child: UniformGrid } }, "The row should sit right under the strip, whose lower rule separates them.");
        });

        [Fact]
        public void SliceThenCopyPasteAndUndoRunTheOldEdits() => WithDark(() =>
        {
            var pad = Pad();
            Click(QuickNamed(CardAt(pad, 0.5), "Slice here"));
            Assert.Equal(new[] { 0.5 }, Wave(pad).Cuts);

            // With a cut the rows change: Delete segment wakes, Clear slices appears.
            var card = CardAt(pad, 0.25);
            var rows = Rows(card);
            Assert.Equal(new[] { "Delete segment", "Clear slices" }, rows.Select(r => (string)r.Header));
            Assert.True(rows[0].IsEnabled);
            Assert.Equal("Alt+L", rows[1].InputGestureText);

            Click(QuickNamed(card, "Copy segment"));
            var clip = Assert.IsType<byte[]>(Get(pad, "_clip"));
            Assert.Equal(1000, WavEdit.DurationMs(clip));   // the first half, up to the cut

            var paste = QuickNamed(CardAt(pad, 0.25), "Paste segment here");
            Assert.True(paste.IsEnabled);
            Click(paste);
            Assert.Equal(3000, WavEdit.DurationMs((byte[])Get(pad, "_wav")!));

            var undo = QuickNamed(CardAt(pad, 0.25), "Undo edit");
            Assert.True(undo.IsEnabled);
            Click(undo);
            Assert.Equal(2000, WavEdit.DurationMs((byte[])Get(pad, "_wav")!));
        });

        [Fact]
        public void ClearSlicesAndDeleteSegmentRunTheOldEdits() => WithDark(() =>
        {
            var pad = Pad();
            Wave(pad).AddCut(0.5);
            var clear = Rows(CardAt(pad, 0.25)).Single(r => (string)r.Header == "Clear slices");
            clear.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Empty(Wave(pad).Cuts);

            Wave(pad).AddCut(0.5);
            var delete = Rows(CardAt(pad, 0.25)).Single(r => (string)r.Header == "Delete segment");
            delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(1000, WavEdit.DurationMs((byte[])Get(pad, "_wav")!));
        });

        private static void WithDark(Action body) => Sta.RunApplication(() =>
        {
            if (Application.ResourceAssembly == null) Application.ResourceAssembly = typeof(App).Assembly;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var merged = app.Resources.MergedDictionaries;
            var saved = merged.ToList();
            merged.Clear();
            merged.Add(ThemeHarness.Create(Theme.Dark, ThemeHarness.FirstRunAccent(Theme.Dark)));
            foreach (string p in new[] { "Controls/Controls.xaml", "Strings/en-US.xaml", "Controls/AppStyles.xaml", "Controls/OverlayScrollViewer.xaml" })
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
