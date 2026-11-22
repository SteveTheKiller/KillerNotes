using System.Text;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// A cross-note Replace All lands whole or not at all. The version each note keeps of its
    /// text before the replace belongs to that same all-or-nothing write: a replace that fails
    /// must not leave a stray version behind on the notes it never changed.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class ReplaceAllHistoryTests
    {
        [Fact]
        public void AFailedReplaceAllLeavesNoVersionBehind()
        {
            using var store = new TempStore();
            long a = NoteStore.Create("A", Encoding.UTF8.GetBytes("alpha"), "alpha");
            long b = NoteStore.Create("B", Encoding.UTF8.GetBytes("beta"), "beta");
            int before = NoteStore.ListHistory(a).Count;

            // Make the write to B fail the way a full disk or a locked file would.
            var csb = new SqliteConnectionStringBuilder { DataSource = store.DbPath };
            using (var db = new SqliteConnection(csb.ConnectionString))
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = $"CREATE TRIGGER fail_b BEFORE UPDATE OF content ON notes WHEN old.id = {b} " +
                                  "BEGIN SELECT RAISE(ABORT, 'disk full'); END";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            Assert.ThrowsAny<SqliteException>(() => NoteStore.UpdateContents(new[]
            {
                (a, Encoding.UTF8.GetBytes("ALPHA"), "ALPHA"),
                (b, Encoding.UTF8.GetBytes("BETA"), "BETA"),
            }));

            Assert.Equal(before, NoteStore.ListHistory(a).Count);
        }
    }
}
