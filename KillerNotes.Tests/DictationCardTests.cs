using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using KillerNotes.Controls.Dictation;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The dictation waveform's right-click card on the family card rules (Shell/NoteCards.cs): two full rows of
    /// tiles, Slice, Copy and Paste over Undo, Clear slices and Delete segment, with no text rows. The pad is built
    /// and never shown, and nothing here opens a microphone or speaker; every tile is checked against the Alt
    /// shortcut the waveform binds for it.
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

        private static List<Button> Tiles(ContextMenu card) => NoteCards.Tiles(card).ToList();

        [Fact]
        public void TheCardIsTwoFullRowsOfTilesWithTheirShortcutsAndNoTextRows() => WithDark(() =>
        {
            var card = CardAt(Pad(), 0.5);

            Assert.Equal("Recording", AutomationProperties.GetName((MenuItem)card.Items[0]));
            Assert.Equal(NoteCards.Width + 44, card.MaxWidth);
            Assert.Equal(new[] { "Slice here", "Copy", "Paste", "Undo", "Clear slices", "Delete segment" },
                Tiles(card).Select(b => (string)b.Tag));
            Assert.Equal(new[] { "Alt+S", "Alt+C", "Alt+V", "Alt+Z", "Alt+L", "Alt+D" },
                Tiles(card).Select(AutomationProperties.GetAcceleratorKey));
            Assert.Equal("Copy segment (Alt+C)", CardTileHelper.Tile(card, "copy").ToolTip);
            Assert.Equal(new[] { 3, 3 }, NoteCards.RowSizes(Tiles(card).Count));
            Assert.DoesNotContain(card.Items.Cast<object>(), item => item is Separator || item is MenuItem { Header: string });
        });

        [Fact]
        public void WithNoCutsDeleteAndClearAreDimmedInPlaceAndPasteAndUndoWait() => WithDark(() =>
        {
            var card = CardAt(Pad(), 0.5);

            Assert.True(CardTileHelper.Tile(card, "slice").IsEnabled);
            Assert.True(CardTileHelper.Tile(card, "copy").IsEnabled);
            Assert.False(CardTileHelper.Tile(card, "paste").IsEnabled);
            Assert.False(CardTileHelper.Tile(card, "undo").IsEnabled);
            Assert.False(CardTileHelper.Tile(card, "clearcuts").IsEnabled);
            Assert.False(CardTileHelper.Tile(card, "deleteseg").IsEnabled);
            Assert.Equal(6, Tiles(card).Count);
        });

        [Fact]
        public void SliceThenCopyPasteAndUndoRunTheOldEdits() => WithDark(() =>
        {
            var pad = Pad();
            CardTileHelper.Click(CardAt(pad, 0.5), "slice");
            Assert.Equal(new[] { 0.5 }, Wave(pad).Cuts);

            // With a cut, Delete segment and Clear slices wake up in the places they already held.
            var card = CardAt(pad, 0.25);
            Assert.True(CardTileHelper.Tile(card, "deleteseg").IsEnabled);
            Assert.True(CardTileHelper.Tile(card, "clearcuts").IsEnabled);

            CardTileHelper.Click(card, "copy");
            var clip = Assert.IsType<byte[]>(Get(pad, "_clip"));
            Assert.Equal(1000, WavEdit.DurationMs(clip));   // the first half, up to the cut

            var paste = CardTileHelper.Tile(CardAt(pad, 0.25), "paste");
            Assert.True(paste.IsEnabled);
            CardTileHelper.Click(paste);
            Assert.Equal(3000, WavEdit.DurationMs((byte[])Get(pad, "_wav")!));

            var undo = CardTileHelper.Tile(CardAt(pad, 0.25), "undo");
            Assert.True(undo.IsEnabled);
            CardTileHelper.Click(undo);
            Assert.Equal(2000, WavEdit.DurationMs((byte[])Get(pad, "_wav")!));
        });

        [Fact]
        public void ClearSlicesAndDeleteSegmentRunTheOldEdits() => WithDark(() =>
        {
            var pad = Pad();
            Wave(pad).AddCut(0.5);
            CardTileHelper.Click(CardAt(pad, 0.25), "clearcuts");
            Assert.Empty(Wave(pad).Cuts);

            Wave(pad).AddCut(0.5);
            CardTileHelper.Click(CardAt(pad, 0.25), "deleteseg");
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
