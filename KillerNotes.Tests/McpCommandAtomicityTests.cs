using System.Linq;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    [Collection(NoteStoreCollection.Name)]
    public sealed class McpCommandAtomicityTests
    {
        [Fact]
        public void FailedLinkUpdateLeavesRichNoteAndHistoryUnchanged() => Sta.Run(() =>
        {
            using var store = new TempStore();
            long id = NoteStore.Create("Original");
            byte[] original = MarkdownBlob.Encode("Original [[Before]]");
            NoteStore.Save(id, "Original", original, "Original [[Before]]");
            NoteStore.SetLinks(id, new[] { "Before" });
            var history = NoteStore.ListHistory(id).Select(h => h.Id).ToArray();
            using (var raw = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = store.DbPath, Pooling = false }.ConnectionString))
            {
                raw.Open();
                using var command = raw.CreateCommand();
                command.CommandText = "CREATE TRIGGER fail_links BEFORE INSERT ON note_links " +
                    "BEGIN SELECT RAISE(ABORT, 'injected link failure'); END";
                command.ExecuteNonQuery();
            }
            var shell = new ShellProbe();

            Assert.Throws<SqliteException>(() => shell.Window.ExecuteMcpCommand(new[]
                { "update", "--id", id.ToString(), "--title", "Changed", "--content", "New [[After]]" }));

            Assert.Equal(0, NoteStore.GetFormat(id));
            Assert.Equal("Original", NoteStore.GetNote(id)!.Title);
            Assert.Equal(original, NoteStore.LoadContent(id));
            Assert.Equal(new[] { "Before" }, NoteStore.OutgoingLinks(id).Select(l => l.Title));
            Assert.Equal(history, NoteStore.ListHistory(id).Select(h => h.Id));
        });
    }
}
