using System;
using System.IO;
using System.Linq;
using System.Text;
using KillerNotes.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>
    /// The trash (1.3.1): a deleted note leaves every live query, waits under the Trash header,
    /// and comes back whole on restore. Pinning rides in the same file because both are
    /// one-column presentation state on the notes row, added in the same release.
    /// </summary>
    [Collection(NoteStoreCollection.Name)]
    public sealed class TrashAndPinTests
    {
        private static readonly byte[] Blob = Encoding.UTF8.GetBytes("bytes standing in for a package");

        // ---- Trash ----

        [Fact]
        public void TrashHidesANoteFromTheListAndShowsItInTheTrash()
        {
            using var _ = new TempStore();
            long keep = NoteStore.Create("Keep");
            long bin = NoteStore.Create("Bin");
            NoteStore.Save(bin, "Bin", Blob, "findable words");

            NoteStore.Trash(bin);

            Assert.Equal([keep], NoteStore.List().Select(n => n.Id));
            Assert.Empty(NoteStore.List("findable"));   // search never answers from the trash
            var trashed = Assert.Single(NoteStore.ListTrash());
            Assert.Equal(bin, trashed.Id);
            Assert.True(trashed.IsDeleted);
            Assert.False(NoteStore.List().Single().IsDeleted);
        }

        [Fact]
        public void RestoreBringsTheNoteBackWithItsGroupTagsAndContent()
        {
            using var _ = new TempStore();
            long id = NoteStore.Create("Rack");
            NoteStore.Save(id, "Rack", Blob, "rack layout");
            NoteStore.AddGroup("Work");
            NoteStore.SetNoteGroup(id, "Work");
            NoteStore.SetNoteTags(id, "Red");
            NoteStore.SaveSketches(id, new System.Collections.Generic.Dictionary<int, byte[]> { [0] = [1, 2, 3] });

            NoteStore.Trash(id);
            NoteStore.Restore(id);

            var row = Assert.Single(NoteStore.List());
            Assert.Equal(id, row.Id);
            Assert.Equal("Work", row.Notebook);
            Assert.Equal("Red", row.Tags);
            Assert.False(row.IsDeleted);
            Assert.Equal(Blob, NoteStore.LoadContent(id));
            Assert.Equal([1, 2, 3], NoteStore.LoadSketches(id)[0]);
            Assert.Empty(NoteStore.ListTrash());
        }

        [Fact]
        public void ATrashedNoteNeitherLinksOutNorCanBeLinkedTo()
        {
            using var _ = new TempStore();
            long hub = NoteStore.Create("Hub");
            long spoke = NoteStore.Create("Spoke");
            NoteStore.SetLinks(spoke, ["Hub"]);
            NoteStore.SetLinks(hub, ["Spoke"]);

            NoteStore.Trash(spoke);

            // Spoke no longer resolves, no longer appears as a backlink of Hub, and its own
            // edge is gone from the graph; Hub's link to it is a ghost again.
            Assert.Equal(-1, NoteStore.ResolveTitle("Spoke"));
            Assert.Empty(NoteStore.Backlinks("Hub"));
            var edge = Assert.Single(NoteStore.AllLinks());
            Assert.Equal(hub, edge.SrcId);
            Assert.Equal(-1, edge.DstId);
            Assert.DoesNotContain(NoteStore.TitlesStartingWith("Sp"), t => t.Id == spoke);

            NoteStore.Restore(spoke);
            Assert.Equal(spoke, NoteStore.ResolveTitle("Spoke"));
            Assert.Single(NoteStore.Backlinks("Hub"));
            Assert.Equal(2, NoteStore.AllLinks().Count);
        }

        [Fact]
        public void DeleteForeverDropsTheRowAndItsPayloads()
        {
            using var _ = new TempStore();
            long id = NoteStore.Create("Gone");
            NoteStore.SaveSketches(id, new System.Collections.Generic.Dictionary<int, byte[]> { [0] = [9] });
            NoteStore.SetLinks(id, ["Somewhere"]);
            NoteStore.Trash(id);

            NoteStore.DeleteForever(id);

            Assert.Empty(NoteStore.ListTrash());
            Assert.Null(NoteStore.CaptureRow(id));
            Assert.Empty(NoteStore.LoadSketches(id));
            Assert.Empty(NoteStore.AllLinks());
        }

        [Fact]
        public void PurgeDropsOnlyNotesOlderThanTheWindow()
        {
            using var store = new TempStore();
            long old = NoteStore.Create("Old");
            long recent = NoteStore.Create("Recent");
            NoteStore.Trash(old);
            NoteStore.Trash(recent);

            // Age one row by hand: the store has no API for a deletion in the past, and the
            // purge is keyed on the stamp string, so an UPDATE through a second connection is
            // the honest way to make a month pass.
            var csb = new SqliteConnectionStringBuilder { DataSource = store.DbPath };
            using (var db = new SqliteConnection(csb.ConnectionString))
            {
                db.Open();
                using var cmd = db.CreateCommand();
                cmd.CommandText = "UPDATE notes SET deleted = '2020-01-01 00:00:00' WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", old);
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            Assert.Equal(1, NoteStore.PurgeTrash(NoteStore.TrashDays));

            var left = Assert.Single(NoteStore.ListTrash());
            Assert.Equal(recent, left.Id);
            Assert.Null(NoteStore.CaptureRow(old));
        }

        [Fact]
        public void EmptyTrashLeavesLiveNotesAlone()
        {
            using var _ = new TempStore();
            long keep = NoteStore.Create("Keep");
            NoteStore.Trash(NoteStore.Create("A"));
            NoteStore.Trash(NoteStore.Create("B"));

            Assert.Equal(2, NoteStore.EmptyTrash());
            Assert.Empty(NoteStore.ListTrash());
            Assert.Equal([keep], NoteStore.List().Select(n => n.Id));
        }

        [Fact]
        public void CaptureAndRestoreRowCarryTheTrashStamp()
        {
            using var _ = new TempStore();
            long id = NoteStore.Create("Snap");
            NoteStore.Trash(id);

            var row = NoteStore.CaptureRow(id);
            Assert.NotNull(row);
            Assert.NotEqual("", row!.Deleted);

            NoteStore.DeleteForever(id);
            NoteStore.RestoreRow(row);

            // Restored verbatim, so it lands back in the trash rather than among the live notes.
            Assert.Empty(NoteStore.List());
            Assert.Equal(id, Assert.Single(NoteStore.ListTrash()).Id);
        }

        [Fact]
        public void ImportSkipsTheSendersTrash()
        {
            string shared = Path.Combine(Path.GetTempPath(), "KillerNotesTests", Guid.NewGuid().ToString("N") + ".kndb");
            try
            {
                using (var sender = new TempStore())
                {
                    NoteStore.Create("Shared");
                    NoteStore.Trash(NoteStore.Create("Thrown away"));
                    NoteStore.Close();
                    File.Copy(sender.DbPath, shared);
                }

                using var receiver = new TempStore();
                Assert.Equal(1, NoteStore.ImportNotes(shared, null));
                Assert.Equal("Shared", Assert.Single(NoteStore.List()).Title);
                Assert.Empty(NoteStore.ListTrash());
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try { File.Delete(shared); } catch { /* best effort */ }
            }
        }

        // ---- Pinned ----

        [Fact]
        public void PinnedRoundTripsAndSurvivesADeleteUndo()
        {
            using var _ = new TempStore();
            long id = NoteStore.Create("Pinned");
            Assert.False(NoteStore.List().Single().Pinned);

            NoteStore.SetPinned(id, true);
            Assert.True(NoteStore.List().Single().Pinned);

            var row = NoteStore.CaptureRow(id);
            Assert.True(row!.Pinned);
            NoteStore.Delete(id);
            NoteStore.RestoreRow(row);
            Assert.True(NoteStore.List().Single().Pinned);

            NoteStore.SetPinned(id, false);
            Assert.False(NoteStore.List().Single().Pinned);
        }

        [Fact]
        public void PinningDoesNotTouchModified()
        {
            using var _ = new TempStore();
            long id = NoteStore.Create("Quiet");
            NoteStore.SetTimestamps(id, new DateTime(2026, 1, 1), new DateTime(2026, 1, 2));
            NoteStore.SetPinned(id, true);
            Assert.Equal(new DateTime(2026, 1, 2), NoteStore.List().Single().Modified);
        }
    }
}
