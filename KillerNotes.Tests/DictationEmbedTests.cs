using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using KillerNotes.Controls.Dictation;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// What the dictation pad remembers after Embed and Save changes: the chip it left in the note,
    /// so the next press replaces that chip instead of adding a second one or looking for the one
    /// that was there before. The pad is built and never shown, and nothing here opens a
    /// microphone or speaker.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class DictationEmbedTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        /// <summary>16-bit mono 8 kHz silence; the pad only measures it and hands it on.</summary>
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

        /// <summary>A pad whose host answers each Embed from the queue; a null entry is a host that
        /// placed nothing, and an exception entry is a host that refused.</summary>
        private static DictationWindow Pad(List<(string? ReplaceHash, long NoteId, string? Database)> calls, Queue<object?> answers)
        {
            var palette = ThemeHarness.Create(Theme.Dark, ThemeHarness.FirstRunAccent(Theme.Dark));
            Application.Current.Resources.MergedDictionaries.Add(palette);
            return new DictationWindow(null, _ => { }, (_, _, replaceHash, noteId, database) =>
            {
                calls.Add((replaceHash, noteId, database));
                object? answer = answers.Dequeue();
                if (answer is Exception ex) throw ex;
                return (EmbeddedRecording?)answer;
            });
        }

        private static void Set(DictationWindow pad, string name, object? value) =>
            (typeof(DictationWindow).GetField(name, Members) ?? throw new MissingFieldException(name)).SetValue(pad, value);

        private static void Invoke(DictationWindow pad, string name, params object[] args) =>
            (typeof(DictationWindow).GetMethod(name, Members) ?? throw new MissingMethodException(name)).Invoke(pad, args);

        private static void TakeAndEmbed(DictationWindow pad)
        {
            Set(pad, "_wav", Silence(2000));
            Set(pad, "_durationMs", 2000);
            Invoke(pad, "Embed");
        }

        [Fact]
        public void EmbeddingAfterAnEditReplacesTheChipTheFirstEmbedPlaced() => Sta.RunApplication(() =>
        {
            var calls = new List<(string? ReplaceHash, long NoteId, string? Database)>();
            var pad = Pad(calls, new Queue<object?>(new object?[] { new EmbeddedRecording("hash-1", 7, "db-1"), new EmbeddedRecording("hash-2", 7, "db-1") }));

            TakeAndEmbed(pad);
            Invoke(pad, "ApplyEdit", Silence(1000), 0.0);
            Invoke(pad, "Embed");

            Assert.Equal(2, calls.Count);
            Assert.Null(calls[0].ReplaceHash);                      // a new take adds a chip
            Assert.Equal(("hash-1", 7, "db-1"), calls[1]);   // the edit replaces that chip
        });

        [Fact]
        public void SavingChangesTwiceTargetsTheChipTheFirstSaveWrote() => Sta.RunApplication(() =>
        {
            var calls = new List<(string? ReplaceHash, long NoteId, string? Database)>();
            var pad = Pad(calls, new Queue<object?>(new object?[] { new EmbeddedRecording("new-hash", 3, "db-1"), new EmbeddedRecording("newer-hash", 3, "db-1") }));

            Assert.True(pad.LoadForEdit(Silence(2000), "old-hash", 3, "db-1"));
            Invoke(pad, "ApplyEdit", Silence(1000), 0.0);
            Invoke(pad, "Embed");
            Invoke(pad, "ApplyEdit", Silence(500), 0.0);
            Invoke(pad, "Embed");

            Assert.Equal("old-hash", calls[0].ReplaceHash);
            Assert.Equal(("new-hash", 3, "db-1"), calls[1]);   // not the hash the chip no longer has
        });

        [Fact]
        public void AnEmbedThatPlacedNothingOrFailedLeavesTheNextOneAddingOrReplacingAsBefore() => Sta.RunApplication(() =>
        {
            var calls = new List<(string? ReplaceHash, long NoteId, string? Database)>();
            var pad = Pad(calls, new Queue<object?>(new object?[] { null, new InvalidOperationException("refused"), new EmbeddedRecording("hash-1", 7, "db-1"), new EmbeddedRecording("hash-2", 7, "db-1") }));

            TakeAndEmbed(pad);      // the host placed nothing
            Invoke(pad, "ApplyEdit", Silence(1000), 0.0);
            Invoke(pad, "Embed");   // the host refused
            Invoke(pad, "Embed");   // placed at last
            Invoke(pad, "ApplyEdit", Silence(500), 0.0);
            Invoke(pad, "Embed");

            Assert.Null(calls[1].ReplaceHash);
            Assert.Null(calls[2].ReplaceHash);
            Assert.Equal("hash-1", calls[3].ReplaceHash);
        });
    }
}
