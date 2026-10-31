using System.Linq;
using KillerNotes.Cli;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>The command line reads notes the way the app does: links match across case in every
    /// script, tag counts match the exact tag, and a cut never splits a character.</summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class CliQueryTests
    {
        [Theory]
        [InlineData("ΩMEGA plan", "ωmega plan")]
        [InlineData("ПРИВЕТ мир", "привет мир")]
        public void BacklinksAndLinksMatchTitlesAcrossCaseInEveryScript(string title, string typed)
        {
            using var store = new TempStore();
            long target = NoteStore.Create(title);
            long linker = NoteStore.Create("linker");
            NoteStore.SetLinks(linker, new[] { typed });

            var back = Assert.Single(Program.Backlinks(store.DbPath, target, 10));
            Assert.Equal(linker, back.Id);
            var link = Assert.Single(Program.Links(store.DbPath, linker, 10));
            Assert.True(link.Resolved);
            Assert.Equal(target, link.Id);
        }

        [Theory]
        [InlineData("to_do", "to-do")]
        [InlineData("50%", "50x")]
        [InlineData(@"a\b", "ab")]
        public void TagCountsCoverOnlyTheExactTag(string tag, string lookalike)
        {
            using var store = new TempStore();
            NoteStore.AddTag(tag, "");
            NoteStore.AddTag(lookalike, "");
            NoteStore.SetNoteTags(NoteStore.Create("exact"), tag);
            NoteStore.SetNoteTags(NoteStore.Create("exact again"), tag);
            NoteStore.SetNoteTags(NoteStore.Create("other"), lookalike);

            var counts = Program.Tags(store.DbPath).ToDictionary(item => item.Name, item => item.Count);
            Assert.Equal(2, counts[tag]);
            Assert.Equal(1, counts[lookalike]);
        }

        [Fact]
        public void GetNeverCutsASurrogatePairInHalf()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("emoji");
            NoteStore.Save(id, "emoji", new byte[] { 1 }, "ab\U0001F600cd");

            var note = Program.Get(store.DbPath, id, 3);

            Assert.True(note.Truncated);
            Assert.Equal("ab", note.Content);
            Assert.False(char.IsHighSurrogate(note.Content[note.Content.Length - 1]));
        }
    }
}
