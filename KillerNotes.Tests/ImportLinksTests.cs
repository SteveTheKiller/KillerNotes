using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Models;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// An imported note's wikilinks count straight away: outgoing links, backlinks and graph
    /// edges, without the note having to be opened and edited first. A [[...]] written inside a
    /// sketch is a caption, not a link, the same rule as a normal save.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class ImportLinksTests
    {
        [Fact]
        public void ImportedNotesLinkImmediately() => Sta.Run(() =>
        {
            string knote = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".knote");
            try
            {
                using (new TempStore())
                {
                    long id = NoteStore.Create("Imported");
                    var sketch = new Attachment(AttachmentKind.Sketch, SketchModel.Serialize(new[]
                    {
                        new SketchObject { Kind = SketchKind.Text, Text = "[[Ghost]]" },
                    }));
                    var img = new Image { Source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8),
                                          ToolTip = Attachments.Marker(AttachmentKind.Sketch, sketch.Hash) };
                    var doc = new FlowDocument(new Paragraph(new Run("see [[Target]] and [[Ünïcode]] ")));
                    ((Paragraph)doc.Blocks.FirstBlock).Inlines.Add(new InlineUIContainer(img));
                    using var ms = new MemoryStream();
                    new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.XamlPackage);
                    NoteStore.SaveEditorState(id, "Imported", ms.ToArray(), "see [[Target]] and [[Ünïcode]] \n[[Ghost]]",
                        new[] { "Target", "Ünïcode" }, new[] { sketch });
                    NoteStore.ExportNote(id, knote, null);
                }

                using var store = new TempStore();
                long target = NoteStore.Create("Target");
                long unicode = NoteStore.Create("Ünïcode");   // case-insensitive Unicode matching: UnicodeSearchTests
                NoteStore.Create("Ghost");
                Assert.Equal(1, NoteStore.ImportNotes(knote, null));
                long imported = NoteStore.List().Single(n => n.Title == "Imported").Id;

                Assert.Equal(new[] { target, unicode }.OrderBy(i => i), NoteStore.OutgoingLinks(imported).Select(l => l.Id).OrderBy(i => i));
                Assert.Contains(NoteStore.Backlinks("Target"), b => b.Id == imported);
                Assert.DoesNotContain(NoteStore.Backlinks("Ghost"), b => b.Id == imported);
            }
            finally { try { File.Delete(knote); } catch { } }
        });

        [Fact]
        public void NotesImportedBeforeTheFixAreIndexedOnOpen()
        {
            using var store = new TempStore();
            long target = NoteStore.Create("Target");
            long old = NoteStore.Create("Old import");
            NoteStore.Save(old, "Old import", new byte[] { 1 }, "points at [[Target]]");   // no links, as an old import left it
            Assert.Empty(NoteStore.OutgoingLinks(old));

            NoteStore.Close();
            NoteStore.Open();
            Assert.Equal(target, Assert.Single(NoteStore.OutgoingLinks(old)).Id);
        }
    }
}
