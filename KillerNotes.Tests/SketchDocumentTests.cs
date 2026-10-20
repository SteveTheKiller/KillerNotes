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
    [Collection(NoteStoreCollection.Name)]
    public class SketchDocumentTests
    {
        [Fact]
        public void OpenSketchCannotPrintIntoAnotherDatabaseWithTheSameNoteId()
        {
            using var first = new TempStore();
            long original = NoteStore.Create("A");
            var session = new SketchSession(original);
            Assert.True(session.CanPrint(original));
            NoteStore.Close();
            NoteStore.DemoDbFile = "other.db";
            NoteStore.Open();
            long other = NoteStore.Create("B");
            Assert.Equal(original, other);
            Assert.False(session.CanPrint(other));
            NoteStore.Close();
            NoteStore.DemoDbFile = "test.db";
            NoteStore.Open();
            Assert.True(session.CanPrint(original));
            Assert.False(session.CanPrint(original + 1));
            NoteStore.Close();
            Assert.False(session.CanPrint(original));
        }

        [Fact]
        public void LegacyOrdinalsStayWithTheirImagesWhenNestedImagesPrecedeThem() => Sta.Run(() =>
        {
            var nested = new Image();
            var legacy = new Image();
            var doc = new FlowDocument(new Paragraph(new Floater(new BlockUIContainer(nested))));
            doc.Blocks.Add(new Paragraph(new InlineUIContainer(legacy)));
            Sketch.Link(doc.Blocks, new System.Collections.Generic.Dictionary<int, byte[]> { [0] = new byte[] { 7 } });
            Assert.False(Sketch.TryGetData(nested, out _));
            Assert.True(Sketch.TryGetData(legacy, out var bytes));
            Assert.Equal(new byte[] { 7 }, bytes);
            Sketch.SetData(nested, new byte[] { 9 });
            var saved = Sketch.Capture(doc.Blocks);
            Assert.Equal(new byte[] { 7 }, saved[0]);
            Assert.Equal(new byte[] { 9 }, saved[-1]);
            Assert.Equal(new[] { nested, legacy }, Sketch.Images(doc.Blocks).ToArray());
        });

        [Fact]
        public void FloatedSketchKeepsEditablePayloadAcrossDatabaseReopen() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("drawing");
            var image = new Image { Source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4) };
            Sketch.SetData(image, new byte[] { 10, 20, 30 });
            var doc = new FlowDocument(new Paragraph(new Floater(new BlockUIContainer(image))));
            using var bytes = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(bytes, DataFormats.XamlPackage);
            NoteStore.Save(id, "drawing", bytes.ToArray(), "");
            NoteStore.SaveSketches(id, Sketch.Capture(doc.Blocks));
            NoteStore.Close();
            NoteStore.Open();
            var reopened = new FlowDocument();
            using var saved = new MemoryStream(NoteStore.LoadContent(id)!);
            new TextRange(reopened.ContentStart, reopened.ContentEnd).Load(saved, DataFormats.XamlPackage);
            Sketch.Link(reopened.Blocks, NoteStore.LoadSketches(id));
            var restored = Assert.Single(Sketch.Images(reopened.Blocks));
            Assert.True(Sketch.TryGetData(restored, out var payload));
            Assert.Equal(new byte[] { 10, 20, 30 }, payload);
        });
    }
}
