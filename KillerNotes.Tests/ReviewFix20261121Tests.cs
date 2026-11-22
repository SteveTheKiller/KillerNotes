using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Models;
using KillerNotes.Services;
using KillerNotes.Shell;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Fixes from the 2026-11-21 review: exports, imports, search wording and sketch payloads.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class ReviewFix20261121Tests
    {
        // ---- BF-206 ----

        [Fact]
        public void SafeNameNeverCutsASurrogatePairInHalf()
        {
            string name = VaultExporter.SafeName(new string('a', 79) + "\U0001F600x");
            Assert.True(name.Length <= 80);
            Assert.False(char.IsHighSurrogate(name[name.Length - 1]));
        }

        // ---- BF-109 ----

        [Fact]
        public void DescribeShowsTheLastDayAsTyped()
        {
            Assert.Contains("before:9999-12-31", SearchQuery.Parse("before:9999-12-31").Describe(k => k));
            Assert.Contains("before:2026-06-30", SearchQuery.Parse("before:2026-06-30").Describe(k => k));
        }

        // ---- BF-305 ----

        [Theory]
        [InlineData(TextMarkerStyle.LowerLatin, "a")]
        [InlineData(TextMarkerStyle.UpperLatin, "A")]
        [InlineData(TextMarkerStyle.LowerRoman, "i")]
        [InlineData(TextMarkerStyle.UpperRoman, "I")]
        public void LetteredAndRomanListsExportAsOrderedLists(TextMarkerStyle style, string type) => Sta.Run(() =>
        {
            var list = new List { MarkerStyle = style, StartIndex = 3 };
            list.ListItems.Add(new ListItem(new Paragraph(new Run("one"))));
            var doc = new FlowDocument(list);

            string html = HtmlExport.FragmentFromDocument(doc);

            Assert.Contains("<ol type=\"" + type + "\" start=\"3\">", html);
            Assert.DoesNotContain("<ul", html);
        });

        // ---- BF-302 ----

        [Fact]
        public void FloatedContentReachesTheHtml() => Sta.Run(() =>
        {
            var floater = new Floater(new Paragraph(new Run("floated text")));
            var doc = new FlowDocument(new Paragraph(floater));

            Assert.Contains("floated text", HtmlExport.FragmentFromDocument(doc));
        });

        [Fact]
        public void RecordingChipsLeaveAPlaceholderInTheHtml() => Sta.Run(() =>
        {
            Border Chip() => new() { Child = new Grid(), Tag = new RecordingRef("abc", 65_000).ToTag() };
            var doc = new FlowDocument();
            doc.Blocks.Add(new BlockUIContainer(Chip()));
            doc.Blocks.Add(new Paragraph(new InlineUIContainer(Chip())));

            string html = HtmlExport.FragmentFromDocument(doc);

            Assert.Equal(2, html.Split(new[] { "1:05" }, StringSplitOptions.None).Length - 1);
        });

        // ---- BF-607 ----

        [Fact]
        public void AnHourLongRecordingKeepsItsHoursInTheHtml() => Sta.Run(() =>
        {
            var doc = new FlowDocument();
            doc.Blocks.Add(new BlockUIContainer(new Border { Child = new Grid(), Tag = new RecordingRef("abc", 3_900_000).ToTag() }));

            Assert.Contains(" 1:05:00]", HtmlExport.FragmentFromDocument(doc));
        });

        // ---- BF-602 follow-up ----

        // The re-key runs on a pool thread, which dies with the process. Closing waits for it,
        // so quitting right after a password change still moves every backup to the new key.
        [Fact]
        public void ClosingWaitsForARekeyStillRunning()
        {
            var controller = new KillerNotes.Features.Security.SecurityController(null!);
            bool finished = false;
            var running = System.Threading.Tasks.Task.Run(() => { System.Threading.Thread.Sleep(300); finished = true; });
            typeof(KillerNotes.Features.Security.SecurityController)
                .GetField("_rekey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(controller, running);

            controller.FinishRekey();

            Assert.True(finished);
        }

        // ---- BF-303 ----

        [Fact]
        public void ASketchPayloadWithMissingMembersStillOpens() => Sta.Run(() =>
        {
            var objs = SketchModel.Deserialize(Encoding.UTF8.GetBytes("[{\"Kind\":0},null,{\"Kind\":5,\"Text\":\"t\"}]"));

            Assert.Equal(2, objs.Count);
            foreach (var o in objs)
            {
                Assert.NotNull(o.Pts);
                Assert.NotNull(o.Color);
                Assert.True(o.Width > 0);
                Assert.True(o.FontSize > 0);
                o.Clone();
            }
        });

        // ---- BF-205 ----

        [Fact]
        public void AUtf16FileImportsWithItsFrontMatter()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kn-bom-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "Cafe.md");
                File.WriteAllText(path, "---\ntags: [a]\n---\nCafé", Encoding.Unicode);

                var plan = FolderImporter.PlanFile(path, new ImportState(new(), new(), new()));

                var note = Assert.Single(plan.Notes);
                Assert.Equal("Café", note.Body.Trim());
                Assert.Equal(new[] { "a" }, note.Tags);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ---- BF-413 ----

        [Fact]
        public void AnOrderedChecklistItemKeepsOneMarker() => Sta.Run(() =>
        {
            var list = new List { MarkerStyle = TextMarkerStyle.Decimal };
            list.ListItems.Add(new ListItem(new Paragraph(new Run(Checklist.Empty + " x"))));

            string md = MarkdownConvert.FromDocument(new FlowDocument(list));

            Assert.Equal("1. [ ] x\n", md);
        });

        [Fact]
        public void ANestedChecklistStaysNested() => Sta.Run(() =>
        {
            string md = MarkdownConvert.FromDocument(MarkdownConvert.ToDocument("- [ ] a\n  - [ ] b\n- [x] c\n", 14));

            Assert.Contains("\n  - [ ] b", "\n" + md);
            Assert.Contains("\n- [ ] a", "\n" + md);
            Assert.Contains("\n- [x] c", "\n" + md);
        });

        // ---- BF-103 ----

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AMarkdownNoteExportsRendered(bool legacyBlob) => Sta.Run(() =>
        {
            using var store = new TempStore();
            const string source = "# Title\n\n**bold** text";
            long id = NoteStore.Create("Md", Note.FormatMarkdown);
            byte[] blob = legacyBlob ? Encoding.UTF8.GetBytes(source) : MarkdownBlob.Encode(source);
            NoteStore.SaveMarkdown(id, "Md", blob, source);

            string html = HtmlExport.FragmentFromDocument(MainWindow.LoadExportDocument(id));

            Assert.Contains("Title", html);
            Assert.DoesNotContain("# Title", html);
            Assert.DoesNotContain("**bold**", html);
        });

        // ---- BF-106 ----

        [Fact]
        public void ImportImageRefusesABadGroupBeforeAddingANote() => Sta.Run(() =>
        {
            using var store = new TempStore();
            string png = TinyPng();
            try
            {
                int before = NoteStore.List().Count;
                var shell = new ShellProbe();

                Assert.Throws<ArgumentException>(() => shell.Window.ExecuteMcpCommand(new[]
                    { "import-image", "--path", png, "--group", new string('g', 241) }));

                Assert.Equal(before, NoteStore.List().Count);
            }
            finally { File.Delete(png); }
        });

        [Fact]
        public void ImportImageRemovesTheNoteWhenTheGroupFails() => Sta.Run(() =>
        {
            using var store = new TempStore();
            string png = TinyPng();
            try
            {
                int before = NoteStore.List().Count;
                using (var raw = new SqliteConnection(new SqliteConnectionStringBuilder
                    { DataSource = store.DbPath, Pooling = false }.ConnectionString))
                {
                    raw.Open();
                    using var command = raw.CreateCommand();
                    command.CommandText = "CREATE TRIGGER fail_group BEFORE UPDATE OF notebook ON notes " +
                        "BEGIN SELECT RAISE(ABORT, 'injected group failure'); END";
                    command.ExecuteNonQuery();
                }
                var shell = new ShellProbe();

                Assert.Throws<SqliteException>(() => shell.Window.ExecuteMcpCommand(new[]
                    { "import-image", "--path", png, "--group", "Pictures" }));

                Assert.Equal(before, NoteStore.List().Count);
            }
            finally { File.Delete(png); }
        });

        private static string TinyPng()
        {
            string path = Path.Combine(Path.GetTempPath(), "kn-img-" + Guid.NewGuid().ToString("N") + ".png");
            var bmp = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            enc.Save(fs);
            return path;
        }
    }
}
