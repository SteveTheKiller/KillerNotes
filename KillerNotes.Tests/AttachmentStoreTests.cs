using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Sketches and recordings are stored once by content hash and named from the note's
    /// document by marker. These pin the storage contract: a history version keeps the exact
    /// payloads it was saved with, restoring it brings them back, and a payload is only removed
    /// once no note, trashed note or version points at it.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class AttachmentStoreTests : IDisposable
    {
        private readonly TimeSpan _interval = NoteStore.HistoryInterval;

        public void Dispose() => NoteStore.HistoryInterval = _interval;

        [Fact]
        public void HistoryRestoreBringsBackTheExactPayloadsOfThatVersion() => Sta.Run(() =>
        {
            using var store = new TempStore();
            NoteStore.HistoryInterval = TimeSpan.Zero;   // every save keeps the previous body
            long id = NoteStore.Create("n");
            var oldSketch = new Attachment(AttachmentKind.Sketch, Bytes(1));
            var oldAudio = new Attachment(AttachmentKind.Recording, Bytes(2), 1500);
            SaveWith(id, oldSketch, oldAudio);

            var newSketch = new Attachment(AttachmentKind.Sketch, Bytes(3));
            var newAudio = new Attachment(AttachmentKind.Recording, Bytes(4), 900);
            SaveWith(id, newSketch, newAudio);

            long version = NoteStore.ListHistory(id).Single().Id;
            Assert.True(NoteStore.RestoreVersion(id, version));

            var restored = Attachments.MarkersIn(NoteStore.LoadContent(id));
            Assert.Equal(new[] { oldSketch.Hash, oldAudio.Hash }.OrderBy(h => h), restored.OrderBy(h => h));
            Assert.Equal(Bytes(1), NoteStore.GetAttachment(oldSketch.Hash)!.Payload);
            var audio = NoteStore.GetAttachment(oldAudio.Hash)!;
            Assert.Equal(Bytes(2), audio.Payload);
            Assert.Equal(1500, audio.DurationMs);
            // The newer state is a version now, so its payloads are still there too.
            Assert.NotNull(NoteStore.GetAttachment(newSketch.Hash));
            Assert.NotNull(NoteStore.GetAttachment(newAudio.Hash));
        });

        [Fact]
        public void APayloadGoesOnlyWhenNothingPointsAtIt() => Sta.Run(() =>
        {
            using var store = new TempStore();
            NoteStore.HistoryInterval = TimeSpan.FromDays(1);   // no versions between these saves
            long a = NoteStore.Create("a"), b = NoteStore.Create("b");
            var shared = new Attachment(AttachmentKind.Sketch, Bytes(5));
            var onlyA = new Attachment(AttachmentKind.Sketch, Bytes(6));
            SaveWith(a, shared, onlyA);
            SaveWith(b, shared);

            SaveWith(a);   // a drops both; the version this save keeps still points at them
            Assert.Single(NoteStore.ListHistory(a));
            Assert.NotNull(NoteStore.GetAttachment(onlyA.Hash));

            NoteStore.DeleteForever(a);   // the note and its versions go
            Assert.Null(NoteStore.GetAttachment(onlyA.Hash));
            Assert.NotNull(NoteStore.GetAttachment(shared.Hash));   // b still uses it

            NoteStore.Trash(b);
            long c = NoteStore.Create("c");
            SaveWith(c);
            Assert.NotNull(NoteStore.GetAttachment(shared.Hash));   // a trashed note keeps its payloads

            NoteStore.DeleteForever(b);
            Assert.Null(NoteStore.GetAttachment(shared.Hash));
        });

        [Fact]
        public void UndoingADeleteKeepsThePayloads() => Sta.Run(() =>
        {
            using var store = new TempStore();
            NoteStore.HistoryInterval = TimeSpan.FromDays(1);
            long a = NoteStore.Create("a"), b = NoteStore.Create("b");
            var shared = new Attachment(AttachmentKind.Sketch, Bytes(7));
            SaveWith(a, shared);
            SaveWith(b, shared);

            var row = NoteStore.CaptureRow(a)!;
            NoteStore.Delete(a);
            SaveWith(b);                     // the other user of the payload lets go of it
            NoteStore.RestoreRow(row);       // Ctrl+Z

            Assert.Equal(Bytes(7), NoteStore.GetAttachment(shared.Hash)!.Payload);
        });

        [Fact]
        public void SharedNotesCarryTheirPayloads() => Sta.Run(() =>
        {
            string knote = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".knote");
            try
            {
                var sketch = new Attachment(AttachmentKind.Sketch, Bytes(8));
                var audio = new Attachment(AttachmentKind.Recording, Bytes(9), 700);
                using (new TempStore())
                {
                    long id = NoteStore.Create("shared");
                    SaveWith(id, sketch, audio);
                    NoteStore.ExportNote(id, knote, password: null);
                }
                using (new TempStore())
                {
                    Assert.Equal(1, NoteStore.ImportNotes(knote, password: null));
                    long id = NoteStore.List().Single().Id;
                    Assert.Equal(new[] { sketch.Hash, audio.Hash }.OrderBy(h => h),
                                 Attachments.MarkersIn(NoteStore.LoadContent(id)).OrderBy(h => h));
                    Assert.Equal(Bytes(8), NoteStore.GetAttachment(sketch.Hash)!.Payload);
                    Assert.Equal(700, NoteStore.GetAttachment(audio.Hash)!.DurationMs);
                }
            }
            finally { try { File.Delete(knote); } catch { } }
        });

        /// <summary>Saves the note as one paragraph holding a marked image per attachment, the
        /// way the editor stores sketches and recording chips.</summary>
        private static void SaveWith(long id, params Attachment[] attachments)
        {
            var bmp = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
            var p = new Paragraph(new Run("body "));
            foreach (var a in attachments)
                p.Inlines.Add(new InlineUIContainer(new Image { Source = bmp, ToolTip = Attachments.Marker(a.Kind, a.Hash) }));
            var doc = new FlowDocument(p);
            using var ms = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.XamlPackage);
            NoteStore.SaveEditorState(id, NoteStore.GetNote(id)!.Title, ms.ToArray(), "body",
                Array.Empty<string>(), attachments);
        }

        private static byte[] Bytes(byte seed) => Enumerable.Range(0, 32).Select(i => (byte)(i * seed + seed)).ToArray();
    }
}
