using System.Linq;
using System.Text;
using KillerNotes.Models;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The open-time rewrap (MarkdownMigration.cs).
    ///
    /// The point of the pass is that it does not wait for anybody to edit anything. Writing
    /// packages only on save leaves a note nobody touches raw forever, and one raw note is enough
    /// to crash every build older than 1.3.0 that opens the database. So the assertion that
    /// matters is EveryRawMarkdownNoteIsRewrappedWithoutBeingEdited: no edit, no save, and the
    /// blob comes out a package anyway.
    ///
    /// In the NoteStore collection because it drives the static store, and on STA because the
    /// rewrap writes XamlPackages.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class MarkdownMigrationTests
    {
        private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

        [Fact]
        public void EveryRawMarkdownNoteIsRewrappedWithoutBeingEdited() => Sta.Run(() =>
        {
            using var store = new TempStore();
            const string source = "# heading\n\n\tindented\n\ntrailing  ";
            long id = NoteStore.Create("md", Note.FormatMarkdown);
            NoteStore.SetFormat(id, Note.FormatMarkdown, Utf8(source), source);
            Assert.False(MarkdownBlob.IsPackage(NoteStore.LoadContent(id)));

            Assert.Equal(1, MarkdownMigration.RewrapRawMarkdown());

            byte[] after = NoteStore.LoadContent(id)!;
            Assert.True(MarkdownBlob.IsPackage(after));
            Assert.Equal(source, MarkdownBlob.Decode(after));
        });

        /// <summary>The stamp has to survive: the rewrap is a storage change, not an edit, and a
        /// bumped modified would reorder the sidebar and change which note the app opens into.</summary>
        [Fact]
        public void TheModifiedStampIsNotTouched() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("md", Note.FormatMarkdown);
            NoteStore.SetFormat(id, Note.FormatMarkdown, Utf8("# note"), "# note");
            var before = NoteStore.List().Single(n => n.Id == id).Modified;

            MarkdownMigration.RewrapRawMarkdown();

            Assert.Equal(before, NoteStore.List().Single(n => n.Id == id).Modified);
        });

        [Fact]
        public void RichNotesAreLeftAlone() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("rich");
            // Not a package, but format 0, so the pass must not touch it: a rich blob is never
            // markdown source and re-encoding one would destroy it.
            NoteStore.Save(id, "rich", Utf8("<not a package>"), "body");

            Assert.Equal(0, MarkdownMigration.RewrapRawMarkdown());
            Assert.Equal("<not a package>", new UTF8Encoding(false).GetString(NoteStore.LoadContent(id)!));
        });

        [Fact]
        public void ASecondPassDoesNothing() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("md", Note.FormatMarkdown);
            NoteStore.SetFormat(id, Note.FormatMarkdown, Utf8("# note"), "# note");

            Assert.Equal(1, MarkdownMigration.RewrapRawMarkdown());
            Assert.Equal(0, MarkdownMigration.RewrapRawMarkdown());
        });

        [Fact]
        public void AnEmptyMarkdownNoteIsNotAMigrationCandidate() => Sta.Run(() =>
        {
            using var store = new TempStore();
            NoteStore.Create("md", Note.FormatMarkdown);   // created with no content at all
            Assert.Equal(0, MarkdownMigration.RewrapRawMarkdown());
        });

        [Fact]
        public void ADatabaseWithNothingToDoReportsNothing() => Sta.Run(() =>
        {
            using var store = new TempStore();
            NoteStore.Create("rich");
            Assert.Equal(0, MarkdownMigration.RewrapRawMarkdown());
        });
    }
}
