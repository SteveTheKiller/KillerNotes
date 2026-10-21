using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Vault export as an exit path (1.6.0, KN-P02): a fresh dated folder each time, links that
    /// still land after file names change, a stable id in each note, a manifest, a report that is
    /// always written, cancel that leaves nothing behind, and a check of what was written.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class VaultExportTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "kn-vault-" + Guid.NewGuid().ToString("N"));
        private static readonly VaultExportText Text = VaultExportText.From(key => key);

        public VaultExportTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public void EveryExportGetsItsOwnDatedFolder()
        {
            var when = new DateTime(2026, 10, 21, 14, 30, 0);
            string first = VaultExporter.NewExportFolder(_dir, "Notes", when);
            Assert.Equal(Path.Combine(_dir, "Notes 2026-10-21 1430"), first);
            Directory.CreateDirectory(first);
            Assert.Equal(Path.Combine(_dir, "Notes 2026-10-21 1430 (2)"), VaultExporter.NewExportFolder(_dir, "Notes", when));
        }

        [Fact]
        public void LinksAreRewrittenOnlyWhereTheFileNameWouldNotFindTheNote() => Sta.Run(() =>
        {
            using var store = new TempStore();
            Markdown("Plain", "plain body");
            Markdown("a/b", "slash title");
            Markdown("Dup", "first dup");
            Markdown("Dup", "second dup");
            long work = Markdown("Same", "work copy");
            NoteStore.SetNoteGroup(work, "Work");
            long home = Markdown("Same", "home copy");
            NoteStore.SetNoteGroup(home, "Home");
            Markdown("Hub", "[[Plain]] [[a/b]] [[Dup]] [[Same]] [[Missing]] [[plain|shown]]\n"
                + "| col |\n| --- |\n| [[a/b]] |\n"
                + "```\n[[a/b]]\n```\n");

            var result = Export();

            string hub = File.ReadAllText(Path.Combine(result.Root, "Hub.md"));
            Assert.Contains("[[Plain]] [[a-b|a/b]] [[Dup]] [[Work/Same|Same]] [[Missing]] [[plain|shown]]", hub);
            Assert.Contains("| [[a-b\\|a/b]] |", hub);
            Assert.Contains("```\n[[a/b]]\n```", hub);
        });

        [Fact]
        public void FrontMatterCarriesTheIdPinAndColor() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = Markdown("Pinned one", "body");
            NoteStore.SetPinned(id, true);
            NoteStore.SetTitleColor(id, "#FF8800");

            var result = Export();

            string md = File.ReadAllText(Path.Combine(result.Root, "Pinned one.md"));
            Assert.StartsWith("---\n", md);
            Assert.Contains("killernotes-id: " + id + "\n", md);
            Assert.Contains("pinned: true\n", md);
            Assert.Contains("color: \"#FF8800\"\n", md);
        });

        [Fact]
        public void TheManifestListsEveryNoteAndItsFile() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long a = Markdown("Alpha", "a");
            long b = Markdown("Beta", "b");
            NoteStore.SetNoteGroup(b, "Work");

            var result = Export();

            var manifest = VaultExporter.ReadManifest(Path.Combine(result.Root, VaultExporter.ManifestName));
            Assert.Equal(VaultExporter.FormatVersion, manifest.FormatVersion);
            Assert.Equal(new[] { (a, "Alpha.md"), (b, "Work/Beta.md") },
                manifest.Notes.OrderBy(n => n.Id).Select(n => (n.Id, n.File)));
            Assert.Equal("markdown", manifest.Notes[0].Format);
        });

        [Fact]
        public void TheReportIsAlwaysWrittenAndSaysWhatIsLeftOut() => Sta.Run(() =>
        {
            using var store = new TempStore();
            Markdown("Clean", "nothing special");

            var result = Export();

            string report = File.ReadAllText(Path.Combine(result.Root, "export-report.md"));
            Assert.Contains("Str_Vault_NothingLost", report);
            Assert.Contains("Str_Vault_NotIncluded", report);
            Assert.Empty(result.Problems);
        });

        [Fact]
        public void CancelLeavesNoFolderBehind() => Sta.Run(() =>
        {
            using var store = new TempStore();
            for (int i = 0; i < 5; i++) Markdown("Note " + i, "body");
            using var cancel = new CancellationTokenSource();
            string root = VaultExporter.NewExportFolder(_dir, "Notes", DateTime.Now);

            using var source = NoteStore.OpenVaultSource();
            var result = VaultExporter.Export(root, NoteStore.List(), source, Text,
                new SyncProgress(done => { if (done == 2) cancel.Cancel(); }), cancel.Token);

            Assert.True(result.Canceled);
            Assert.False(Directory.Exists(root));
        });

        [Fact]
        public void TheCheckFindsAMissingAttachmentFile() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Rec");
            AttachmentStoreTests.SaveWith(id, new Attachment(AttachmentKind.Recording, new byte[] { 1, 2, 3, 4 }, 2000));
            var result = Export();
            Assert.Empty(result.Problems);

            foreach (string file in Directory.GetFiles(Path.Combine(result.Root, "Rec.assets"), "recording-*")) File.Delete(file);

            var problems = VaultExporter.Check(result.Root);
            Assert.Contains(problems, p => p.Contains("Rec.md") && p.Contains("recording-1"));
        });

        [Fact]
        public void AnEncryptedNotepadExportsThroughItsOwnConnectionOnAnotherThread()
        {
            using var store = new TempStore("pw");
            NoteStore.Save(NoteStore.Create("Secret"), "Secret", MarkdownBlob.Encode("hidden"), "hidden");
            NoteStore.SetFormat(NoteStore.List().Single().Id, 1, MarkdownBlob.Encode("hidden"), "hidden");
            var notes = NoteStore.List();
            string root = VaultExporter.NewExportFolder(_dir, "Notes", DateTime.Now);
            VaultExportResult? result = null;

            using (var source = NoteStore.OpenVaultSource())
                Sta.Run(() => result = VaultExporter.Export(root, notes, source, Text, null, CancellationToken.None));

            Assert.True(NoteStore.IsOpen);
            Assert.Equal(1, result!.Written);
            Assert.Contains("hidden", File.ReadAllText(Path.Combine(root, "Secret.md")));
        }

        private long Markdown(string title, string body)
        {
            long id = NoteStore.Create(title);
            NoteStore.SetFormat(id, 1, MarkdownBlob.Encode(body), body);
            return id;
        }

        private VaultExportResult Export()
        {
            string root = VaultExporter.NewExportFolder(_dir, "Notes", DateTime.Now);
            using var source = NoteStore.OpenVaultSource();
            return VaultExporter.Export(root, NoteStore.List(), source, Text, null, CancellationToken.None);
        }

        /// <summary>Reports on the calling thread, unlike Progress&lt;T&gt;, which posts.</summary>
        private sealed class SyncProgress(Action<int> report) : IProgress<int>
        {
            public void Report(int value) => report(value);
        }
    }
}
