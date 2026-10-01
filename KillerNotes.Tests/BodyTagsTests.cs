using System.Linq;
using System;
using KillerNotes.Models;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public class BodyTagsTests
    {
        [Fact]
        public void ParsesGroupsAndDeduplicatesIgnoringCase()
        {
            Assert.Equal(new[] { "Azure", "EntraId", "PIM" },
                BodyTags.Parse("[#Azure, #EntraId,#PIM] and [#azure]"));
        }

        [Theory]
        [InlineData("# Heading\n#Azure")]
        [InlineData("[#Azure,EntraId]")]
        [InlineData("[[#Azure]]")]
        [InlineData("\\[#Azure]")]
        [InlineData("[#Azure](https://example.org)")]
        [InlineData("`[#Azure]`")]
        [InlineData("`` text ` [#Azure] ``")]
        [InlineData("```text\n[#Azure]\n```")]
        [InlineData("~~~\n[#Azure]\n~~~")]
        [InlineData("```\n[#Azure]")]
        [InlineData("    [#Azure]")]
        public void IgnoresOrdinaryTextAndCode(string text)
        {
            Assert.Empty(BodyTags.Parse(text));
        }

        [Fact]
        public void CodeDoesNotHideTagsInFollowingText()
        {
            Assert.Equal(new[] { "PIM" }, BodyTags.Parse("`[#Azure]` and [#PIM]"));
        }

        [Fact]
        public void CaretFindsOnlyTheIndividualTag()
        {
            const string text = "Before [#Azure, #EntraId,#PIM] after";
            Assert.Equal("Azure", BodyTags.At(text, text.IndexOf("Azure") + 2));
            Assert.Equal("EntraId", BodyTags.At(text, text.IndexOf("#EntraId")));
            Assert.Null(BodyTags.At(text, text.IndexOf(',')));
            Assert.Null(BodyTags.At(text, text.IndexOf(']')));
        }

        [Fact]
        public void RecentNotesUseExactTagsAndModificationOrder()
        {
            var notes = Enumerable.Range(1, 8).Select(i => new Note
            {
                Id = i, Tags = "azure", Modified = new DateTime(2026, 1, i)
            }).ToList();
            notes.Add(new Note { Id = 9, Tags = "AzureAD", Modified = new DateTime(2026, 2, 1) });
            notes.Add(new Note { Id = 10, Tags = "Azure", Modified = new DateTime(2026, 2, 1),
                Deleted = new DateTime(2026, 2, 2) });
            Assert.Equal(new long[] { 7, 6, 5, 4, 3 }, BodyTags.RecentNotes(notes, "Azure", 8).Select(n => n.Id));
        }

        [Theory]
        [InlineData("[#", "")]
        [InlineData("[#Az", "Az")]
        [InlineData("[#Azure, #En", "En")]
        [InlineData("[#Azure,#P", "P")]
        public void CompletesOnlyTheCurrentTag(string text, string expected)
        {
            Assert.Equal(expected, BodyTags.CompletionQuery(text, text.Length));
        }

        [Theory]
        [InlineData("#")]
        [InlineData("# Heading")]
        [InlineData("[[#Az")]
        [InlineData("\\[#Az")]
        [InlineData("[#Azure] text #")]
        [InlineData("[ordinary text #")]
        [InlineData("[#Azure,invalid,#")]
        [InlineData("```\n[#Az")]
        [InlineData("    [#Az")]
        public void DoesNotCompleteOutsideTagGroups(string text)
        {
            Assert.Null(BodyTags.CompletionQuery(text, text.Length));
        }

        [Fact]
        public void CompletionWorksBeforeClosingBracketButNotInsideCodeOrMidToken()
        {
            Assert.Equal("Az", BodyTags.CompletionQuery("[#Az]", 4));
            Assert.Null(BodyTags.CompletionQuery("`[#Az]`", 5));
            Assert.Null(BodyTags.CompletionQuery("[#Az](https://example.org)", 4));
            Assert.Null(BodyTags.CompletionQuery("[#Azure]", 4));
        }

        [Fact]
        public void SuggestionsAreAlphabeticalAndNarrowByPrefix()
        {
            string[] tags = { "PIM", "azure", "EntraId", "Azure", "AzureAD", "two words" };
            Assert.Equal(new[] { "azure", "AzureAD", "EntraId", "PIM" }, BodyTags.Suggestions(tags, ""));
            Assert.Equal(new[] { "azure", "AzureAD" }, BodyTags.Suggestions(tags, "AZ"));
            Assert.Empty(BodyTags.Suggestions(tags, "Unknown"));
        }
    }

    [Collection(NoteStoreCollection.Name)]
    public class BodyTagStoreTests
    {
        [Fact]
        public void RemovingBodyTagsPreservesManualAssignments()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("test");
            NoteStore.SetNoteTags(id, "Azure, Work");
            NoteStore.Save(id, "test", new byte[0], "[#Azure,#PIM]");
            var note = Assert.Single(NoteStore.List());
            Assert.Equal("Azure, Work, PIM", note.Tags);
            Assert.Equal("Azure, Work", note.ManualTags);
            NoteStore.Save(id, "test", new byte[0], "No body tags");
            Assert.Equal("Azure, Work", Assert.Single(NoteStore.List()).Tags);
        }

        [Fact]
        public void ManualAssignmentDoesNotCopyBodyTagsIntoStorage()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("test");
            NoteStore.Save(id, "test", new byte[0], "[#Azure]");
            var note = Assert.Single(NoteStore.List());
            TagManager.SetAssigned(note, "Work", true);
            NoteStore.Save(id, "test", new byte[0], "Body tag removed");
            Assert.Equal("Work", Assert.Single(NoteStore.List()).Tags);
        }

        [Fact]
        public void TagsBeyondTheSnippetAreIncludedAndSearchable()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("test");
            NoteStore.Save(id, "test", new byte[0], new string('x', 180) + "\n[#Azure]");
            var note = Assert.Single(NoteStore.List("Azure"));
            Assert.Equal("Azure", note.Tags);
            Assert.Equal(120, note.Snippet.Length);
        }
    }
}
