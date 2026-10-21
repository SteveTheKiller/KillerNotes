using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using KillerNotes.Services;
using Microsoft.Win32;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Capture (1.6.0, KN-P04): the quick note's rules, appending without touching the open edit,
    /// the launch and pipe routes, Open with for Markdown and text files, and the global shortcut
    /// choices. Window and tray behavior is checked by hand.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class QuickCaptureTests
    {
        [Theory]
        [InlineData("Buy milk\nand eggs", "Buy milk")]
        [InlineData("\n\n  Spaced title  \nbody", "Spaced title")]
        [InlineData("   \n  ", "Untitled")]
        public void TheFirstLineIsTheTitle(string text, string title) =>
            Assert.Equal(title, QuickCapture.TitleOf(text, "Untitled"));

        [Fact]
        public void ALongFirstLineIsCutAtAWord()
        {
            string title = QuickCapture.TitleOf(string.Join(" ", Enumerable.Repeat("word", 60)), "Untitled");
            Assert.True(title.Length <= QuickCapture.MaxTitle);
            Assert.EndsWith("word", title);
        }

        [Fact]
        public void TheSizeLimitIsOneMegabyteOfText()
        {
            Assert.False(QuickCapture.TooLarge(new string('a', 1024 * 1024)));
            Assert.True(QuickCapture.TooLarge(new string('a', 1024 * 1024 + 1)));
            Assert.True(QuickCapture.TooLarge(new string('é', 600 * 1024)));   // two bytes each in UTF-8
        }

        [Fact]
        public void ANewQuickNoteLandsInItsGroupAsMarkdownWithEveryWordKept()
        {
            using var store = new TempStore();
            long id = QuickCapture.CreateNote("Inbox" + NoteStore.GroupSep + "Ideas", "Call Sam\nabout [[Plan]]", "Untitled");

            var note = NoteStore.List().Single(n => n.Id == id);
            Assert.Equal("Call Sam", note.Title);
            Assert.Equal("Inbox" + NoteStore.GroupSep + "Ideas", note.Notebook);
            Assert.True(note.IsMarkdown);
            Assert.Equal("Call Sam\nabout [[Plan]]", MarkdownBlob.Decode(NoteStore.LoadContent(id)));
            Assert.Contains(NoteStore.ListGroups(), g => g.Name == "Inbox");
            Assert.Contains(NoteStore.ListGroups(), g => g.Name == "Inbox" + NoteStore.GroupSep + "Ideas");
            Assert.Equal(new[] { "Plan" }, NoteStore.OutgoingLinks(id).Select(l => l.Title));
        }

        [Fact]
        public void AppendingAddsToAMarkdownNote()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("2026-10-21", 1);
            NoteStore.SetFormat(id, 1, MarkdownBlob.Encode("Morning notes"), "Morning notes");

            Sta.Run(() => QuickCapture.Append(id, "Later thought"));

            Assert.Equal("Morning notes\n\nLater thought", MarkdownBlob.Decode(NoteStore.LoadContent(id)));
            Assert.Contains("Later thought", NoteStore.LoadPlain(id));
        }

        [Fact]
        public void AppendingAddsParagraphsToARichNoteAndKeepsItsRecording() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Daily");
            var recording = new Attachment(AttachmentKind.Recording, new byte[] { 1, 2, 3 }, 1000);
            AttachmentStoreTests.SaveWith(id, recording);

            QuickCapture.Append(id, "first\nsecond");

            var doc = new FlowDocument();
            using (var ms = new MemoryStream(NoteStore.LoadContent(id)!))
                new TextRange(doc.ContentStart, doc.ContentEnd).Load(ms, DataFormats.XamlPackage);
            var paragraphs = doc.Blocks.OfType<Paragraph>().Select(p => new TextRange(p.ContentStart, p.ContentEnd).Text).ToList();
            Assert.Equal("first", paragraphs[paragraphs.Count - 2]);
            Assert.Equal("second", paragraphs[paragraphs.Count - 1]);
            Assert.NotNull(NoteStore.GetAttachment(recording.Hash));
            Assert.Contains("second", NoteStore.LoadPlain(id));
        });

        [Fact]
        public void AFileOpenedAgainFindsTheNoteItBecame()
        {
            using var store = new TempStore();
            string dir = Path.Combine(Path.GetTempPath(), "kn-open-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string file = Path.Combine(dir, "Opened.md");
                File.WriteAllText(file, "# Opened", new UTF8Encoding(false));
                var plan = FolderImporter.PlanFile(file, NoteStore.ImportState());
                Assert.Single(plan.Notes);
                long id = 0;
                Sta.Run(() => id = NoteStore.ImportBatch(FolderImporter.Prepare(plan, null, System.Threading.CancellationToken.None)).Single());

                Assert.Equal(id, NoteStore.ImportedNoteFor(plan.Notes[0].SourceKey, plan.Notes[0].Hash));
                var again = FolderImporter.PlanFile(file, NoteStore.ImportState());
                Assert.Empty(again.Notes);
                Assert.Equal(1, again.AlreadyImported);

                NoteStore.Trash(id);
                Assert.Null(NoteStore.ImportedNoteFor(plan.Notes[0].SourceKey, plan.Notes[0].Hash));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        [Fact]
        public void ATextFileComesInAsPlainText()
        {
            using var store = new TempStore();
            string file = Path.Combine(Path.GetTempPath(), "kn-text-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(file, "---\nnot: front matter\n---\nline", new UTF8Encoding(false));
            try
            {
                var plan = FolderImporter.PlanFile(file, NoteStore.ImportState());
                Assert.Equal("---\nnot: front matter\n---\nline", plan.Notes.Single().Body);
                Assert.Equal(Path.GetFileNameWithoutExtension(file), plan.Notes.Single().Title);
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void TheSameOpenTwiceWithinAMinuteIsOneRequest()
        {
            var recent = new RecentOpens();
            var now = new DateTime(2026, 10, 21, 9, 0, 0);
            Assert.True(recent.First("c:\\a.md", "h1", now));
            Assert.False(recent.First("C:\\A.md", "h1", now.AddSeconds(30)));
            Assert.True(recent.First("c:\\a.md", "h2", now.AddSeconds(31)));    // the file changed
            Assert.True(recent.First("c:\\a.md", "h1", now.AddSeconds(91)));    // a minute later
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("Win+Alt+N", true)]
        [InlineData("Win+Alt+J", false)]    // offered once, before the choice became one key
        [InlineData("Ctrl+Alt+N", false)]   // AltGr+N on international layouts
        [InlineData("junk", false)]
        public void OnlyTheOfferedShortcutsAreAccepted(string? stored, bool valid) =>
            Assert.Equal(valid, QuickNoteHotkey.TryParse(stored, out _, out _));

        [Fact]
        public void QuickNoteLaunchesAreRecognizedAndForwarded()
        {
            Assert.True(QuickCapture.IsQuickNoteLaunch(new[] { "--quick-note" }));
            Assert.True(QuickCapture.IsQuickNoteLaunch(new[] { "/QUICK-NOTE" }));
            Assert.False(QuickCapture.IsQuickNoteLaunch(new[] { "notes.md" }));
            // Not a path any file could have: a Windows name cannot hold ':' after the drive.
            Assert.StartsWith("kn:", QuickCapture.PipeMessage);
        }

        [Fact]
        public void OpenWithAddsKillerNotesForMarkdownAndTextWithoutTakingThemOver()
        {
            string branch = @"Software\KillerNotesTests\" + Guid.NewGuid().ToString("N");
            using var root = Registry.CurrentUser.CreateSubKey(branch);
            try
            {
                using (var md = root.CreateSubKey(@"Software\Classes\.md")) md.SetValue("", "SomeEditor.md");
                var app = typeof(KillerNotes.App);
                var register = app.GetMethod("RegisterFileAssociations", BindingFlags.NonPublic | BindingFlags.Static, null,
                    new[] { typeof(RegistryKey), typeof(string), typeof(string) }, null)!;
                var unregister = app.GetMethod("UnregisterFileAssociations", BindingFlags.NonPublic | BindingFlags.Static)!;

                register.Invoke(null, new object[] { root, @"C:\Apps\KillerNotes.exe", Path.GetTempPath() });

                foreach (string ext in new[] { ".md", ".markdown", ".txt" })
                    using (var open = root.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids"))
                        Assert.Contains("KillerNotes.Text", open!.GetValueNames());
                using (var md = root.OpenSubKey(@"Software\Classes\.md")) Assert.Equal("SomeEditor.md", md!.GetValue(""));
                using (var cmd = root.OpenSubKey(@"Software\Classes\KillerNotes.Text\shell\open\command"))
                    Assert.Equal("\"C:\\Apps\\KillerNotes.exe\" \"%1\"", cmd!.GetValue(""));

                unregister.Invoke(null, new object[] { root });

                Assert.Null(root.OpenSubKey(@"Software\Classes\KillerNotes.Text"));
                using (var open = root.OpenSubKey(@"Software\Classes\.md\OpenWithProgids"))
                    Assert.DoesNotContain("KillerNotes.Text", open?.GetValueNames() ?? Array.Empty<string>());
                using (var md = root.OpenSubKey(@"Software\Classes\.md")) Assert.Equal("SomeEditor.md", md!.GetValue(""));
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(branch, false); }
        }
    }
}
