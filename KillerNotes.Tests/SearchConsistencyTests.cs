using System;
using System.Linq;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Search (1.6.0, KN-P06): one matching rule for the sidebar, Find, the match counts, the
    /// highlights and the CLI; filters in the search box; saved searches kept in the notepad.
    /// Accents count ("cafe" does not find "café"); case and how a letter was typed do not.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class SearchConsistencyTests
    {
        [Fact]
        public void TheQueryParserSeparatesWordsPhrasesAndFilters()
        {
            var q = SearchQuery.Parse("budget \"next quarter\" TAG:work group:\"Q4 plans\" title:draft after:2026-01-05 before:2026-06-30 created:2026-10 in:trash http://x.y odd:thing after:nope");

            Assert.Equal(new[] { "budget", "next quarter", "http://x.y", "odd:thing", "after:nope" }, q.Words);
            Assert.Equal(new[] { "work" }, q.Tags);
            Assert.Equal(new[] { "Q4 plans" }, q.Groups);
            Assert.Equal(new[] { "draft" }, q.TitleWords);
            Assert.Equal(new DateTime(2026, 1, 5), q.After);
            Assert.Equal(new DateTime(2026, 7, 1), q.Before);   // the whole day of the 30th counts
            Assert.Equal((new DateTime(2026, 10, 1), new DateTime(2026, 11, 1)), q.Created);
            Assert.True(q.InTrash);
            Assert.Equal("budget next quarter http://x.y odd:thing after:nope", q.FreeText);
        }

        [Theory]
        [InlineData("created:2026", 2026, 1, 1, 2027, 1, 1)]
        [InlineData("created:2026-02-03", 2026, 2, 3, 2026, 2, 4)]
        public void CreatedTakesAYearAMonthOrADay(string text, int y1, int m1, int d1, int y2, int m2, int d2) =>
            Assert.Equal((new DateTime(y1, m1, d1), new DateTime(y2, m2, d2)), SearchQuery.Parse(text).Created);

        [Fact]
        public void FiltersNarrowTheSidebarAndCombineWithWords()
        {
            using var store = new TempStore();
            long work = Note("Budget draft", "the budget for Q4", "work, money", "Work", "2026-03-01 10:00:00", "2026-03-02 10:00:00");
            long sub = Note("Plan", "budget talk", "work", "Work" + NoteStore.GroupSep + "Q4 plans", "2026-10-02 09:00:00", "2026-10-03 09:00:00");
            long home = Note("Groceries", "budget for food", "home", "Home", "2025-12-01 08:00:00", "2026-07-15 08:00:00");
            long gone = Note("Old budget", "budget", "work", "", "2025-01-01 08:00:00", "2025-01-01 08:00:00");
            NoteStore.Trash(gone);

            Assert.Equal(new[] { work, sub }, Ids("budget tag:work"));
            Assert.Equal(new[] { work, sub }, Ids("group:work"));                 // a group takes in its subgroups
            Assert.Equal(new[] { sub }, Ids("group:\"q4 plans\""));
            Assert.Equal(new[] { sub }, Ids("group:\"Work/Q4 plans\""));        // a path, / between levels
            Assert.Equal(new[] { work }, Ids("title:draft"));
            Assert.Equal(new[] { sub, home }, Ids("after:2026-07-01"));           // modified
            Assert.Equal(new[] { work }, Ids("before:2026-03-02"));               // the whole day counts
            Assert.Equal(new[] { sub }, Ids("created:2026-10"));
            Assert.Equal(new[] { gone }, Ids("budget in:trash"));
            Assert.Empty(Ids("tag:wor"));                                         // a tag is matched whole
        }

        [Fact]
        public void EveryPlaceAgreesOnCaseCompositionAndScript()
        {
            using var store = new TempStore();
            var fixtures = new[]
            {
                "Café au lait", "CAFÉ NOIR", "Café decomposed", "cafe plain", "Σοφία και ΣΟΦΊΑ",
                "Қазақ тілі", "Tiếng Việt", "Tiếng Việt",
            };
            var ids = fixtures.Select(f => Note(f, f, "", "", "2026-01-01 00:00:00", "2026-01-01 00:00:00")).ToList();

            foreach (string query in new[] { "café", "CAFÉ", "σοφία", "ҚАЗАҚ", "tiếng việt", "cafe" })
            {
                var sidebar = Ids(query).OrderBy(i => i).ToList();
                var counted = ids.Where((id, i) => NoteSearch.CountMatches(fixtures[i], query) > 0).OrderBy(i => i).ToList();
                var cli = KillerNotes.Cli.Program.Search(store.DbPath, query, 50).Select(n => n.Id).OrderBy(i => i).ToList();
                Assert.Equal(sidebar, counted);
                Assert.Equal(sidebar, cli);
            }
            // Accents count: "cafe" finds only the note without one.
            Assert.Equal(new[] { ids[3] }, Ids("cafe"));
            Assert.Equal(new[] { ids[0], ids[1], ids[2] }, Ids("café").OrderBy(i => i));
            Assert.Equal(new[] { ids[6], ids[7] }, Ids("tiếng việt").OrderBy(i => i));
        }

        [Fact]
        public void HighlightsCoverTheOriginalLettersOfADecomposedWord()
        {
            string text = "x Café y";
            var hit = Assert.Single(NoteSearch.FindMatches(text, "CAFÉ"));
            Assert.Equal((2, 5), hit);
            Assert.Equal("Café", text.Substring(hit.Start, hit.Length));
        }

        [Fact]
        public void FiltersAreNeverCountedAsWords()
        {
            Assert.Equal(1, NoteSearch.CountMatches("tag:work and the budget", "tag:work budget"));
            Assert.Equal(1, NoteSearch.CountMatches("next quarter, next week", "\"next quarter\""));
        }

        [Fact]
        public void TheCliTakesTheSameFilters()
        {
            using var store = new TempStore();
            long work = Note("Budget", "budget", "work", "", "2026-01-01 00:00:00", "2026-01-01 00:00:00");
            Note("Budget home", "budget", "home", "", "2026-01-01 00:00:00", "2026-01-01 00:00:00");

            Assert.Equal(new[] { work }, KillerNotes.Cli.Program.Search(store.DbPath, "budget tag:work", 50).Select(n => n.Id));
        }

        [Fact]
        public void SavedSearchesLiveInTheirNotepad()
        {
            using (var store = new TempStore())
            {
                NoteStore.SaveSearch("Work budget", "budget tag:work");
                NoteStore.SaveSearch("Recent", "after:2026-10-01");
                NoteStore.SaveSearch("work BUDGET", "budget tag:work group:Work");   // same name, new query

                Assert.Equal(new[] { ("Recent", "after:2026-10-01"), ("work BUDGET", "budget tag:work group:Work") },
                    NoteStore.ListSavedSearches().OrderBy(s => s.Name));
                NoteStore.DeleteSavedSearch("recent");
                Assert.Single(NoteStore.ListSavedSearches());
            }
            using (new TempStore())
                Assert.Empty(NoteStore.ListSavedSearches());
        }

        [Fact]
        public void TheScopeLineNamesWhatWasSearched()
        {
            string scope = SearchQuery.Parse("budget tag:work in:trash").Describe(key => key);
            Assert.Contains("Str_Search_ScopeAll", scope);
            Assert.Contains("tag:work", scope);
            Assert.Contains("Str_Search_ScopeTrash", scope);
            Assert.Contains("Str_Search_ScopeNoTrash", SearchQuery.Parse("budget").Describe(key => key));
        }

        private static long[] Ids(string query) => NoteStore.List(query).Select(n => n.Id).OrderBy(i => i).ToArray();

        private static long Note(string title, string body, string tags, string group, string created, string modified)
        {
            long id = NoteStore.Create(title, 1);
            NoteStore.SetFormat(id, 1, MarkdownBlob.Encode(body), body);
            NoteStore.SetNoteTags(id, tags);
            NoteStore.SetNoteGroup(id, group);
            NoteStore.SetTimestamps(id, DateTime.Parse(created), DateTime.Parse(modified));
            return id;
        }
    }
}
