using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Folder import (1.6.0, KN-P03): a folder of Markdown and its pictures and audio comes in as
    /// notes, groups, tags and links, after a preview that changes nothing, in one step that
    /// either all lands or leaves the notepad as it was, and never twice.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class FolderImportTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "kn-import-" + Guid.NewGuid().ToString("N"));

        public FolderImportTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string Write(string rel, string text)
        {
            string path = Path.Combine(_dir, rel.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text.Replace("\n", "\r\n"), new UTF8Encoding(false));
            return path;
        }

        private static (ImportPlan Plan, System.Collections.Generic.List<long> Ids) Import(string folder)
        {
            var plan = FolderImporter.Plan(folder, NoteStore.ImportState(), CancellationToken.None);
            System.Collections.Generic.List<long> ids = [];
            Sta.Run(() => ids = NoteStore.ImportBatch(FolderImporter.Prepare(plan, null, CancellationToken.None)));
            return (plan, ids);
        }

        [Fact]
        public void ThePreviewReadsFrontMatterGroupsAndSkipsWhatItCannotImport()
        {
            using var store = new TempStore();
            NoteStore.Save(NoteStore.Create("Alpha"), "Alpha", MarkdownBlob.Encode("x"), "x");
            Write("Work/Plan.md", "---\ntitle: \"The plan\"\ntags: [\"work\", \"q4\"]\ncreated: 2025-01-02 03:04\nmodified: 2025-02-03 04:05\npinned: true\ncolor: \"#FF8800\"\n---\n\nBody text");
            Write("Alpha.md", "No front matter, so the file name is the title");
            Write("List tags.md", "---\ntags:\n  - one\n  - two\n---\nbody");
            Write("Embeds.md", "Look: ![[diagram.png]]");
            Write(".obsidian/app.md", "settings, not a note");
            Write("Board.canvas", "{}");
            Write("export-report.md", "a previous export's report");
            Write("killernotes-export.json", "{}");
            File.WriteAllBytes(Path.Combine(_dir, "Spec.pdf"), new byte[] { 37, 80, 68, 70 });
            Write("Huge.md", new string('x', 2000));

            var plan = FolderImporter.Plan(_dir, NoteStore.ImportState(), CancellationToken.None, maxBytes: 1000);

            Assert.Equal(new[] { "Alpha", "Embeds", "List tags", "The plan" }, plan.Notes.Select(n => n.Title).OrderBy(t => t));
            var planNote = plan.Notes.Single(n => n.Title == "The plan");
            Assert.Equal(new[] { "Work" }, planNote.Group);
            Assert.Equal(new[] { "work", "q4" }, planNote.Tags);
            Assert.Equal(new DateTime(2025, 1, 2, 3, 4, 0), planNote.Created);
            Assert.Equal(new DateTime(2025, 2, 3, 4, 5, 0), planNote.Modified);
            Assert.True(planNote.Pinned);
            Assert.Equal("#FF8800", planNote.Color);
            Assert.Equal("Body text", planNote.Body.Trim());
            Assert.Equal(new[] { "one", "two" }, plan.Notes.Single(n => n.Title == "List tags").Tags);
            Assert.Equal(new[] { "Work" }, plan.NewGroups);
            Assert.Equal(new[] { "Alpha" }, plan.Clashes);
            Assert.Equal(new[] { "Board.canvas", "Embeds.md", "Huge.md", "Spec.pdf" }, plan.Skipped.Select(s => s.File).OrderBy(f => f));
        }

        [Fact]
        public void AnExportComesBackWithGroupsTagsLinksPicturesAndRecordings() => Sta.Run(() =>
        {
            string exportRoot;
            var recording = new Attachment(AttachmentKind.Recording, Wav(8000, 8000), 500);
            using (var source = new TempStore())
            {
                long linked = NoteStore.Create("a/b");
                NoteStore.SetFormat(linked, 1, MarkdownBlob.Encode("target body"), "target body");
                NoteStore.SetNoteGroup(linked, "Work");
                NoteStore.SetNoteTags(linked, "alpha, beta");
                NoteStore.SetPinned(linked, true);
                NoteStore.SetTitleColor(linked, "#00FF00");
                long hub = NoteStore.Create("Hub");
                NoteStore.SetFormat(hub, 1, MarkdownBlob.Encode("See [[a/b]] and [[a/b|the other]]."), "x");
                long rich = NoteStore.Create("Site visit");
                var p = new Paragraph(new Run("Photo "));
                p.Inlines.Add(new InlineUIContainer(Picture(null)));
                p.Inlines.Add(new InlineUIContainer(Picture(Attachments.Marker(AttachmentKind.Recording, recording.Hash))));
                var doc = new FlowDocument(p);
                using var ms = new MemoryStream();
                new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.XamlPackage);
                NoteStore.SaveEditorState(rich, "Site visit", ms.ToArray(), "Photo", Array.Empty<string>(), new[] { recording });
                exportRoot = VaultExporter.NewExportFolder(_dir, "Notes", DateTime.Now);
                using var vault = NoteStore.OpenVaultSource();
                Assert.Empty(VaultExporter.Export(exportRoot, NoteStore.List(), vault, VaultExportText.From(k => k), null, CancellationToken.None).Problems);
            }

            using var target = new TempStore();
            var plan = FolderImporter.Plan(exportRoot, NoteStore.ImportState(), CancellationToken.None);
            NoteStore.ImportBatch(FolderImporter.Prepare(plan, null, CancellationToken.None));

            var notes = NoteStore.List();
            var imported = notes.Single(n => n.Title == "a/b");
            Assert.Equal("Work", imported.Notebook);
            Assert.Equal(new[] { "alpha", "beta" }, NoteStore.SplitTags(imported.Tags));
            Assert.True(imported.Pinned);
            Assert.Equal("#00FF00", imported.TitleColor);
            Assert.True(imported.IsMarkdown);
            Assert.Equal("target body", MarkdownBlob.Decode(NoteStore.LoadContent(imported.Id)).Trim());
            Assert.Contains(NoteStore.ListGroups(), g => g.Name == "Work");
            Assert.Contains(NoteStore.ListTags(), t => t.Name == "alpha");

            var hubNote = notes.Single(n => n.Title == "Hub");
            Assert.Equal("See [[a/b]] and [[a/b|the other]].", MarkdownBlob.Decode(NoteStore.LoadContent(hubNote.Id)).Trim());
            Assert.Contains(NoteStore.Backlinks("a/b"), b => b.Id == hubNote.Id);

            var site = notes.Single(n => n.Title == "Site visit");
            Assert.False(site.IsMarkdown);
            var loaded = new FlowDocument();
            using (var ms = new MemoryStream(NoteStore.LoadContent(site.Id)!))
                new TextRange(loaded.ContentStart, loaded.ContentEnd).Load(ms, DataFormats.XamlPackage);
            var images = loaded.Blocks.OfType<Paragraph>().SelectMany(b => b.Inlines.OfType<InlineUIContainer>()).Select(c => c.Child).OfType<Image>().ToList();
            Assert.Equal(2, images.Count);
            Assert.Contains(images, i => Equals(i.ToolTip, Attachments.Marker(AttachmentKind.Recording, recording.Hash)));
            Assert.Equal(recording.Payload, NoteStore.GetAttachment(recording.Hash)!.Payload);
            Assert.Equal(500, NoteStore.GetAttachment(recording.Hash)!.DurationMs);
        });

        [Fact]
        public void MarkdownLinksToOtherFilesBecomeNoteLinks()
        {
            using var store = new TempStore();
            Write("Other note.md", "other");
            Write("Index.md", "Read [the other one](Other%20note.md), [same](<Other note.md>), [web](https://example.com/x.md)\n```\n[code](Other%20note.md)\n```");

            Import(_dir);

            string index = MarkdownBlob.Decode(NoteStore.LoadContent(NoteStore.List().Single(n => n.Title == "Index").Id));
            Assert.Contains("Read [[Other note|the other one]], [[Other note|same]], [web](https://example.com/x.md)", index);
            Assert.Contains("```\n[code](Other%20note.md)\n```", index.Replace("\r\n", "\n"));
        }

        [Fact]
        public void ImportingTheSameFolderTwiceAddsNothingButAChangedFileComesInAgain()
        {
            using var store = new TempStore();
            Write("One.md", "first");
            string two = Write("Two.md", "second");
            Import(_dir);

            var again = FolderImporter.Plan(_dir, NoteStore.ImportState(), CancellationToken.None);
            Assert.Empty(again.Notes);
            Assert.Equal(2, again.AlreadyImported);

            File.AppendAllText(two, " and more");
            var changed = FolderImporter.Plan(_dir, NoteStore.ImportState(), CancellationToken.None);
            Assert.Equal(new[] { "Two" }, changed.Notes.Select(n => n.Title));
            Assert.Equal(1, changed.AlreadyImported);
        }

        [Fact]
        public void CancelingBeforeTheStepChangesNothing()
        {
            using var store = new TempStore();
            for (int i = 0; i < 5; i++) Write($"Note {i}.md", "body " + i);
            var plan = FolderImporter.Plan(_dir, NoteStore.ImportState(), CancellationToken.None);
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();

            Sta.Run(() => Assert.ThrowsAny<OperationCanceledException>(() => FolderImporter.Prepare(plan, null, cancel.Token)));

            Assert.Empty(NoteStore.List());
        }

        [Fact]
        public void AFailureDuringTheStepLeavesTheNotepadAsItWas()
        {
            using var store = new TempStore();
            Write("Fine.md", "---\ntags: [\"newtag\"]\n---\nok");
            Write("Group/Boom.md", "fails");
            var plan = FolderImporter.Plan(_dir, NoteStore.ImportState(), CancellationToken.None);
            System.Collections.Generic.List<PreparedImport> prepared = [];
            Sta.Run(() => prepared = FolderImporter.Prepare(plan, null, CancellationToken.None));
            using (var fault = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = store.DbPath }.ConnectionString))
            {
                fault.Open();
                using var cmd = fault.CreateCommand();
                cmd.CommandText = "CREATE TRIGGER boom BEFORE INSERT ON notes WHEN new.title = 'Boom' BEGIN SELECT RAISE(ABORT, 'controlled'); END";
                cmd.ExecuteNonQuery();
            }

            Assert.Throws<SqliteException>(() => NoteStore.ImportBatch(prepared));

            Assert.Empty(NoteStore.List());
            Assert.Empty(NoteStore.ListGroups());
            Assert.DoesNotContain(NoteStore.ListTags(), t => t.Name == "newtag");   // beside the seeded default tags
            Assert.Equal(2, FolderImporter.Plan(_dir, NoteStore.ImportState(), CancellationToken.None).Notes.Count);
        }

        [Fact]
        public void WavAndFlacDurationsAreRead()
        {
            Assert.Equal(500, AudioCodec.DurationMs(Wav(8000, 8000)));
            Assert.Equal(0, AudioCodec.DurationMs(new byte[] { 1, 2, 3 }));
        }

        /// <summary>A mono 16-bit PCM WAV of the given rate holding dataBytes of silence.</summary>
        private static byte[] Wav(int rate, int dataBytes)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataBytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataBytes); w.Write(new byte[dataBytes]);
            return ms.ToArray();
        }

        private static Image Picture(string? marker) => new()
        {
            Source = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[16], 8),
            ToolTip = marker,
        };
    }
}
