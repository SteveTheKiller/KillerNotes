using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KillerNotes.Features.Security;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    // What a lock leaves behind belongs to one database. Held edits and tools go back only to that
    // database, once it is really open, and nothing another database does can replace or show them.
    [Collection(NoteStoreCollection.Name)]
    public sealed class HeldWorkTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N"));

        public HeldWorkTests()
        {
            Directory.CreateDirectory(_root);
            NoteStore.DemoDbFile = "notes.db";
        }

        public void Dispose()
        {
            NoteStore.Close();
            NoteStore.DbDirOverride = null;
            NoteStore.DemoDbFile = null;
            try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public void EditsFromASecondFailedLockLeaveTheFirstOnesAlone() => Sta.Run(() =>
        {
            var held = new HeldEdits();
            string a = OpenFolder("a", "secret a");
            long aNote = NoteStore.Create("A note");
            held.Hold(Capture("words for A", aNote, a));

            string b = OpenFolder("b", "secret b");
            long bNote = NoteStore.Create("B note");
            Assert.Equal(aNote, bNote);
            held.Hold(Capture("words for B", bNote, b));

            Assert.Equal(new[] { "words for A", "words for B" }, held.Titles);
            Assert.Equal("words for B\r\n", Restored(held, b, bNote));
            Assert.Equal("words for A\r\n", Restored(held, a, aNote));
            Assert.False(held.Any);
        });

        [Fact]
        public void HeldEditsSurviveAnyNumberOfSwitches() => Sta.Run(() =>
        {
            var held = new HeldEdits();
            string a = OpenFolder("a", "secret a");
            long note = NoteStore.Create("A note");
            held.Hold(Capture("words for A", note, a));

            OpenFolder("b", null);
            for (int i = 0; i < 3; i++)
            {
                Assert.Empty(held.For(NoteStore.DatabaseId));
                OpenFolder("c" + i, null);
            }

            Assert.Equal("words for A\r\n", Restored(held, a, note));
        });

        [Fact]
        public void TheSameFileNameAndRowInAnotherFolderGetsNothing() => Sta.Run(() =>
        {
            var held = new HeldEdits();
            string a = OpenFolder("a", "secret a");
            long note = NoteStore.Create("A note");
            held.Hold(Capture("private words", note, a));

            OpenFolder("b", null);
            long other = NoteStore.Create("B note");
            Assert.Equal(note, other);
            Assert.Equal("notes.db", NoteStore.ActiveDbFile);

            Assert.Empty(held.For(NoteStore.DatabaseId));
            var editor = new FlowDocument(new Paragraph(new Run("B's own words")));
            Assert.False(held.Titles.Count == 0);
            Assert.False(Held(held).TryRestore(editor, other, NoteStore.DatabaseId));
            Assert.Equal("B's own words\r\n", Text(editor));
        });

        [Fact]
        public void AFreshFileReplacingTheOwnerGetsNothing() => Sta.Run(() =>
        {
            var held = new HeldEdits();
            string a = OpenFolder("a", "secret a");
            string path = NoteStore.DbPath;
            long note = NoteStore.Create("A note");
            held.Hold(Capture("private words", note, a));
            NoteStore.Close();
            File.Delete(path);

            NoteStore.Open();

            Assert.Empty(held.For(NoteStore.DatabaseId));
            Assert.True(held.Any);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EditsFollowTheirDatabaseThroughARenameOrAMove(bool otherFolder) => Sta.Run(() =>
        {
            var held = new HeldEdits();
            string a = OpenFolder("a", "secret a");
            string path = NoteStore.DbPath;
            long note = NoteStore.Create("A note");
            held.Hold(Capture("words for A", note, a));
            NoteStore.Close();

            string folder = otherFolder ? Path.Combine(_root, "moved") : NoteStore.DbDir;
            Directory.CreateDirectory(folder);
            File.Move(path, Path.Combine(folder, "renamed.db"));
            NoteStore.DbDirOverride = folder;
            NoteStore.DemoDbFile = "renamed.db";
            NoteStore.Open("secret a");

            Assert.Equal("words for A\r\n", Restored(held, NoteStore.DatabaseId!, note));
        });

        [Fact]
        public void APlainCopyFromBeforeThePasswordGetsNothing() => Sta.Run(() =>
        {
            var held = new HeldEdits();
            OpenFolder("a", null);
            string path = NoteStore.DbPath;
            long note = NoteStore.Create("A note");
            string copy = Path.Combine(_root, "copy");
            Directory.CreateDirectory(copy);
            NoteStore.Close();
            File.Copy(path, Path.Combine(copy, "notes.db"));
            NoteStore.Open();
            NoteStore.SetPassword("secret a");
            held.Hold(Capture("private words", note, NoteStore.DatabaseId!));

            NoteStore.Close();
            NoteStore.DbDirOverride = copy;
            NoteStore.Open();

            Assert.False(NoteStore.HasPassword);
            Assert.Empty(held.For(NoteStore.DatabaseId));
        });

        [Fact]
        public void PicturesAndTheirPayloadsComeBackWithTheEdits() => Sta.Run(() =>
        {
            var held = new HeldEdits();
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            var picture = new Image { Source = bitmap, ToolTip = "payload marker" };
            var editor = new FlowDocument(new Paragraph(new InlineUIContainer(picture)));
            held.Hold(LockedEdits.Capture(editor, 3, "With a picture", "db-a", 0));
            Assert.Empty(editor.Blocks);

            var reopened = new FlowDocument();
            Assert.True(held.For("db-a").Single().TryRestore(reopened, 3, "db-a"));

            var restored = ((InlineUIContainer)((Paragraph)reopened.Blocks.First()).Inlines.First()).Child;
            Assert.Same(picture, restored);
            Assert.Equal("payload marker", picture.ToolTip);
        });

        [Fact]
        public void EditsWithNoKnownDatabaseAreNeverHandedToOne() => Sta.Run(() =>
        {
            var held = new HeldEdits();
            held.Hold(LockedEdits.Capture(new FlowDocument(), 1, "t", null, 0));

            Assert.Empty(held.For(null));
            Assert.Empty(held.For("db-a"));
            Assert.True(held.Any);
            Assert.False(held.All[0].TryRestore(new FlowDocument(), 1, null));
        });

        // Row ids are reused once a note is gone, and a note can change format while its edits
        // wait, so the id alone is not proof that this is still the note the edits came from.
        [Fact]
        public void EditsOnlyFitTheNoteTheyCameFrom() => Sta.Run(() =>
        {
            var created = new DateTime(2026, 10, 1, 9, 30, 0);
            var edits = LockedEdits.Capture(new FlowDocument(), 4, "t", "db-a", 0, created);

            Assert.True(edits.Fits(new KillerNotes.Models.Note { Created = created, Format = 0 }));
            Assert.False(edits.Fits(new KillerNotes.Models.Note { Created = created.AddSeconds(1), Format = 0 }));
            Assert.False(edits.Fits(new KillerNotes.Models.Note { Created = created, Format = 1 }));
            Assert.False(edits.Fits(new KillerNotes.Models.Note { Created = created, Format = 0, Deleted = DateTime.Now }));
        });

        [Theory]
        [InlineData(0, "Old title")]
        [InlineData(1, "Old title")]
        [InlineData(0, "")]
        public void EditsThatNoLongerFitTheirNoteGoIntoANewNoteOfTheirOwnDatabase(int format, string title) => Sta.Run(() =>
        {
            string a = OpenFolder("a", "secret a");
            long old = NoteStore.Create("Old title", format);
            NoteStore.Create("Later note");
            var edits = LockedEdits.Capture(new FlowDocument(new Paragraph(new Run("unsaved words"))), old, title, a, format);
            NoteStore.Delete(old);
            Assert.Null(NoteStore.GetNote(old));

            Assert.True(edits.MoveToNewNote("Untitled"));

            var made = NoteStore.GetNote(edits.NoteId)!;
            Assert.NotEqual(old, made.Id);
            Assert.Equal(title.Length == 0 ? "Untitled" : title, made.Title);
            Assert.Equal(format, made.Format);
            Assert.True(edits.Fits(made));
            var editor = new FlowDocument();
            Assert.True(edits.TryRestore(editor, made.Id, a));
            Assert.Equal("unsaved words\r\n", Text(editor));
        });

        [Fact]
        public void ADraftIsHandedBackOnlyToTheDatabaseItWasHeldFor()
        {
            var drafts = new HeldDrafts();
            drafts.Hold("db-a", "words for A");
            drafts.Hold("db-b", "words for B");

            Assert.Equal("", drafts.Take("db-c"));
            Assert.Equal("", drafts.Take((string?)null));
            Assert.Equal("words for B", drafts.Take("db-b"));
            Assert.Equal("", drafts.Take("db-b"));
            Assert.Equal("words for A", drafts.Take(null, "db-a", "path:x"));
        }

        [Fact]
        public void ADraftTypedMeanwhileStaysBesideTheHeldOne()
        {
            Assert.Equal("held", HeldDrafts.Join("held", ""));
            Assert.Equal("typed", HeldDrafts.Join("", "typed"));
            Assert.Equal("held" + Environment.NewLine + Environment.NewLine + "typed", HeldDrafts.Join("held", "typed"));
        }

        [Fact]
        public void OneStuckEntryDoesNotHideTheOthersForThatDatabase() => Sta.Run(() =>
        {
            var held = new HeldEdits();
            held.Hold(LockedEdits.Capture(new FlowDocument(), 1, "gone", "db-a", 0));
            held.Hold(LockedEdits.Capture(new FlowDocument(), 2, "fine", "db-a", 0));

            var walked = held.For("db-a");
            held.Done(walked[1]);    // finishing one while walking must not disturb the walk

            Assert.Equal(new[] { "gone", "fine" }, walked.Select(e => e.Title));
            Assert.Equal(new[] { "gone" }, held.Titles);
        });

        [Fact]
        public void TitlesAreOnlyShownForTheDatabaseTheyBelongTo() => Sta.Run(() =>
        {
            var held = new HeldEdits();
            held.Hold(LockedEdits.Capture(new FlowDocument(), 1, "private title", "db-a", 0));

            Assert.False(held.AllBelongTo("db-b"));
            Assert.False(held.AllBelongTo(null));
            Assert.True(held.AllBelongTo("db-a"));

            held.Hold(LockedEdits.Capture(new FlowDocument(), 2, "another", "db-b", 0));
            Assert.False(held.AllBelongTo("db-a"));
        });

        [Fact]
        public void HeldWorkFollowsItsDatabaseThroughAPasswordChange() => Sta.Run(() =>
        {
            var edits = new HeldEdits();
            var tools = new HeldTools();
            bool shown = false;
            edits.Hold(LockedEdits.Capture(new FlowDocument(), 1, "t", "old-id", 0));
            tools.Hold("old-id", "sketch", () => shown = true);

            edits.Rebind("old-id", "new-id");
            tools.Rebind("old-id", "new-id");

            Assert.Single(edits.For("new-id"));
            Assert.Empty(edits.For("old-id"));
            Assert.True(edits.All[0].TryRestore(new FlowDocument(), 1, "new-id"));
            tools.ReturnFor("old-id");
            Assert.False(shown);
            tools.ReturnFor("new-id");
            Assert.True(shown);
        });

        [Fact]
        public void TheDictationClipBelongsToOnePadAndNotToEveryPad()
        {
            var clip = typeof(KillerNotes.Controls.Dictation.DictationWindow).GetField("_clip",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(clip);
        }

        [Fact]
        public void AHeldToolStaysHiddenFromEveryOtherDatabase()
        {
            var tools = new HeldTools();
            var shown = new List<string>();
            tools.Hold("db-a", "sketch", () => { shown.Add("sketch"); return true; });

            tools.ReturnFor("db-b");
            tools.ReturnFor(null);
            Assert.Empty(shown);

            tools.ReturnFor("db-a");
            tools.ReturnFor("db-a");
            Assert.Equal(new[] { "sketch" }, shown);
        }

        [Fact]
        public void AToolWhoseSlotIsTakenWaitsForTheNextChance()
        {
            var tools = new HeldTools();
            bool slotFree = false;
            int shown = 0;
            tools.Hold("db-a", "sketch", () => { if (!slotFree) return false; shown++; return true; });

            tools.ReturnFor("db-a");
            Assert.Equal(0, shown);

            slotFree = true;
            tools.ReturnFor("db-a");
            Assert.Equal(1, shown);
        }

        [Fact]
        public void AToolHeldWithNoKnownDatabaseNeverComesBack()
        {
            var tools = new HeldTools();
            bool shown = false;
            tools.Hold(null, "sketch", () => shown = true);

            tools.ReturnFor(null);

            Assert.False(shown);
        }

        [Fact]
        public void AClosedToolIsForgotten()
        {
            var tools = new HeldTools();
            bool shown = false;
            tools.Hold("db-a", "sketch", () => shown = true);

            tools.Forget("sketch");
            tools.ReturnFor("db-a");

            Assert.False(shown);
        }

        private string OpenFolder(string name, string? password)
        {
            NoteStore.Close();
            NoteStore.DbDirOverride = Path.Combine(_root, name);
            Directory.CreateDirectory(NoteStore.DbDirOverride);
            NoteStore.Open(password);
            return NoteStore.DatabaseId!;
        }

        private static LockedEdits Capture(string words, long note, string database) =>
            LockedEdits.Capture(new FlowDocument(new Paragraph(new Run(words))), note, words, database, 0);

        private static LockedEdits Held(HeldEdits held) => held.All.Single();

        private static string Restored(HeldEdits held, string database, long note)
        {
            var edits = held.For(database).Single();
            var editor = new FlowDocument(new Paragraph(new Run("loaded from disk")));
            Assert.True(edits.TryRestore(editor, note, database));
            held.Done(edits);
            return Text(editor);
        }

        private static string Text(FlowDocument doc) => new TextRange(doc.ContentStart, doc.ContentEnd).Text;
    }
}
