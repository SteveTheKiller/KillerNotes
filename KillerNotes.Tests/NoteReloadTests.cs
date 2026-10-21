using System.Text;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public sealed class NoteReloadTests
    {
        [Fact]
        public void ReplacedNoteCanReloadAndSaveAfterLeavingSearchResults()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Example");
            NoteStore.Save(id, "Example", Encoding.UTF8.GetBytes("cat"), "cat");
            Assert.Equal(id, Assert.Single(NoteStore.List("cat")).Id);

            NoteStore.UpdateContents(new[] { (id, Encoding.UTF8.GetBytes("dog"), "dog") });

            Assert.Empty(NoteStore.List("cat"));
            var reloaded = NoteStore.GetNote(id);
            Assert.NotNull(reloaded);
            Assert.Equal("dog", reloaded!.Snippet);
            string edited = Encoding.UTF8.GetString(NoteStore.LoadContent(reloaded.Id)!) + " again";
            NoteStore.Save(reloaded.Id, reloaded.Title, Encoding.UTF8.GetBytes(edited), edited);
            Assert.Equal("dog again", Encoding.UTF8.GetString(NoteStore.LoadContent(id)!));
        }

        [Fact]
        public void UndoCanReloadANoteThatLeavesTheReplacementSearch()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Example");
            NoteStore.Save(id, "Example", Encoding.UTF8.GetBytes("cat"), "cat");
            var before = NoteStore.CaptureRow(id)!;
            NoteStore.UpdateContents(new[] { (id, Encoding.UTF8.GetBytes("dog"), "dog") });
            Assert.Equal(id, Assert.Single(NoteStore.List("dog")).Id);

            NoteStore.RestoreContents(new[] { before });

            Assert.Empty(NoteStore.List("dog"));
            var reloaded = NoteStore.GetNote(id);
            Assert.NotNull(reloaded);
            Assert.Equal("cat", reloaded!.Snippet);
            Assert.Equal("cat", Encoding.UTF8.GetString(NoteStore.LoadContent(reloaded.Id)!));
        }

        [Fact]
        public void MetadataLookupPreservesTrashAndFormatStateAndHandlesMissingNotes()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Example");
            NoteStore.SetFormat(id, 1, Encoding.UTF8.GetBytes("markdown"), "markdown");
            NoteStore.Trash(id);

            var note = NoteStore.GetNote(id);
            Assert.NotNull(note);
            Assert.True(note!.IsDeleted);
            Assert.Equal(1, note.Format);
            Assert.Null(NoteStore.GetNote(long.MaxValue));
            NoteStore.Close();
            Assert.Null(NoteStore.GetNote(id));
        }
    }
}
