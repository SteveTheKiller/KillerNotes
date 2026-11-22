using System.Linq;
using System.Text;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Undo of a cross-note Replace All or a rename's link rewrite sits on the app's undo stack
    /// long after the replace. It may only put the old text back while every note still holds
    /// what the replace wrote; once one has been edited since, the undo refuses rather than
    /// silently writing over that work.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class UndoReplaceTests
    {
        private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

        [Fact]
        public void UndoPutsTheOldTextBackWhenNothingChangedSince()
        {
            using var _ = new TempStore();
            long a = NoteStore.Create("A", B("old"), "old");
            var before = new[] { NoteStore.CaptureRow(a)! };
            var written = new[] { (a, B("new"), "new") };
            NoteStore.UpdateContents(written);

            Assert.True(NoteStore.RestoreContentsIfUnchanged(before, written.Select(w => (w.Item1, w.Item2))));

            Assert.Equal("old", NoteStore.CaptureRow(a)!.Plain);
        }

        [Fact]
        public void UndoRefusesAndKeepsEveryNoteWhenOneWasEditedSince()
        {
            using var _ = new TempStore();
            long a = NoteStore.Create("A", B("old a"), "old a");
            long b = NoteStore.Create("B", B("old b"), "old b");
            var before = new[] { NoteStore.CaptureRow(a)!, NoteStore.CaptureRow(b)! };
            var written = new[] { (a, B("new a"), "new a"), (b, B("new b"), "new b") };
            NoteStore.UpdateContents(written);
            NoteStore.Save(b, "B", B("typed later"), "typed later");

            Assert.False(NoteStore.RestoreContentsIfUnchanged(before, written.Select(w => (w.Item1, w.Item2))));

            Assert.Equal("new a", NoteStore.CaptureRow(a)!.Plain);
            Assert.Equal("typed later", NoteStore.CaptureRow(b)!.Plain);
        }
    }
}
