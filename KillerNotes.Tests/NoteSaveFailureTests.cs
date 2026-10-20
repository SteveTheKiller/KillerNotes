using System.Collections.Generic;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public class NoteSaveFailureTests
    {
        [Theory]
        [InlineData("notes", "UPDATE")]
        [InlineData("note_links", "INSERT")]
        [InlineData("sketches", "INSERT")]
        public void FailedSaveKeepsTheWholeCommittedNoteAndCanBeRetried(string table, string operation)
        {
            using var store = new TempStore();
            long id = NoteStore.Create("original");
            NoteStore.Save(id, "original", new byte[] { 1 }, "original text");
            NoteStore.SetLinks(id, new[] { "original link" });
            NoteStore.SaveSketches(id, new Dictionary<int, byte[]> { [0] = new byte[] { 2 } });
            using var fault = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = store.DbPath }.ConnectionString);
            fault.Open();
            using var command = fault.CreateCommand();
            command.CommandText = $"CREATE TRIGGER fail_save BEFORE {operation} ON {table} BEGIN SELECT RAISE(ABORT, 'controlled save failure'); END";
            command.ExecuteNonQuery();
            Assert.Throws<SqliteException>(() => NoteStore.SaveEditorState(id, "edited", new byte[] { 3 }, "edited text",
                new[] { "edited link" }, new Dictionary<int, byte[]> { [0] = new byte[] { 4 } }));
            Assert.Equal(new byte[] { 1 }, NoteStore.LoadContent(id));
            Assert.Equal("original", Assert.Single(NoteStore.List()).Title);
            Assert.Equal(new byte[] { 2 }, NoteStore.LoadSketches(id)[0]);
            Assert.Empty(NoteStore.ListHistory(id));
            command.CommandText = "DROP TRIGGER fail_save";
            command.ExecuteNonQuery();
            NoteStore.SaveEditorState(id, "edited", new byte[] { 3 }, "edited text", new[] { "edited link" },
                new Dictionary<int, byte[]> { [0] = new byte[] { 4 } });
            Assert.Equal(new byte[] { 3 }, NoteStore.LoadContent(id));
            Assert.Equal(new byte[] { 4 }, NoteStore.LoadSketches(id)[0]);
            Assert.Single(NoteStore.ListHistory(id));
        }
    }
}
