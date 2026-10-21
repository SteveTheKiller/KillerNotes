using System.Linq;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Library search and wikilink resolution ignore case for every script, not only ASCII, and
    /// treat a precomposed accent and the same letter plus a combining accent as equal. Casing is
    /// the invariant culture's (no Turkish dotless-i rule), the same folding in-note Find uses.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class UnicodeSearchTests
    {
        [Theory]
        [InlineData("CAFÉ AU LAIT", "café")]
        [InlineData("ΩMEGA plan", "ωmega")]
        [InlineData("ПРИВЕТ мир", "привет")]
        [InlineData("Café decomposed", "CAFÉ")]
        [InlineData("café composed", "café")]
        [InlineData("plain ascii", "ASCII")]
        public void TitlesMatchAcrossCaseAndComposition(string title, string query)
        {
            using var store = new TempStore();
            long id = NoteStore.Create(title);
            NoteStore.Create("unrelated");
            Assert.Equal(id, Assert.Single(NoteStore.List(query)).Id);
        }

        [Fact]
        public void BodyAndTagsMatchAndEveryWordMustAppear()
        {
            using var store = new TempStore();
            long body = NoteStore.Create("one");
            NoteStore.Save(body, "one", new byte[] { 1 }, "Notes about ÄRGER and ZÜRICH");
            long tagged = NoteStore.Create("two");
            NoteStore.SetNoteTags(tagged, "Ελλάδα");

            Assert.Equal(body, Assert.Single(NoteStore.List("ärger zürich")).Id);
            Assert.Empty(NoteStore.List("ärger missing"));
            Assert.Equal(tagged, Assert.Single(NoteStore.List("ΕΛΛΆΔΑ")).Id);
        }

        [Fact]
        public void SearchStaysRightWhenAnOlderVersionChangesANote()
        {
            using var store = new TempStore();
            long id = NoteStore.Create("n");
            NoteStore.Save(id, "n", new byte[] { 1 }, "first ZÜRICH");
            Assert.Single(NoteStore.List("zürich"));

            // An older KillerNotes has no fold triggers: it changes the text and the stamp only.
            using (var raw = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = store.DbPath, Pooling = false }.ConnectionString))
            {
                raw.Open();
                using var cmd = raw.CreateCommand();
                cmd.CommandText = "UPDATE notes SET plain = 'now GENÈVE', modified = '2099-01-01 00:00:00' WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }
            Assert.Single(NoteStore.List("genève"));
            Assert.Empty(NoteStore.List("zürich"));
        }

        [Fact]
        public void WikilinksResolveAcrossCase()
        {
            using var store = new TempStore();
            long target = NoteStore.Create("Ünïcode Ωmega");
            long source = NoteStore.Create("source");
            NoteStore.SetLinks(source, WikiLinks.Parse("[[ünïcode ωMEGA]]"));

            Assert.Equal(target, Assert.Single(NoteStore.OutgoingLinks(source)).Id);
            Assert.Contains(NoteStore.Backlinks("ÜNÏCODE ΩMEGA"), b => b.Id == source);
        }
    }
}
