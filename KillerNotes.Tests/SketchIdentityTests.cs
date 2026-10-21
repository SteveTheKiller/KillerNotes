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
    /// Sketches follow their own marker rather than their position, so inserting or removing
    /// other images never hands one sketch's drawing to another image, and restoring a version
    /// brings back the drawing that version was saved with. Drives the same Sketch and NoteStore
    /// calls the editor makes on save (CaptureAttachments) and open (Link).
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class SketchIdentityTests : IDisposable
    {
        private readonly TimeSpan _interval = NoteStore.HistoryInterval;

        public void Dispose() => NoteStore.HistoryInterval = _interval;

        [Fact]
        public void RestoringAVersionBringsBackThatVersionsEditableSketches() => Sta.Run(() =>
        {
            using var store = new TempStore();
            NoteStore.HistoryInterval = TimeSpan.Zero;
            long id = NoteStore.Create("n");

            // v1: sketch A, a plain image, sketch B.
            var doc = new FlowDocument(new Paragraph());
            var a = AddImage(doc, Payload("A1"));
            AddImage(doc, null);
            var b = AddImage(doc, Payload("B1"));
            Save(id, doc);

            // v2: a new plain image ahead of everything, A edited, B removed.
            doc = Open(id);
            var images = Sketch.Images(doc.Blocks).ToList();
            ((Paragraph)doc.Blocks.FirstBlock).Inlines.InsertBefore(
                ((Paragraph)doc.Blocks.FirstBlock).Inlines.FirstInline, new InlineUIContainer(NewImage()));
            Sketch.SetData(images[0], Payload("A2"));
            ((Paragraph)doc.Blocks.FirstBlock).Inlines.Remove((InlineUIContainer)images[2].Parent);
            Save(id, doc);

            doc = Open(id);
            Assert.Equal(new[] { null, "A2", null }, Sketches(doc));

            Assert.True(NoteStore.RestoreVersion(id, NoteStore.ListHistory(id).First().Id));
            doc = Open(id);
            Assert.Equal(new[] { "A1", null, "B1" }, Sketches(doc));
        });

        [Fact]
        public void NotesSavedBeforeMarkersStillFindTheirSketchesByPosition() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("old");
            var doc = new FlowDocument(new Paragraph());
            AddImage(doc, null);
            AddImage(doc, null);
            using (var ms = new MemoryStream())
            {
                new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.XamlPackage);
                NoteStore.Save(id, "old", ms.ToArray(), "");
            }
            NoteStore.SaveSketches(id, new System.Collections.Generic.Dictionary<int, byte[]> { [1] = Payload("legacy") });

            Assert.Equal(new[] { null, "legacy" }, Sketches(Open(id)));
        });

        private static void Save(long id, FlowDocument doc)
        {
            var attachments = Sketch.CaptureAttachments(doc.Blocks);
            using var ms = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.XamlPackage);
            NoteStore.SaveEditorState(id, "n", ms.ToArray(), "", Array.Empty<string>(), attachments);
        }

        private static FlowDocument Open(long id)
        {
            var doc = new FlowDocument();
            using var ms = new MemoryStream(NoteStore.LoadContent(id)!);
            new TextRange(doc.ContentStart, doc.ContentEnd).Load(ms, DataFormats.XamlPackage);
            Sketch.Link(doc.Blocks, NoteStore.LoadSketches(id), NoteStore.GetAttachment);
            return doc;
        }

        private static string?[] Sketches(FlowDocument doc) =>
            Sketch.Images(doc.Blocks)
                .Select(i => Sketch.TryGetData(i, out var p) ? System.Text.Encoding.UTF8.GetString(p) : null)
                .ToArray();

        private static Image AddImage(FlowDocument doc, byte[]? payload)
        {
            var img = NewImage();
            if (payload != null) Sketch.SetData(img, payload);
            ((Paragraph)doc.Blocks.FirstBlock).Inlines.Add(new InlineUIContainer(img));
            return img;
        }

        private static Image NewImage() =>
            new() { Source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8) };

        private static byte[] Payload(string s) => System.Text.Encoding.UTF8.GetBytes(s);
    }
}
