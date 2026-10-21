using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Services;
using KillerNotes.Shell;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Vault export is the way out of KillerNotes, so it carries a note's pictures, sketches
    /// (as their image) and recordings (as audio files) beside the markdown, floated ones
    /// included, and names anything it could not carry in export-report.md instead of
    /// reporting a complete export.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class VaultExportMediaTests : IDisposable
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "kn-vault-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        [Fact]
        public void PicturesSketchesAndRecordingsAreWrittenBesideTheNote() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Site visit");
            var sketch = new Attachment(AttachmentKind.Sketch, new byte[] { 1, 2, 3 });
            var audio = new Attachment(AttachmentKind.Recording, Enumerable.Range(0, 40).Select(i => (byte)i).ToArray(), 65000);
            var p = new Paragraph(new Run("Rack photo "));
            p.Inlines.Add(new InlineUIContainer(Picture(null)));
            p.Inlines.Add(new InlineUIContainer(Picture(Attachments.Marker(AttachmentKind.Sketch, sketch.Hash))));
            p.Inlines.Add(new InlineUIContainer(Picture(Attachments.Marker(AttachmentKind.Recording, audio.Hash))));
            p.Inlines.Add(new Floater(new BlockUIContainer(Picture(null))));
            Save(id, new FlowDocument(p), sketch, audio);

            var (written, failed, losses) = Export();

            Assert.Equal((1, 0), (written, failed));
            Assert.Empty(losses);
            string md = File.ReadAllText(Path.Combine(_root, "Site visit.md"));
            var assets = Directory.GetFiles(Path.Combine(_root, "Site visit.assets")).Select(Path.GetFileName).ToList();
            Assert.Equal(3, assets.Count(a => a!.EndsWith(".png")));
            string audioFile = assets.Single(a => !a!.EndsWith(".png"))!;
            Assert.Equal(audio.Payload, File.ReadAllBytes(Path.Combine(_root, "Site visit.assets", audioFile)));
            Assert.Contains("Site visit.assets/" + audioFile, md);
            Assert.Contains("1:05", md);
            Assert.False(File.Exists(Path.Combine(_root, "export-report.md")));
        });

        [Fact]
        public void AnythingNotCarriedIsNamedInTheReport() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Lost audio");
            var missing = new Attachment(AttachmentKind.Recording, new byte[] { 9, 9, 9 }, 1000);
            var p = new Paragraph(new Run("Call notes "));
            p.Inlines.Add(new InlineUIContainer(Picture(Attachments.Marker(AttachmentKind.Recording, missing.Hash))));
            Save(id, new FlowDocument(p));   // the payload was never stored

            var (_, _, losses) = Export();

            Assert.Single(losses);
            string report = File.ReadAllText(Path.Combine(_root, "export-report.md"));
            Assert.Contains("Lost audio", report);
        });

        private (int, int, System.Collections.Generic.List<string>) Export()
        {
            var shell = (MainWindow)FormatterServices.GetUninitializedObject(typeof(MainWindow));
            Directory.CreateDirectory(_root);
            var result = typeof(MainWindow).GetMethod("ExportVaultTo", Members)!.Invoke(shell, new object[] { _root, NoteStore.List() })!;
            var t = result.GetType();
            return ((int)t.GetField("Item1")!.GetValue(result)!, (int)t.GetField("Item2")!.GetValue(result)!,
                    (System.Collections.Generic.List<string>)t.GetField("Item3")!.GetValue(result)!);
        }

        private static void Save(long id, FlowDocument doc, params Attachment[] attachments)
        {
            using var ms = new MemoryStream();
            new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.XamlPackage);
            NoteStore.SaveEditorState(id, NoteStore.GetNote(id)!.Title, ms.ToArray(), "", Array.Empty<string>(), attachments);
        }

        private static Image Picture(string? marker) => new()
        {
            Source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8),
            ToolTip = marker,
        };
    }
}
