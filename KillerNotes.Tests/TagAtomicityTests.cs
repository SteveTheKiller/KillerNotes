using System;
using System.Linq;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// Renaming or deleting a tag touches the tag definition and every note carrying it. A write
    /// that fails partway must leave the whole library as it was, not a definition that says one
    /// thing while some notes still say another. The failure is a real storage error: a trigger
    /// added through a second connection aborts the update of one chosen note.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class TagAtomicityTests
    {
        [Theory]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        public void AFailedNoteUpdateLeavesTheWholeTagChangeUndone(int failingNote, bool rename)
        {
            using var store = new TempStore();
            NoteStore.AddTag("Alpha", "#ff0000");
            long[] ids = Enumerable.Range(0, 3).Select(i => NoteStore.Create("n" + i)).ToArray();
            foreach (long id in ids) NoteStore.SetNoteTags(id, "Alpha, Keep");

            FailTagUpdateOf(store.DbPath, ids[failingNote]);
            if (rename) Assert.ThrowsAny<SqliteException>(() => NoteStore.RenameTag("Alpha", "Beta"));
            else Assert.ThrowsAny<SqliteException>(() => NoteStore.DeleteTag("Alpha"));

            Assert.Equal(new[] { "Alpha" }, Defined("Alpha", "Beta"));
            Assert.All(ids, id => Assert.Equal("Alpha, Keep", NoteStore.GetNote(id)!.Tags));
        }

        /// <summary>Which of the given names are defined; new databases also carry default color tags.</summary>
        private static string[] Defined(params string[] names) =>
            NoteStore.ListTags().Select(t => t.Name).Where(names.Contains).ToArray();

        [Fact]
        public void ASuccessfulRenameAndDeleteUpdateEveryNote()
        {
            using var store = new TempStore();
            NoteStore.AddTag("Alpha", "#ff0000");
            long[] ids = Enumerable.Range(0, 3).Select(i => NoteStore.Create("n" + i)).ToArray();
            foreach (long id in ids) NoteStore.SetNoteTags(id, "Alpha, Keep");

            NoteStore.RenameTag("Alpha", "Beta");
            Assert.Equal(new[] { "Beta" }, Defined("Alpha", "Beta"));
            Assert.All(ids, id => Assert.Equal("Beta, Keep", NoteStore.GetNote(id)!.Tags));

            NoteStore.DeleteTag("Beta");
            Assert.Empty(Defined("Alpha", "Beta"));
            Assert.All(ids, id => Assert.Equal("Keep", NoteStore.GetNote(id)!.Tags));
        }

        private static void FailTagUpdateOf(string dbPath, long id)
        {
            var csb = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false };
            using var raw = new SqliteConnection(csb.ConnectionString);
            raw.Open();
            using var cmd = raw.CreateCommand();
            cmd.CommandText = $"CREATE TRIGGER fail_tags BEFORE UPDATE OF tags ON notes WHEN NEW.id = {id} " +
                              "BEGIN SELECT RAISE(ABORT, 'injected tag failure'); END;";
            cmd.ExecuteNonQuery();
        }
    }
}
